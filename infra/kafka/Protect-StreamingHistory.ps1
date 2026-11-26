param(
    [string]$Container = "nexusagent-kafka"
)

$ErrorActionPreference = "Stop"

$topics = @(
    "nexusagent.documents.v1",
    "nexusagent.workflows.v1"
)

foreach ($topic in $topics) {

    & docker exec $Container `
        /opt/kafka/bin/kafka-configs.sh `
        --bootstrap-server localhost:9092 `
        --entity-type topics `
        --entity-name $topic `
        --alter `
        --add-config "cleanup.policy=delete,retention.ms=-1,retention.bytes=-1"

    if ($LASTEXITCODE -ne 0) {
        throw "Retention protection failed: $topic"
    }

    $configuration = & docker exec $Container `
        /opt/kafka/bin/kafka-configs.sh `
        --bootstrap-server localhost:9092 `
        --entity-type topics `
        --entity-name $topic `
        --describe

    if ($LASTEXITCODE -ne 0) {
        throw "Retention verification failed: $topic"
    }

    $text = $configuration -join "`n"

    foreach ($required in @(
        "cleanup.policy=delete",
        "retention.ms=-1",
        "retention.bytes=-1"
    )) {
        if (-not $text.Contains($required)) {
            throw "Missing configuration: $topic / $required"
        }
    }

    Write-Host "Protected source history: $topic"
}
