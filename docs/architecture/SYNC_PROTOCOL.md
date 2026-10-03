# Sync protocol

## 1. Goal

The Android app must accept local writes offline and synchronize exactly-once in effect when connectivity returns.

Network requests themselves may be delivered more than once. Server operations therefore must be idempotent/revision-aware.

## 2. Local state

A locally saved transaction is immediately usable in the UI with a sync state.

States:

- SYNCED
- PENDING
- SYNCING
- FAILED

`FAILED` should distinguish retryable/transient from user-action-required errors internally.

## 3. Create

Android generates `transactionId` before local save.

Outbox CREATE contains the complete transaction.

Server semantics:

```text
ID absent
  → append workbook row
  → revision 1
  → return transaction

ID present + same canonical content
  → return existing transaction
  → do not append a duplicate row

ID present + different content
  → 409 transaction_id_conflict
```

Acceptance test: sending the same create 10 times leaves exactly one canonical workbook row.

## 4. Update

Outbox UPDATE contains:

- transaction ID;
- expected revision;
- complete desired canonical fields.

Server:

```text
current revision == expected
  → update
  → revision + 1
  → audit
else
  → 409
```

Do not auto-merge finance fields.

## 5. Delete

Outbox DELETE contains:

- transaction ID;
- expected revision.

Server performs hard removal from visible table plus metadata tombstone/audit.

A repeated delete of the same already-deleted transaction can return a stable idempotent success if the request matches the tombstone semantics.

## 6. Outbox compaction

Allowed simplifications before synchronization:

- CREATE then local UPDATE → one CREATE with latest payload;
- CREATE then local DELETE before any server acknowledgement → cancel both and remove local draft;
- multiple local UPDATEs → keep latest desired state with the original expected server revision when safe.

Do not compact across an acknowledged server revision without updating the base revision.

## 7. Worker ordering

For one account, process outbox in deterministic creation order.

Only one sync worker should actively mutate the account at a time.

The backend additionally serializes per-workbook writes.

## 8. Retry classes

### Retry automatically

- network unavailable;
- timeout;
- HTTP 5xx;
- transient server busy.

Use WorkManager exponential backoff with sane limits.

### Refresh/auth path

- expired access token → refresh;
- revoked/expired refresh session → mark account requires login; do not delete pending local operations.

### User action required

- revision conflict;
- master value no longer valid;
- workbook schema/import problem;
- permanent validation failure.

Do not infinitely retry permanent 4xx validation errors.

## 9. Pull/reconciliation

On app start/resume or successful sync, refresh current-month canonical transactions as needed.

The app does not need continuous realtime sockets.

A simple model is enough:

- load cached current month instantly;
- fetch canonical month when online;
- merge server canonical rows with still-pending local outbox intent.

## 10. One-device simplification

v1 one-active-device policy removes most genuine concurrent-device edit cases.

Revision checking remains required because:

- retries occur;
- future multi-device expansion should remain possible;
- export/edit/import may change canonical data;
- stale local state can exist after long offline periods.

## 11. Excel import and Android cache

Successful workbook import is a canonical replacement event.

After import:

- server increments/changes a workbook generation/version identifier;
- Android invalidates relevant cached canonical rows;
- pending unsynchronized operations require explicit safe handling.

Recommended v1 behavior: reject/disable workbook replacement import while the current device reports unsynchronized outbox operations, or require the user to sync/discard them first.

Current Android menu enforces an empty **actual outbox** (including failed/delete
operations) under a gate shared with remote sync/pull and local mutation. It never
discards pending work. The selected year's synchronized rows/Master are invalidated
before upload and refreshed afterward; this also covers an ambiguous committed
import with a lost response. A failed import may temporarily remove that year's
local canonical cache until refetch. The current backend transport does not expose
a workbook-generation identifier, so the version-based check above remains a
future reconciliation enhancement, not a currently implemented guarantee.
