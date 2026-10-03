# Data model

## 1. Canonical transaction

The canonical finance fields mirror the monthly Excel table.

```text
Transaction
- transactionId : UUID        # system identity, stored in hidden metadata
- revision      : integer     # system revision, hidden metadata
- date          : local date
- type          : string      # constrained by Master
- paymentMethod : string      # optional; empty string or a Master value
- category      : string      # constrained by Master
- amountIdr     : integer     # positive whole rupiah amount
- description   : string?     # optional
```

Important:

- `transactionId` and `revision` are not added to the visible `A:F` monthly table.
- `amountIdr` must not use binary floating point.
- transaction local date determines target year/month.

Only payment and description are optional. Blank payment is stored as `""` and
a blank Excel C cell, without a placeholder Master entry or settlement status.
The owner may record an advanced expense now and fill payment on that same
transaction later. This is an UPDATE, not another expense; monthly totals count
the original positive amount regardless of payment. Date, type, category and
positive whole-IDR amount remain required. Nonblank payment must match Master.
Existing non-null Room/DTO fields remain compatible; no local schema migration.

## 2. Excel visible row mapping

```text
A Tanggal
B Tipe
C Pembayaran
D Kategori
E Jumlah (Rp)
F Keterangan
```

## 3. `__AppIndex`

Hidden system-sheet schema:

```text
TransactionId
Year
Month
LastKnownExcelRow
Revision
ContentHash
SnapshotJson
IsDeleted
CreatedAtUtc
UpdatedAtUtc
DeletedAtUtc
```

Purpose:

- stable application identity;
- idempotency;
- optimistic revision;
- external-import reconciliation;
- tombstone for deleted IDs;
- before-state reference.

`SnapshotJson` contains only the canonical transaction fields needed for reconciliation/audit, not arbitrary workbook data.

`LastKnownExcelRow` is a hint, never the sole identity mechanism.

## 4. `__AuditLog`

Hidden system-sheet schema:

```text
AuditId
TransactionId
Action
Source
OccurredAtUtc
RevisionBefore
RevisionAfter
BeforeJson
AfterJson
UserId
DeviceId
CorrelationId
```

Actions:

- CREATE
- UPDATE
- DELETE
- IMPORT_RECONCILE
- SYSTEM_MIGRATION

Sources:

- ANDROID
- EXCEL_IMPORT
- SYSTEM

The audit trail is part of the workbook export.

## 5. Android Room model

Suggested conceptual entities:

### `LocalTransaction`

```text
transactionId
serverRevision?
date
type
paymentMethod
category
amountIdr
description?
syncState
lastSyncError?
updatedLocallyAt
```

`syncState`:

- SYNCED
- PENDING
- SYNCING
- FAILED

### `OutboxOperation`

```text
operationId
transactionId
operationType  # CREATE / UPDATE / DELETE
expectedRevision?
payloadJson?
createdAt
attemptCount
lastAttemptAt?
lastError?
```

The outbox must survive process death/reboot.

An operation and its local transaction state must be committed atomically in Room where applicable.

## 6. Auth SQLite conceptual model

### User

```text
Id
UsernameNormalized
PasswordHash
CreatedAtUtc
DisabledAtUtc?
```

### Device

```text
Id
UserId
DeviceLabel
CreatedAtUtc
LastSeenAtUtc
RevokedAtUtc?
```

### RefreshSession

```text
Id
UserId
DeviceId
RefreshTokenHash
CreatedAtUtc
LastUsedAtUtc
IdleExpiresAtUtc
AbsoluteExpiresAtUtc
RevokedAtUtc?
ReplacedBySessionId?
```

No finance transaction rows belong in this DB.

## 7. Master data DTO

Read from workbook `Master`:

```text
MasterData
- transactionTypes[]
- paymentMethods[]
- categories[]
    - name
    - kind
    - monthlyBudgetIdr?
- workbookYear
- schemaVersion
```

Android caches Master data for offline form dropdowns.

## 8. Transient Smart Scan extraction

Smart Scan uses a versioned, model-independent in-memory result before a
transaction becomes a local draft:

```text
TransactionExtraction
- schemaVersion
- documentKind
- date             : ExtractedField<local-date-string>
- amountIdr        : ExtractedField<positive integer IDR>
- transactionType  : ExtractedField<string>
- paymentMethod    : ExtractedField<string>
- category         : ExtractedField<string>
- merchant         : ExtractedField<string>
- description      : ExtractedField<string>
- warnings[]

ExtractedField<T>
- value            : T?
- alternatives[]   : T
- confidence       : HIGH / MEDIUM / LOW / MISSING
- evidence         : string?
```

The extraction is untrusted and is not a Room entity, outbox payload, server
record, or workbook row. A deterministic validator first canonicalizes
transaction type, payment method, and category against cached Master data. An
invalid or unsupported value becomes `null` with a blocking issue. Valid
partial fields may prefill the ordinary transaction editor, but all required
fields still need human review before Save.
