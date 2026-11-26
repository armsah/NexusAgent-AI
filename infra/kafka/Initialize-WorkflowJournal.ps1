$ErrorActionPreference = "Stop"

$sql = Get-Content (
    Join-Path $PSScriptRoot "workflow-event-journal.sql"
) -Raw

$sql | docker exec -i nexusagent-postgres `
    psql -X -v ON_ERROR_STOP=1 -U nexusagent -d nexusagent

if ($LASTEXITCODE -ne 0) {
    throw "Workflow event journal initialization failed."
}