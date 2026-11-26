$ErrorActionPreference = "Stop"

$compose = Join-Path $PSScriptRoot "compose.yaml"
$manifest = Get-Content (Join-Path $PSScriptRoot "topics.json") -Raw |
    ConvertFrom-Json

docker compose -p nexusagent-kafka -f $compose config --quiet

if ($LASTEXITCODE -ne 0) {
    throw "Invalid Kafka Compose configuration."
}

docker compose -p nexusagent-kafka -f $compose up -d --wait --wait-timeout 180

if ($LASTEXITCODE -ne 0) {
    docker logs --tail 100 nexusagent-kafka
    throw "Kafka startup failed."
}

foreach ($topic in $manifest.topics) {
    docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --create `
        --if-not-exists `
        --topic $topic.name `
        --partitions $topic.partitions `
        --replication-factor 1 `
        --config "retention.ms=$($topic.retentionMs)" `
        --config "cleanup.policy=delete"

    if ($LASTEXITCODE -ne 0) {
        throw "Topic creation failed: $($topic.name)"
    }

    $description = docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --describe `
        --topic $topic.name

    if ($LASTEXITCODE -ne 0) {
        throw "Topic inspection failed."
    }

    $description | Write-Host

    $text = $description -join "`n"

    if ($text -notmatch "PartitionCount:\s*$($topic.partitions)\b" -or
        $text -notmatch "ReplicationFactor:\s*1\b" -or
        $text -notmatch "retention.ms=$($topic.retentionMs)\b") {
        throw "Topic configuration mismatch: $($topic.name)"
    }
}

Write-Host "KAFKA INITIALIZATION PASSED" -ForegroundColor Green

# Provision the compacted tenant analytics snapshot topic.
& (Join-Path $PSScriptRoot "Initialize-TenantAnalytics.ps1")
