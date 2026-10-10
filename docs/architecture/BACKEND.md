# Backend architecture

## 1. Runtime

Target **.NET 10 LTS / ASP.NET Core** for v1.

Do not use Microsoft Excel COM/Interop. The deployment target is Linux/Docker.

## 2. Suggested project structure

```text
services/api/
  src/
    Lajur.Api/
    Lajur.Application/
    Lajur.Domain/
    Lajur.Infrastructure/
  tests/
    Lajur.UnitTests/
    Lajur.IntegrationTests/
    Lajur.WorkbookTests/
```

This can be simplified if the agent finds it too ceremony-heavy, but keep the following boundaries explicit:

- HTTP/auth;
- finance domain commands;
- workbook adapter;
- infrastructure persistence.

## 3. Storage layout

Canonical host layout:

```text
/srv/lajur/
  data/
    users/
      <immutable-user-id>/
        workbooks/
          Keuangan_Pribadi_2026.xlsm
          Keuangan_Pribadi_2027.xlsm
  backups/
    <immutable-user-id>/
      2026/
      2027/
  templates/
    finance-template.xlsm
  auth/
    lajur-auth.db
  keys/
  staging/               # transient import uploads, deleted after each request
  logs/                  # optional if not stdout-only
```

Never derive `<immutable-user-id>` from a username or upload filename.

## 3a. Browser workbook UI

`Lajur.Api` also serves Razor Pages (`Pages/`) and static assets (`wwwroot/`) for the browser
viewer (ADR 0008). `IFinanceReportService` reads one consistent year snapshot through
`OpenXmlWorkbookStore.ReadYearAsync` (Master + twelve A:F tables + B1/D1 balance inputs, under the
per-workbook lock) and `FinanceReportCalculator` recomputes the Rekap/monthly aggregates. The
report GETs never write to the workbook and never read cached formula/pivot values.
Browser transaction commands share the finance writer with Android (ADR 0009).
Structured budget/projection commands use `IWorkbookSettingsService`, mapped
literal inputs, independent scope versions and audit operation receipts (ADR
0010). Both use the owner/year lock and atomic XLSM promotion. Settings commands
create pre-settings backups; exact replay does not rewrite the active book.

## 4. Per-workbook serialization

All operations targeting the same user/year workbook must pass through a per-key asynchronous lock.

Lock key:

`(userId, year)`

The lock covers:

- current workbook read if mutation follows;
- temp copy/mutation;
- validation;
- backup decision;
- atomic promotion.

Different users/years may proceed independently.

## 5. Atomic mutation

Do not edit the active file in place.

Required pattern:

1. acquire workbook lock;
2. copy active workbook to temp file on the same filesystem;
3. mutate temp;
4. validate ZIP/OPC/workbook invariants;
5. write/flush temp;
6. create backup according to policy;
7. atomically replace active file using same-filesystem rename;
8. release lock.

Failure before promotion leaves active workbook unchanged.

## 6. Workbook adapter

Expose a narrow interface such as:

```text
GetMasterData(year)
ListTransactions(year, month, query?)
GetTransaction(id)
CreateTransaction(command)
UpdateTransaction(command)
DeleteTransaction(command)
ImportWorkbook(...)
ExportWorkbook(...)
GenerateYear(...)
```

HTTP controllers must not manipulate XML parts directly.

## 7. Formula behavior

Do not rely on cached formula output in `Rekap` or monthly dashboards.

For Android/API summaries:

- parse canonical transaction rows;
- compute totals in application memory.

After writes, preserve formulas and configure workbook calculation properties so desktop Excel can recalculate on open when appropriate.

## 8. Validation layers

### API DTO validation

- syntax/types;
- required fields;
- reasonable limits.

### Domain validation

- amount > 0;
- allowed type/payment/category from Master;
- date/year relationship;
- expected revision.

### Workbook validation

- required sheets;
- required monthly tables;
- table headers;
- VBA package part retained;
- defined names;
- no overlap/corruption caused by mutation;
- system sheets valid.

## 9. Auth DB

SQLite is acceptable and intentionally separate from finance storage.

Use modern password hashing via ASP.NET Core Identity/password hashing facilities rather than custom crypto.

Refresh tokens are high-entropy opaque values; store only a hash server-side.

## 10. Observability

Log structured events:

- request/correlation ID;
- user ID (non-secret internal ID);
- operation;
- workbook year;
- duration;
- result category.

Never log:

- password;
- raw refresh token;
- full receipt OCR text by default;
- full financial transaction payload at info level.

## 11. Health endpoints

Suggested:

- `/health/live`
- `/health/ready`

Readiness checks (implemented):

- auth DB accessible;
- `data`, `backups`, `staging`, `keys` and `auth` under the storage root are writable
  (a probe file is created and deleted; a read-only or missing Docker mount fails readiness).

It never touches a real workbook. Template presence is not part of readiness because a fresh
deployment may legitimately run without one until the operator installs it.

Web-viewer report notes: `ReportClock` supplies the owner's local date for TODAY()-based panels
(`Lajur:Reports:TimeZone`, default `Asia/Jakarta`, fixed UTC+7 fallback when tzdata is absent).
`OpenXmlWorkbookStore` caches one parsed year per workbook keyed by file write time and length,
checked under the per-workbook lock; promotion explicitly invalidates the parsed
year cache rather than relying solely on file stamp resolution.
