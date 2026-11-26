# E738-04 authoritative publication remediation

## Verified historical investigation

- Total publication records: 159.
- Non-stream publications: 37.
- Unresolved lifecycle publications: 122.
- Surviving matching document and version records: zero.
- Independently verified historical wire events: zero.
- Repository-local backup candidates: zero.

The investigation does not establish that external backups
are unavailable or that all historical records reached Kafka.

## Implemented safeguards

The document publisher persists the complete semantic wire
envelope following acknowledged Kafka delivery.

The authoritative inventory exporter operates read-only.

The executable Java inventory gate validates event contracts
and relational identity and denies incomplete migration.

## Acceptance

- Java targeted tests: 32/32 passed.
- Failures, errors and skips: zero.
- Actual incomplete inventory rejection: verified.

## Remaining limitations

- PostgreSQL persistence follows Kafka acknowledgement,
  leaving a publication crash window.
- Database envelope validation is not independent Kafka
  history verification.
- Successful live isolated authoritative migration remains
  outstanding.
- Production migration is not authorized.

E738-04 remains PARTIAL.

Cloud expenditure: EUR 0.
