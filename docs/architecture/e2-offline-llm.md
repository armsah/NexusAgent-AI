# E2 - Offline LLM and Python AI Service

## Architecture

NexusAgent supports local AI inference through a Python FastAPI
microservice backed by Ollama. The C# application retains responsibility
for orchestration, authorization, governance and persistence.

| Component | Technology |
|---|---|
| Agent governance | C# / .NET 10 |
| AI inference service | Python 3.12 / FastAPI |
| Local generation | qwen2.5:3b |
| Local embeddings | qwen3-embedding:4b |
| Embedding dimensions | 1536 |
| Communication | HTTP REST / JSON |
| Persistence | PostgreSQL / pgvector |

## Implemented capabilities

- Local text generation with bounded output.
- Semantic embedding generation and validation.
- Schema-constrained grounded generation.
- Citation allow-list validation.
- Empty-evidence abstention.
- C# HTTP adapters for generation and embeddings.
- Opt-in LocalPython dependency injection configuration.
- Loopback-only configured service communication.
- Existing Azure OpenAI provider compatibility.
- No database schema migration required.

## Run locally

Install Python 3.12 and Ollama.

Download the required local models:

    ollama pull qwen2.5:3b
    ollama pull qwen3-embedding:4b

From the repository root:

    cd services/nexusagent-ai
    py -3.12 -m venv .venv
    .\.venv\Scripts\python.exe -m pip install -r requirements.txt
    .\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8001

In a separate PowerShell terminal at the repository root,
configure the local providers:

    $env:Embeddings__Provider = "LocalPython"
    $env:GroundedAnswers__Provider = "LocalPython"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    dotnet run --project src/NexusAgent.Api

Existing database and document storage configuration is required.

## REST endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /health/live | Process liveness |
| POST | /v1/generate | Text generation |
| POST | /v1/embed | Semantic embeddings |
| POST | /v1/grounded | Evidence-based generation |

## Verification

- Python regression: 19 tests passed.
- Existing .NET regression: 250 tests passed.
- Final .NET Release build and regression command succeeded.
- Real local text generation passed.
- Real 1536-dimensional embedding generation passed.
- Real grounded generation passed.
- Live C# to Python to Ollama communication passed.
- Cross-service citation and abstention checks passed.

Run Python verification from services/nexusagent-ai:

    .\.venv\Scripts\python.exe -m pytest -q
    .\.venv\Scripts\python.exe smoke_test.py

Run .NET regression from the repository root:

    dotnet test NexusAgent.slnx -c Release

## Limitations and operational considerations

- Local inference endpoints are intended for trusted development use.
- Authentication and concurrency controls remain pending.
- Citation membership does not establish factual entailment.
- Existing documents require reindexing after changing embedding models.
- Reindexing can replace previous document embeddings.
- End-to-end PostgreSQL indexing and retrieval with real local
  embeddings remain unverified.
- Permanent C# adapter-specific tests remain pending.
- Cross-service acceptance used a temporary console project.
- Python tests emit a non-blocking Starlette deprecation warning.
- Dependency installation and initial model downloads require internet.
- No Azure resources were provisioned or invoked.

## Next stage

E3 introduces Supervisor, Knowledge, Policy and Verifier agents
using the governed agent harness and local inference capabilities.