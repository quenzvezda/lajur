# API contract

## Android workbook reports

`GET /api/v1/reports?year=2026` requires Android bearer authentication and returns
the same `YearReport` workbook-derived schema used by the web report. It contains
12 monthly reports, category budgets/amounts, canonical A:F rows, balance inputs,
spending pace, cash/income-source projections and annual totals. Monetary fields
are integer IDR. Today uses the configured report time zone (default Asia/Jakarta).
Only the authenticated account's workbook is read; query year is 1900–9999.
Missing workbook returns 404, invalid year 400, unauthenticated requests 401.
Responses are no-store; Android explicitly manages its private offline snapshot.
Read-only reports do not mutate XLSM or trust stale cached Excel formula values.

## Local registration and recovery (ADR 0012)

All account responses are `no-store`; request bodies are limited to 4 KiB.
Account mutation attempts share a per-IP rate limit of 10 per 10 minutes.

| Method/path under `/api/v1/auth` | Authorization | Body/result |
| --- | --- | --- |
| GET `registration` | Anonymous | `{mode: invite/closed/open}` |
| POST `register` | Anonymous | `username`, `password`, `invitationCode`; returns `username`, eight `codes` once |
| POST `recover` | Anonymous | `username`, `code`, `newPassword`; 204 then normal login; generic 400 on failure |
| POST `password` | Android bearer | current `password`, `newPassword`; 204; all sessions ended |
| POST `recovery-codes` | Android bearer | current `password`; returns replacement `codes` once |
| POST `invitations` | Android bearer | current `password`; returns `code`, `expiresAtUtc` once |

Browser forms `/daftar`, `/pulihkan`, `/akun` call the same domain service with
Razor antiforgery and the independent browser cookie. API account commands do
not accept the web cookie as a bearer credential. Passwords remain 12–256 chars;
client confirmation is checked by web/Android. Recovery cancels the old code
set, sessions and outstanding invitations; no workbook content is changed.

This is a behavioral contract, not a generated OpenAPI file. The implementation should generate an OpenAPI document from ASP.NET Core.

Base prefix:

`/api/v1`

All authenticated endpoints use Bearer access tokens.

This applies to `/api/v1`. Browser routes are separately described in section 9;
their cookie never authenticates the Android API.

Use `application/problem+json`/ProblemDetails-style errors.

## 1. Conventions

### Time

Server/audit timestamps: UTC ISO-8601.

Transaction `date`: date-only local calendar value (`YYYY-MM-DD`).

### Money

Whole IDR integer:

```json
{ "amountIdr": 128500 }
```

No floating-point currency.

### IDs

Use UUID strings.

Client generates `transactionId` before local save, enabling offline creation/idempotency.

## 2. Auth

### `POST /auth/login`

```json
{
  "username": "rama",
  "password": "...",
  "deviceId": "optional-previous-server-device-uuid",
  "deviceLabel": "POCO F6"
}
```

`deviceId` is omitted on the first login and retained by Android for subsequent
logins on the same installation. A missing, unknown, or cross-account device ID
creates a new server device identity; it is never trusted as authorization.

Response:

```json
{
  "accessToken": "...",
  "accessTokenExpiresAtUtc": "...",
  "refreshToken": "...",
  "refreshTokenIdleExpiresAtUtc": "...",
  "refreshTokenAbsoluteExpiresAtUtc": "...",
  "device": {
    "id": "...",
    "label": "POCO F6"
  }
}
```

A new successful device login applies the v1 one-active-device policy.

### `POST /auth/refresh`

Refresh-token rotation. Old token becomes unusable.

### `POST /auth/logout`

Revoke current refresh session.

### `GET /me`

Returns user/device/session and onboarding state.

### Optional admin/account endpoints

- password change;
- list/revoke devices;
- operator CLI for account creation/reset.

Do not build public registration unless requirements change.

## 3. Master

### `GET /master?year=2026`

Returns types, payment methods, categories/budgets, workbook schema/version.

