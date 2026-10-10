# ADR 0009 — Browser monthly transaction mutations

**Status:** Accepted for the requested monthly CRUD slice, 2026-10-10. Supersedes only the read-only transaction restriction in ADR 0008; its separate browser session remains in effect.

The owner requested monthly web CRUD after reviewing the editing research. Browser commands live under `/web/books/{year}/transactions`, use the existing `LajurWeb` cookie policy plus antiforgery, and share `IFinanceService` and the atomic, revisioned XLSM writer with Android. `/api/v1` continues to accept only Android bearer authentication. Audit source `WEB` records the authenticated user and request correlation ID, leaving DeviceId empty rather than inventing a phone identity.

Reports expose UUID/revision only when an index entry's physical location and finance content match the visible row. Legacy/unreconciled rows remain readable with edit/delete disabled until import reconciliation; GET never migrates a workbook. Duplicate-content rows keep distinct identities. Every write invalidates the parsed year cache under the workbook lock.

The browser commits one complete draft transaction, retains failed drafts in tab memory, and uses the same create UUID on retry. Update/delete conflicts require review of current server data; they never silently overwrite. An uncertain response requires a read of the target before retry, with exact content comparison for update recovery. This is state reconciliation, not a new general command-idempotency framework. Monetary input and browser command responses use decimal strings; row editing/formatting uses integer parsing and BigInt.

Initial scope is monthly add/edit/delete/duplicate, same-year month movement, keyboard entry and responsive forms. Master, budget, projection inputs, bulk paste and restore/undo remain separate slices. Six A:F finance columns and the workbook preservation boundary are unchanged; no SQL finance mirror, browser offline outbox or new frontend framework is introduced.

Owner-approved fast-entry refinement (2026-10-10): validated creates enter a FIFO
queue in tab memory and immediately release the form. Each entry snapshots its
complete input and stable UUID; one worker sends commands serially. A rejected
or uncertain entry pauses later commands for explicit correction/reconciliation,
while new input remains available. Edit/delete wait until pending creates finish.
Only confirmed writes appear in canonical totals/table; pending entries have
separate statuses. The queue is capped at 50 unfinished entries and is not a
durable/offline browser outbox. Leaving warns while queued entries remain.
