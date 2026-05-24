# Digital Twin Implementation Checklist

Use this checklist when implementing features.

## Architecture

- [ ] Feature scope is phase-tagged (v1/v2 or equivalent).
- [ ] Explicit no-auto-send/no-implicit-side-effect behavior is verified.
- [ ] Failure and retry strategy is documented.

## Security and Identity

- [ ] Managed identity path implemented or intentionally deferred with rationale.
- [ ] Least-privilege permissions reviewed and mapped to requirement IDs.
- [ ] Secrets (if any) stored in approved secret store only.

## Observability

- [ ] Traces include workflow span boundaries and key attributes.
- [ ] Metrics include latency, error rate, timeout, and retry counters.
- [ ] Logs are source-generated and redact sensitive payloads.

## Quality

- [ ] Unit tests cover normal and failure paths.
- [ ] Integration tests cover external dependency boundaries.
- [ ] Telemetry assertions exist for critical workflows.

## Release

- [ ] Rollout plan includes rollback and monitoring thresholds.
- [ ] Runbook updated with operational steps and known failure signals.