Android caches it for offline use.

## 4. Summary

### `GET /summary?year=2026&month=9`

Calculated from canonical transaction rows, not formula caches.

Possible response:

```json
{
  "year": 2026,
  "month": 9,
  "incomeIdr": 355000,
  "expenseIdr": 2874523,
  "transactionCount": 57
}
```

No requirement to reproduce the full Excel dashboard.

## 5. Transactions

### `GET /transactions?year=2026&month=9&query=coffee`

Pagination is optional initially because monthly transaction volume is modest, but the API design should not require returning all years at once.

Until legacy workbook migration runs in Phase 4, this endpoint returns only
application-owned rows represented in `__AppIndex`. Summary totals still include
all valid visible A:F transaction rows.

Response item:

```json
{
  "transactionId": "uuid",
  "revision": 3,
  "date": "2026-09-19",
  "type": "Pengeluaran",
  "paymentMethod": "Shopee Pay",
  "category": "Kebutuhan",
  "amountIdr": 85000,
  "description": "..."
}
```

### `POST /transactions`

Request:

```json
{
  "transactionId": "client-generated-uuid",
  "date": "2026-09-19",
  "type": "Pengeluaran",
  "paymentMethod": "Shopee Pay",
  "category": "Kebutuhan",
  "amountIdr": 85000,
  "description": "..."
}
```

Semantics:

- For create and update, `paymentMethod` is optional: omitted, JSON `null`,
  empty or whitespace-only input is normalized to `""`. Responses always use
  a string (empty when unspecified); nonblank values must match Master.
  Date, type, category and positive whole-IDR amount remain required.
  Filling/clearing payment edits the original transaction and does not change
  expense totals or create a settlement record.
- first successful create → revision 1;
- retry of identical `transactionId` + equivalent content → return existing transaction successfully;
- same ID with conflicting content → `409 Conflict`.

An `Idempotency-Key` header equal to transaction ID may also be accepted, but transaction ID is the durable identity.

### `PUT /transactions/{id}?year=2026`

```json
{
  "expectedRevision": 3,
  "date": "2026-09-19",
  "type": "Pengeluaran",
  "paymentMethod": "Shopee Pay",
  "category": "Makanan-Jadi",
  "amountIdr": 65000,
  "description": "..."
}
```

If canonical revision differs: `409 Conflict`.

`year` identifies the current workbook containing the transaction. Moving a
transaction to another month in the same year is supported. Moving it across
years is rejected in v1 and must be represented as delete + create.

Successful update increments revision by exactly one and appends audit history.

### `DELETE /transactions/{id}?year=2026&expectedRevision=3`

The query contains the current workbook year and `expectedRevision`.

Delete:

- removes the visible row from monthly table;
- marks metadata tombstone;
- records full before-state in audit log.

No canonical soft-deleted row remains in the monthly table.

## 6. Workbooks

### `GET /workbooks`

Lists the authenticated account's available years with file size and last-modified UTC timestamp.

### `POST /workbooks/{year}/create`

Generate from approved template if absent.

Generation clears template example rows, patches only the known `Rekap` title and
monthly `DATE(...)` helper formulas, initializes very-hidden application metadata,
requests full calculation on the next Excel open, validates the result, and then
promotes it atomically. It fails with `workbook_already_exists` rather than
overwriting an existing year.

### `POST /workbooks/import`

Multipart upload.

Form fields:

- `file`: required `.xlsm` upload;
- `year`: optional confirmation; the server also infers the year from canonical
  rows, known helper formulas, and `Rekap` text and rejects disagreement;
- `replaceExisting`: must be `true` to replace an existing year;
- `confirmNoPendingChanges`: must be `true`; Android sets this only after its
  local outbox is empty or the user has explicitly resolved pending work.

Required behavior:

- stage;
- validate;
- migrate/reconcile;
- backup existing active year if replacing;
- atomically promote;
- return validation/migration summary.

Never replace the active workbook after a failed validation.

