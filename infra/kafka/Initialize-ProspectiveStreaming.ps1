param(
    [switch]$Provision
)

$ErrorActionPreference = "Stop"

$topics = @(
    "nexusagent.documents.v2",
    "nexusagent.workflows.v2",
    "nexusagent.analytics.v2",
    "nexusagent.events.dlq.v2",
    "nexusagent.tenant-analytics.v2"
)

function Check($operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed."
    }
}

$existing = @(
    docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 --list
)

Check "Broker inventory"

$present = @(
    $topics | Where-Object {
        $existing -contains $_
    }
)

if ($present.Count -gt 0 -and
    $present.Count -ne $topics.Count) {

    throw "Partial prospective generation requires review."
}

if ($present.Count -eq 0 -and -not $Provision) {

    Write-Host "Prospective generation is not provisioned."
    Write-Host "Use -Provision to create the isolated generation."
    return
}

if ($present.Count -eq 0) {

    foreach ($topic in $topics) {

        $cleanup = "delete"

        if ($topic -eq "nexusagent.tenant-analytics.v2") {
            $cleanup = "compact"
        }

        docker exec nexusagent-kafka `
            /opt/kafka/bin/kafka-topics.sh `
            --bootstrap-server localhost:9092 `
            --create `
            --topic $topic `
            --partitions 3 `
            --replication-factor 1 `
            --config "cleanup.policy=$cleanup" `
            --config retention.ms=-1 `
            --config retention.bytes=-1

        Check "Prospective topic creation: $topic"
    }
}

foreach ($topic in $topics) {

    docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --describe --topic $topic

    Check "Prospective topic inspection: $topic"
}

Write-Host "Prospective topic inventory verified."
Write-Host "No legacy topic was modified."
Write-Host "No producer routing or production cutover was performed."
