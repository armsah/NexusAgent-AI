# E738-05 — Acceptance topic isolation

Each acceptance run receives a UUID-derived namespace containing
document, workflow, analytics, dead-letter and tenant-analytics topics.

Production topic names remain unchanged.

Cleanup is restricted to explicitly owned acceptance topics. The
broker-level infrastructure test verifies creation and deletion of
five dedicated topics and preservation of pre-existing topic names.

The existing live acceptance scenarios still require integration
with the isolated topic configuration, isolated application IDs and
verified internal-topic and consumer-group cleanup.

Preservation of pre-existing topic names does not establish
record-level preservation. That requires additional acceptance.