Uploads are copied into account-scoped staging with compressed and expanded-size,
entry-count, path, package-part, VBA, sheet/table/header, Master, year, and row
validation. Compatible legacy rows receive deterministic transaction IDs and
revision 1. Existing metadata is reconciled by content hash first and row location
second. One `SYSTEM_MIGRATION` audit summarizes an import instead of fabricating a
historical CREATE event for every legacy row.

Before year detection and migration, an import preflight checks every non-blank
row of the twelve monthly tables with the migration's own rules and reports all
problems at once (`422`, code `workbook_import_invalid`). `detail` is an
Indonesian, user-facing list (first 25 lines) that Android shows as-is; the
`issues` extension carries the full machine-readable list:

```json
{
  "status": 422,
  "code": "workbook_import_invalid",
  "detail": "Workbook belum bisa diimpor: 1 baris perlu diperbaiki di Excel, lalu impor ulang.
• Sep baris 24 (kolom D): Kategori 'Lainnya' terdaftar sebagai Pemasukan di Master, tetapi baris ini bertipe Pengeluaran.",
  "issues": [
    { "sheet": "Sep", "row": 24, "column": "D", "cell": "D24", "message": "Kategori 'Lainnya' terdaftar sebagai Pemasukan di Master, tetapi baris ini bertipe Pengeluaran." }
  ]
}
```

Issue messages never contain amounts or descriptions. Structural problems (missing
sheet/table/header, broken date helpers, conflicting year) are also reported as
`workbook_import_invalid` with a single explanatory `detail`.

### `GET /workbooks/{year}/export`

Downloads actual `.xlsm`.

### `GET /workbooks/export-all`

Returns zip of annual `.xlsm` workbooks.

Exports contain the complete workbook package, including VBA and the application
audit/index sheets.

## 7. Device/session management

Suggested:

- `GET /devices`
- `DELETE /devices/{id}`

May be deferred from the first UI but backend semantics should allow revocation.

## 8. Errors

Examples:

- `400` invalid DTO;
- `401` expired/invalid access session;
- `403` disabled/revoked user/device;
- `404` canonical transaction/year absent;
- `409` revision or transaction-ID conflict;
- `422` workbook/domain validation error;
- `429` login rate limit;
- `503` `workbook_unavailable`: the workbook is busy (file contention); retrying later can help;
- `503` `server_storage_unavailable`: a server storage area (for example import staging) is
  read-only, missing, full or not permitted. This is server configuration, not the user's
  workbook, and retrying does not help; readiness (`/health/ready`) fails at the same time.

Problem body should contain stable machine code, e.g.:

```json
{
  "type": "...",
  "title": "Transaction revision conflict",
  "status": 409,
  "code": "transaction_revision_conflict",
  "traceId": "..."
}
```

## 9. Browser monthly transaction commands

Base `/web/books/{year}` uses the `LajurWeb` cookie policy. Unsafe methods also
require `X-Lajur-CSRF` with the antiforgery token from the book page. GETs never
write or reconcile metadata; all reads/writes are isolated to the logged-in user.
Browser endpoints do not create/revoke Android sessions. See ADR 0009.

- `GET /report`: server-computed annual report; canonical rows include nullable
  `transactionId`, `revision`, and exact `amountIdrText`. Null identity means the
  row is readable but requires import reconciliation before edit/delete.
- `GET /transactions/{id}`: current indexed transaction, or 404. Browser responses
  use a decimal **string** for `amountIdr`, allowing exact integer recovery.
- `POST /transactions`: `{ "transactionId": "uuid", "input": { "date": "2026-10-10",
  "type": "Pengeluaran", "paymentMethod": "", "category": "...",
  "amountIdr": "15000", "description": "..." } }`.
- `PUT /transactions/{id}`: `{ "expectedRevision": 1, "input": { ... } }`.
- `DELETE /transactions/{id}?expectedRevision=1`: revisioned deletion, 204.

