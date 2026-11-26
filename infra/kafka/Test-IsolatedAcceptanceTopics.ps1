param(
    [string]$Container = "nexusagent-kafka"
)

$ErrorActionPreference = "Stop"

$run = [Guid]::NewGuid().ToString("N")
$prefix = "nexus-acceptance-$run"

$topics = @(
    "$prefix.documents",
    "$prefix.workflows",
    "$prefix.analytics",
    "$prefix.dlq",
    "$prefix.tenant-analytics"
)

$created = New-Object System.Collections.Generic.List[string]

function Invoke-Topics {
    param([string[]]$Arguments)

    $output = & docker exec $Container `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Kafka topic operation failed."
    }

    return $output
}

$before = @(Invoke-Topics -Arguments @("--list"))

try {

    foreach ($topic in $topics) {

        if ($before -contains $topic) {
            throw "Acceptance topic collision."
        }

        $cleanup = "delete"

        if ($topic.EndsWith(".tenant-analytics")) {
            $cleanup = "compact"
        }

        Invoke-Topics -Arguments @(
            "--create",
            "--topic", $topic,
            "--partitions", "3",
            "--replication-factor", "1",
            "--config", "cleanup.policy=$cleanup"
        )

        $created.Add($topic)
    }

    $current = @(Invoke-Topics -Arguments @("--list"))

    foreach ($topic in $topics) {

        if ($current -notcontains $topic) {
            throw "Acceptance topic missing: $topic"
        }
    }

    Write-Host "Five isolated acceptance topics verified."
}
finally {

    foreach ($topic in $created) {

        if ($topics -notcontains $topic) {
            throw "Cleanup ownership verification failed."
        }

        Invoke-Topics -Arguments @(
            "--delete", "--topic", $topic
        )
    }

    $deadline = (Get-Date).AddSeconds(60)

    do {

        $after = @(Invoke-Topics -Arguments @("--list"))

        $remaining = @(
            $created | Where-Object {
                $after -contains $_
            }
        )

        if ($remaining.Count -eq 0) {
            break
        }

        Start-Sleep -Seconds 1

    } while ((Get-Date) -lt $deadline)

    if ($remaining.Count -ne 0) {
        throw "Acceptance topic cleanup incomplete."
    }

    foreach ($topic in $before) {

        if ($after -notcontains $topic) {
            throw "Pre-existing topic disappeared: $topic"
        }
    }

    Write-Host "Acceptance topic cleanup verified."
    Write-Host "Pre-existing topic names preserved."
}
