& {
    $ErrorActionPreference = "Stop"

    docker exec nexusagent-kafka `
        /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --create `
        --if-not-exists `
        --topic nexusagent.tenant-analytics.v1 `
        --partitions 3 `
        --replication-factor 1 `
        --config cleanup.policy=compact

    if ($LASTEXITCODE -ne 0) {
        throw "Tenant analytics topic creation failed."
    }

    $description = @(
        docker exec nexusagent-kafka `
            /opt/kafka/bin/kafka-topics.sh `
            --bootstrap-server localhost:9092 `
            --describe `
            --topic nexusagent.tenant-analytics.v1
    )

    if ($LASTEXITCODE -ne 0) {
        throw "Tenant analytics topic verification failed."
    }

    $text = $description -join "`n"

    if ($text -notmatch "PartitionCount:\s*3" -or
        $text -notmatch "cleanup.policy=compact") {
        throw "Tenant analytics topic configuration mismatch."
    }

    $description | Write-Host
}