Input follows the same Master/type/category/payment constraints as Android;
amount is a positive whole-IDR decimal string within Int64, date must be in the
route year. POST retries use the original UUID and canonical content. PUT/DELETE
use expected revision and return the existing 409/404 domain codes. A lost PUT
response is reconciled by reading the target and comparing exact content before
retrying; no silent overwrite or automatic conflict resolution is promised.

Monthly UI restricts new drafts to the displayed month; editing can move an
existing row to another month in the same year with confirmation. Financial
responses are no-store. Guarded category/payment commands are documented below.

## 10. Browser structured workbook settings

Base `/web/books/{year}/settings` uses the same browser cookie, antiforgery and
no-store policies (ADR 0010). All IDR inputs/responses are decimal strings or
null, distinct from zero. Cell addresses and formulas are never client inputs.

- `GET /budget`: `{ version, categories: [{ category, amountIdr, editable, reason }] }`.
- `PUT /budget`: `{ operationId, expectedVersion, changes: [{ category, amountIdr }] }`.
  A batch commits atomically. Only supported expense-category targets are writable;
  one monthly target is shared by Jan–Dec in that annual book.
- `GET /projection/{month}`: `{ month, version, sources: [{ id, label, payoutDay,
  estimateIdr, isMealAllowance, workDays, dailyRateIdr, editable, reason }] }`.
- `PUT /projection/{month}`: `{ operationId, expectedVersion, changes: [{ id,
  payoutDay, estimateIdr, workDays, dailyRateIdr }] }`. Meal entries require
  estimateIdr=null; other entries require workDays/dailyRateIdr=null. Payout day
  is nullable 1–31, workdays nullable 0–31, money nullable nonnegative Int64;
  computed estimates and totals must also fit. Successful PUT returns fresh settings.
- `GET /operations/{operationId}`: receipt `{ operationId, scope }`, or 404. Scope
  is `BUDGET` or `PROJECTION_<month>`. An accepted receipt survives later edits.

Unsupported/formula inputs are read-only. Unknown IDs, duplicate targets, invalid
values and aggregate overflow reject the whole batch. Settings versions are
independent of transaction revisions. Stale versions or UUID reuse with different
payloads return 409 `settings_revision_conflict`; exact command retry returns
current settings without another audit/write effect. Browser uncertainty is
inspected by receipt before retrying the original command. Owner/year lock,
pre-settings backup, validated temporary workbook and atomic promotion apply.

## 11. Guarded browser Master commands

- `GET /web/books/{year}/master`: `{ version, categories, payments,
  categoryCapacity, paymentCapacity, canAddCategory, canAddPayment,
  categoryReason, paymentReason }`. Each entry exposes name/kind/budgetIdr/icon,
  transactionCount/historyCount/dependencyCount, canRename/canDelete and reason.
- `PUT /web/books/{year}/master`: `{ operationId, expectedVersion, entity,
  action, originalName, name, kind, budgetIdr, icon }`. Entity is category/payment;
  action is create/update/delete. New entries have originalName=null; delete uses
  originalName and no replacement fields. Category kinds are Pengeluaran/Pemasukan;
  expense budget is nullable exact nonnegative IDR text. Payment icons are optional
  cash/bank/wallet/qr/shopping/card/other; null chooses the name-based display default.
- Successful command returns current management snapshot. Identical retry is
  idempotent; receipt GET from section 10 has scope `MASTER`. Version/UUID conflict
  returns 409; invalid/unsafe/capacity-dependent operations return 422 with detail.

Same browser cookie/CSRF/tenant/no-store policies apply. Identity changes/deletes
recheck ledger/history/native dependencies under lock. Used payment icons and
category budgets remain editable while identities stay locked. Budget must be
unset before category deletion. Known list bindings stay contiguous; no cascade
or offline-command remapping occurs. A verified backup and complete managed
native LajurMaster report accompany successful writes. Existing Android Master
GET exposes current lists without changing its compatible DTO; browser reports
add paymentIcons mapping. Stale Android commands fail validation for user review.
