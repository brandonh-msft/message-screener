# Repository Default Instructions

Apply the digital twin overlay by default for all implementation, refactor, review, and planning tasks in this repository.

These instructions are for repo development only. Product runtime AI assets live under `src/MessageScreener.Api/config/` and `src/MessageScreener.Api/config/copilot-runtime/`.

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
- Commit work continuously in small, atomic commits as each logical unit is completed.
- Do not wait to batch all changes at the end of a session; keep the branch in a regularly committed state.
