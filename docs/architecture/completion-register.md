# Project 7 - Mandatory Completion Register

## Definition of Done

The project cannot be declared complete until every agreed requirement
has been implemented, integrated, tested and documented.

Every open item requires closure evidence, including relevant tests,
acceptance results and GitHub commit or pull request references.

Deferred work is not completed work.
Failed or skipped required acceptance tests block final completion.
No required feature may remain a placeholder.

## Outstanding implementation and verification register

| ID | Requirement | Target | Status |
|---|---|---|---|
| CR-001 | Permanent C# inference adapter tests | E3 | OPEN |
| CR-002 | Live PostgreSQL local embedding indexing and retrieval | E3 | OPEN |
| CR-003 | Four concrete agents and governed coordination | E3 | OPEN |
| CR-004 | Trusted identity and agent authorization integration | E3/E10 | OPEN |
| CR-005 | Typed evidence handoffs and citation verification | E3 | OPEN |
| CR-006 | Durable agent execution state and recovery | E4 | OPEN |
| CR-007 | Agent memory and bounded planning | E4 | OPEN |
| CR-008 | Retry idempotency and cancellation verification | E4 | OPEN |
| CR-009 | Durable human approval state | E4 | OPEN |
| CR-010 | Versioned evaluation datasets and benchmarks | E5 | OPEN |
| CR-011 | Adversarial prompt-injection and grounding tests | E5 | OPEN |
| CR-012 | Inference authentication and concurrency controls | E5 | OPEN |
| CR-013 | Request limits and inference timeout enforcement | E5 | OPEN |
| CR-014 | Python dependency deprecation remediation | E5 | OPEN |
| CR-015 | Java functional event processing service | E6 | OPEN |
| CR-016 | Kafka and Kafka Streams integration | E7 | OPEN |
| CR-017 | NoSQL agent artifact persistence | E8 | OPEN |
| CR-018 | Angular monitoring and approval interface | E9 | OPEN |
| CR-019 | REST integration and contract testing | E10 | OPEN |
| CR-020 | Production authentication and endpoint authorization | E10 | OPEN |
| CR-021 | Docker and Kubernetes deployment | E11 | OPEN |
| CR-022 | Ansible configuration automation | E12 | OPEN |
| CR-023 | GitHub Actions and Jenkins CI/CD | E13 | OPEN |
| CR-024 | Distributed telemetry and trace export | E13/E14 | OPEN |
| CR-025 | Azure infrastructure and Foundry validation | E14 | OPEN |
| CR-026 | Cloud cost attribution and budget controls | E14 | OPEN |
| CR-027 | Production networking and deployment security | E14 | OPEN |
| CR-028 | Load, failure, recovery and rollback testing | E15 | OPEN |
| CR-029 | Original P0-P19 requirements verification | E15 | OPEN |
| CR-030 | Full regression and documentation audit | E15 | OPEN |
| CR-031 | Four-language architecture coverage verification | E15 | OPEN |
| CR-032 | Final cloud resource and cost cleanup verification | E15 | OPEN |
| CR-033 | Governed multi-agent endpoint acceptance and isolation | E3 | OPEN |
| CR-034 | Hybrid retrieval relevance and citation integrity | E3/E5 | OPEN |
| CR-035 | Production request-size enforcement verification | E10/E15 | OPEN |
| CR-036 | Agent invocation authorization boundary verification | E3 | OPEN |

## Closure procedure

1. Implement the required capability.
2. Add permanent automated tests where applicable.
3. Execute required live integration and acceptance tests.
4. Record actual results and remaining limitations.
5. Update documentation and README.
6. Commit, push and merge through GitHub.
7. Record closure evidence before marking the item CLOSED.

Newly discovered deficiencies must be added to this register.

Azure operations require explicit cost approval before execution.
Budget restrictions do not justify marking unverified requirements complete.

Final acceptance requires zero OPEN or BLOCKED required items.

## E3 acceptance evidence

- Release build and full .NET regression passed.
- Python regression: 27 tests passed.
- Live local embedding, indexing, retrieval and generation passed.
- Governed endpoint and direct executor authorization tests passed.
- Production and Staging endpoint isolation passed.
- No required acceptance tests were intentionally skipped.
- Azure expenditure: EUR 0.
- E3 closure requires the GitHub merge reference.

CR-004, CR-034 and CR-035 retain their later-stage requirements.
CR-014 remains open for dependency deprecation remediation.
