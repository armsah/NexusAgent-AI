# E738-06 — Existing synthetic snapshot reconciliation

Earlier live acceptance runs published synthetic events into shared
topics and generated compacted tenant analytics snapshots.

Previously published source and accepted-history records are preserved.

A tenant-name prefix alone is not sufficient evidence that a tenant
is synthetic. Reconciliation requires an exact verified tenant
manifest and comparison with authoritative PostgreSQL tenant data.

The candidate inventory script is read-only. It does not publish
tombstones, delete records or reset application offsets.

The classification policy preserves authoritative and unrelated
tenants and requires review when the authoritative inventory is
incomplete.

Actual reconciliation remains gated on a complete verified inventory,
proven synthetic provenance and protection against snapshot
republication by active stream processors.
