# Digital Twin Overlay

This overlay standardizes how engineering work is executed in this repository.

## Goals

- Build for production safety by default.
- Prefer managed identity over secrets wherever possible.
- Make observability first-class with OpenTelemetry traces, metrics, and logs.
- Use source-generated structured logging for runtime paths.
- Avoid hidden side effects and automatic send/deploy behaviors.

## Guardrails

- Never enable implicit auto-send behaviors for user-facing communication flows.
- Require explicit user action for outbound operations.
- Preserve least-privilege scope and document each new permission request.
- Keep data handling tenant-scoped and auditable.

## Implementation Defaults

- Platform baseline: .NET 10 and modern C# features.
- Auth baseline: managed identity first; secrets only when unavoidable.
- Telemetry baseline: OpenTelemetry at startup with consistent resource attributes.
- Logging baseline: source-generated logging with stable event IDs and no sensitive payload logging.
- Delivery baseline: phased delivery with measurable gates and explicit rollback plan.
- Commit baseline: agents must commit incrementally in small, atomic commits as each logical unit of work completes.

## Definition of Ready For New Work

- Acceptance criteria are explicit and testable.
- Security/privacy constraints are listed.
- Observability expectations are listed (signals and success metrics).
- Failure modes and fallback behavior are defined.

## Definition of Done

- Tests cover expected behavior and key failure paths.
- Telemetry is emitted for success, error, timeout, and retry paths.
- Permissions and data flows are documented.
- No hidden automation path can perform user-impacting sends.
