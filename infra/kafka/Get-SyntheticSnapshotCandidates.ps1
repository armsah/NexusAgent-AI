param(
    [string]$Container = "nexusagent-kafka"
)

$ErrorActionPreference = "Stop"

$topic = "nexusagent.tenant-analytics.v1"

$lines = & docker exec $Container `
    /opt/kafka/bin/kafka-console-consumer.sh `
    --bootstrap-server localhost:9092 `
    --topic $topic `
    --from-beginning `
    --timeout-ms 5000 `
    --isolation-level read_committed `
    --property print.key=true `
    --property print.value=true

$latest = @{}

foreach ($line in $lines) {

    $parts = $line -split "`t", 2

    if ($parts.Count -ne 2) {
        throw "Unexpected snapshot record format."
    }

    $key = $parts[0]
    $value = $parts[1]

    if ($value -eq "null") {
        $latest.Remove($key)
        continue
    }

    $snapshot = $value | ConvertFrom-Json

    if ($snapshot.tenantId -ne $key) {
        throw "Snapshot key and tenant identity mismatch."
    }

    $latest[$key] = $snapshot
}

$candidates = @(
    $latest.Keys | Where-Object {
        $_ -match '^e73[67]-tenant-[a-f0-9]{32}$' -or
        $_ -match '^e738-crash-[a-f0-9]{32}$'
    } | Sort-Object
)

$result = [ordered]@{
    observedAtUtc = [DateTime]::UtcNow.ToString("o")
    topic = $topic
    observedSnapshotCount = $latest.Count
    candidateCount = $candidates.Count
    candidates = $candidates
    authoritativeInventoryVerified = $false
    completeInventoryVerified = $false
    destructiveChangesAuthorized = $false
    status = "REVIEW_REQUIRED"
}

$result | ConvertTo-Json -Depth 5
