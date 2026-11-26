# E3 - Governed multi-agent orchestration

## Architecture

The coordinator executes four bounded stages:

1. Supervisor: validates the request.
2. Policy: rejects prohibited or suspicious instructions.
3. Knowledge: retrieves authorized evidence and invokes local inference.
4. Verifier: independently retrieves evidence and validates citations.

Built-in agents cannot be invoked through the public agent executor.
The coordinator uses the internal governed execution path.

## Retrieval and grounding

PostgreSQL full-text search and pgvector cosine similarity provide
hybrid retrieval using reciprocal rank fusion.

Retrieval checks tenant, document ACL, active status, content version,
embedding model and the persisted workflow question.

Python normalizes citations only against exact evidence sentences.
The C# verifier independently rejects unsupported extractive claims,
unknown citations and forged evidence.

## Development endpoint

POST /api/workflows/{id}/agents/execute

The endpoint uses server-controlled development identity and persisted
workflow ownership. It is unavailable in Production and Staging.

Development identity is not production authentication.

## Local execution

Start PostgreSQL and Ollama. Start the Python inference service:

    cd services/nexusagent-ai
    .\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8001

From a separate repository-root PowerShell terminal:

    $env:Embeddings__Provider = "LocalPython"
    $env:GroundedAnswers__Provider = "LocalPython"
    $env:Agents__EnableDevelopmentExecution = "true"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    dotnet run --project src/NexusAgent.Api

Create a workflow containing a question, then invoke its agent endpoint.
Authorized documents must have current text and vector indexes.

## Acceptance

The permanent acceptance suite covers:

- Four-stage governed execution.
- Tenant and owner isolation.
- Production and Staging endpoint isolation.
- Prompt-injection rejection.
- Direct built-in executor rejection.
- Lexical and hybrid retrieval.
- Real local embedding generation.
- Actual content indexer execution and PostgreSQL persistence.
- Live PostgreSQL, C#, Python and Ollama coordination.
- Extractive citation verification.

The indexer acceptance uses a controlled seekable content stream.
Physical storage and outbox integration retain their separate tests.

Final regression is required before publication.

## Remaining project requirements

Durable agent state, recovery, planning and memory belong to E4.
Semantic relevance evaluation and inference hardening belong to E5.
Production authentication belongs to E10.

Extractive verification does not establish general semantic entailment.
The application boundary is not a sandbox against trusted in-process code.

The Starlette TestClient deprecation remains tracked in CR-014.
No Azure resources were provisioned during E3.

## Final authorization hardening

Multi-agent execution requires explicit development opt-in.
Production and Staging deny execution regardless of configuration.

The authorized executor attaches an internal agent-specific
authorization marker after workflow ownership and policy checks.
All four built-in agents reject unauthorized direct invocation.

This protects ordinary application invocation paths, not arbitrary
malicious trusted code or reflection.

Cumulative corrective regression results:

- C# unit tests: 163 passed.
- Architecture tests: 3 passed.
- Integration tests: 121 passed, including live local inference.
- Python tests: 27 passed.
- Total: 314 passed, zero failed or skipped.

The seven unit fixture corrections were validated separately after
the successful integration and architecture runs.

Production authentication remains assigned to E10.
Dependency deprecation remediation remains assigned to E5.
Azure expenditure: EUR 0.
