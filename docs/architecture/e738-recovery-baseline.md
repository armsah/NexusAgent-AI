# Versioned recovery baseline

## Verified evidence

- 122 legacy publication records remain unresolved.
- Bounded Kafka comparison found zero matching event identities.
- 219 readable records included 38 unparseable envelopes.
- Prospective cross-language publication recovery passed.
- Existing PostgreSQL inventory counts and Kafka boundaries
  remained unchanged during baseline-policy acceptance.

## Recovery policy

The candidate baseline records source boundaries and inventory
counts. It is not a verified materialized-state checkpoint.

Isolated prospective rebuilds require verified source boundaries,
verified prospective history, isolation and production preservation.

Production baseline migration additionally requires verified
baseline state, classified broker records and explicit approval.

Complete historical reconstruction requires complete historical
coverage and zero unresolved or unclassified records.

Synthetic reconciliation requires verified provenance, complete
authoritative tenant inventory and explicit approval.

## Regression

- Java targeted regression: 49 passed.
- C# targeted regression: passed.
- No production application-data changes were requested.

## Outstanding implementation

The policy is implemented and unit tested. Production migration
entry-point integration and materialized baseline verification
remain outstanding.

Malformed broker records require classification.
Synthetic snapshot provenance remains unverified.

E738-04 and E738-06 remain open.
E7.4 and E8 must not be represented as completed.
