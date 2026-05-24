# Repository Default Instructions

Apply the digital twin overlay by default for all implementation, refactor, review, and planning tasks in this repository.

Primary policy sources:
- .digital-twin/overlay.md
- .digital-twin/implementation-checklist.md

Default behavior requirements:
- Use .NET 10 as the baseline when adding or changing service code.
- Prefer managed identity-first authentication for external dependencies.
- Implement OpenTelemetry-first observability (traces, metrics, logs).
- Use source-generated structured logging in runtime paths.
- Enforce production-safe defaults: no hidden auto-send and no implicit side effects.
- Keep permissions least-privilege and document scope rationale.
- Define failure modes, fallback behavior, and validation criteria.

Execution expectation:
- Treat the checklist in .digital-twin/implementation-checklist.md as required quality gates before considering work complete.
