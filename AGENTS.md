# AGENTS.md — Lajur repository instructions

This file is a map, not an encyclopedia. Durable requirements live under `docs/`.

## Mission

Build **Lajur**, an offline-first Android personal-finance entry app backed by a self-hosted ASP.NET Core API. Excel `.xlsm` workbooks are the financial source of truth.

## Read before changing code

1. `ARCHITECTURE.md`
2. `docs/index.md`
3. The relevant subsystem spec.
4. `docs/exec-plans/active/0001-mvp.md` until it is superseded.

## Hard constraints

1. **Financial transaction truth lives in the user's `.xlsm` workbook.**
2. Server SQLite is allowed for auth/infrastructure metadata only; do not mirror the finance ledger into SQL.
3. Android never edits `.xlsm` files directly. It talks to the API.
4. OCR and LLM inference must be local/on-device. Do not add OpenAI, Gemini, Azure AI, AWS Textract, or any cloud inference dependency.
5. Scan results are drafts only. A human must review required fields before save.
6. Android must remain usable for manual entry while offline.
7. Synchronization must be idempotent.
8. Preserve workbook macros/VBA, formulas, tables, defined names, formatting, and unrelated package parts.
9. Do not widen the existing monthly `A:F` transaction tables without an explicit ADR. The real workbook uses columns to the right.
10. v1 assumes one active Android device per account. Different accounts have isolated workbooks.
11. Do not turn Lajur into a full budgeting/accounting suite unless the product spec changes.
12. No mandatory flaky UI end-to-end test suite in v1 CI.

## Preferred repository shape

```text
apps/android/                 # Kotlin Android app
services/api/                 # ASP.NET Core API
tests/workbook-fixtures/      # sanitized XLSM fixtures
docs/                         # system of record
docker-compose.yml
```

Do not create additional top-level architecture without a clear reason.

## Agent workflow

For non-trivial work:

1. Read the relevant spec and current execution plan.
2. State assumptions in the execution plan if the spec is intentionally open.
3. Implement the smallest coherent vertical slice.
4. Run the tests appropriate to the touched subsystem.
5. Update the active execution plan: progress, decisions, surprises, and follow-ups.
6. If a durable architectural decision changes, add/update an ADR instead of silently diverging.
7. Keep documentation synchronized with behavior.

## Change discipline

- Prefer small, reviewable commits/PRs.
- Avoid speculative abstractions before a second implementation needs them.
- Keep boundaries explicit: Android UI/domain/data, API/auth/workbook adapter.
- Never silently “fix” user workbook business formulas.
- Use UTC for server/audit timestamps; use local timezone only for user display and transaction-local date semantics.
- Monetary values are integer IDR amounts; never use binary floating point for money.
- Never derive storage paths from untrusted usernames or uploaded filenames.
- Imported workbooks must be validated before replacing an active workbook.

## Testing minimums

Backend changes:
- unit tests for changed business logic;
- integration tests for API/workbook behavior where relevant;
- workbook preservation tests for any XLSM mutation code.

Android changes:
- JVM/unit tests for changed domain/sync logic;
- Room/instrumentation tests only where they provide concrete value;
- manual smoke test for UI flows affected by the change.

OCR/LLM changes:
- run the evaluation dataset/benchmark when model or extraction behavior changes;
- model quality benchmarks are not required on every ordinary CI commit.

## Definition of done

A task is done when:

- the intended behavior works;
- relevant tests pass;
- no hard constraint above is violated;
- docs/plan are updated where behavior or decisions changed;
- no secrets, real receipts, or private workbook data are committed.

If a requirement is ambiguous, prefer the simplest implementation consistent with the product spec and record the assumption in the active execution plan.
