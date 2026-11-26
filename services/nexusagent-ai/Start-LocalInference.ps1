$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:NEXUS_INFERENCE_API_KEY) -or
    $env:NEXUS_INFERENCE_API_KEY.Length -lt 32) {
    throw "Configure NEXUS_INFERENCE_API_KEY with a strong shared secret."
}

$python = Join-Path $PSScriptRoot ".venv/Scripts/python.exe"

if (-not (Test-Path $python)) {
    throw "Create the virtual environment and install requirements.txt."
}

Push-Location $PSScriptRoot

try {
    & $python -m uvicorn app.main:app `
        --host 127.0.0.1 --port 8001 --workers 1

    if ($LASTEXITCODE -ne 0) {
        throw "Inference service failed."
    }
}
finally {
    Pop-Location
}