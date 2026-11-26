# E738-04 — Retention, migration and rebuild safety

## Source retention

Document and workflow event topics use unlimited time and size
retention with delete-only cleanup. Source compaction is prohibited.

Previously expired records cannot be recovered by changing retention.
Existing source completeness must therefore be independently verified
against authoritative PostgreSQL publication history.

## Durable state

Accepted history, identities, latest projections, pending events,
global identities, tenant metrics, tenant metric identities and
reconciliation audit changelogs require compaction-only cleanup.

Independent expiry of identity or accepted-history stores is prohibited.

Unlimited retention increases disk usage. Production deployment requires
capacity monitoring, backup and an approved archival strategy.

## Rebuild and migration

A rebuild requires verified complete source history, authoritative
history verification, an isolated target application and preservation
of production offsets and outputs.

Production consumer-group resets, state deletion and in-place replay
are not authorized by this policy.

New state stores require an explicit migration review. Missing
historical audit records must not be fabricated.

The policy validator and source configuration script implement the
initial safeguards. Runtime enforcement, verified historical
completeness and end-to-end migration acceptance remain required.
