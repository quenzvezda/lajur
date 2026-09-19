# Lajur repository instructions for GitHub Copilot

Read `/AGENTS.md` first. Treat `/docs/` as the system of record.

Key constraints:

- Excel `.xlsm` is the canonical financial store.
- No cloud OCR or cloud LLM inference.
- Android is native Kotlin.
- Backend is ASP.NET Core/.NET 10 LTS.
- SQLite on the server is for auth/infrastructure only, not finance transactions.
- Preserve workbook VBA/macros and unrelated package parts.
- Keep monthly transaction tables at A:F unless an ADR explicitly changes this.
- v1 assumes one active Android device per account.
- Do not introduce mandatory flaky Compose UI E2E tests into the CI gate.

For non-trivial work, update the active execution plan under `docs/exec-plans/active/`.
