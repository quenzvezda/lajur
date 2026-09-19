---
applyTo: "services/api/**"
---

# Backend-specific instructions

- Target .NET 10 LTS unless an ADR changes the runtime.
- Do not create a financial transaction table in SQLite/SQL.
- Auth/session infrastructure may use SQLite.
- Derive user storage paths from server-side immutable IDs, never username or upload filename.
- Serialize workbook writes per user/year.
- All workbook mutations must use temp-file + validate + same-filesystem atomic replacement.
- Preserve `vbaProject.bin`, formulas, defined names, tables, formatting, and unrelated OPC parts.
- Do not rely on formula cached values for API summaries; calculate summaries from canonical transaction rows.
- Create is idempotent by transaction ID. Update/delete require expected revision.
- Use structured problem responses and never return stack traces to clients.
- Imported workbooks must be staged and validated before the active workbook can be replaced.
