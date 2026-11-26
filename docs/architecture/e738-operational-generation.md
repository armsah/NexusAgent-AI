# Prospective operational streaming generation

## Architecture

Legacy generation v1 remains the default.

Prospective generation v2 has distinct source topics, output topics,
dead-letter topic, tenant analytics topic and application identity.

Cross-generation application and topic bindings are rejected.

The runtime selects its generation using NEXUS_STREAM_GENERATION.

## Provisioning

The local prospective namespace can be provisioned using:

infra/kafka/Initialize-ProspectiveStreaming.ps1 -Provision

Provisioning does not migrate producer routing or historical records.

Explicit pristine initialization remains required.

## Historical disposition

Legacy snapshots are preserved and are not eligible for import into
the prospective generation through the disposition policy.

Unverified historical candidates are not classified as proven
synthetic records and are not authorized for deletion.

## Acceptance

Targeted regression: 90 passed.
Failures, errors and skips: zero.

## Outstanding integration

Live prospective runtime initialization, restart and restoration
must still be verified.

Producer routing must be integrated before operational cutover.

The historical disposition policy must be connected to the actual
snapshot consumption or export boundary.

Residual topic ownership remains unverified.

E738-04 and E738-06 remain open.

Cloud expenditure: EUR 0.
