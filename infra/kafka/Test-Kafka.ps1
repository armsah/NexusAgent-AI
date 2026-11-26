$ErrorActionPreference = "Stop"

$topic = "nexusagent.acceptance." + [Guid]::NewGuid().ToString("N")
$payload = "nexusagent-e7-" + [Guid]::NewGuid().ToString("N")
$created = $false

try {
    docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --create `
        --topic $topic `
        --partitions 1 `
        --replication-factor 1

    if ($LASTEXITCODE -ne 0) {
        throw "Acceptance topic creation failed."
    }

    $created = $true

    $payload | docker exec -i nexusagent-kafka `
        /opt/kafka/bin/kafka-console-producer.sh `
        --bootstrap-server localhost:9092 `
        --topic $topic `
        --producer-property acks=all `
        --producer-property enable.idempotence=true

    if ($LASTEXITCODE -ne 0) {
        throw "Kafka publication failed."
    }

    $received = docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-console-consumer.sh `
        --bootstrap-server localhost:9092 `
        --topic $topic `
        --from-beginning `
        --max-messages 1 `
        --timeout-ms 15000

    if ($LASTEXITCODE -ne 0) {
        throw "Kafka consumption failed."
    }

    if (($received -join "`n").Trim() -ne $payload) {
        throw "Kafka payload mismatch."
    }

    Write-Host "Publication and consumption: PASSED"

    docker restart nexusagent-kafka

    if ($LASTEXITCODE -ne 0) {
        throw "Broker restart failed."
    }

    $ready = $false

    for ($i = 0; $i -lt 60; $i++) {
        $health = docker inspect `
            --format '{{.State.Health.Status}}' `
            nexusagent-kafka

        if ($health -eq "healthy") {
            $ready = $true
            break
        }

        Start-Sleep -Seconds 2
    }

    if (-not $ready) {
        throw "Broker restart readiness failed."
    }

    $received = docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-console-consumer.sh `
        --bootstrap-server localhost:9092 `
        --topic $topic `
        --from-beginning `
        --max-messages 1 `
        --timeout-ms 15000

    if ($LASTEXITCODE -ne 0 -or
        ($received -join "`n").Trim() -ne $payload) {
        throw "Persistent replay failed."
    }

    Write-Host "Broker restart and persistent replay: PASSED"
}
finally {
    if ($created) {
        docker exec nexusagent-kafka `
            /opt/kafka/bin/kafka-topics.sh `
            --bootstrap-server localhost:9092 `
            --delete `
            --topic $topic

        if ($LASTEXITCODE -ne 0) {
            throw "Acceptance topic cleanup failed."
        }
    }
}

Write-Host "KAFKA ACCEPTANCE PASSED" -ForegroundColor Green