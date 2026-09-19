# API contract

This is a behavioral contract, not a generated OpenAPI file. The implementation should generate an OpenAPI document from ASP.NET Core.

Base prefix:

`/api/v1`

All authenticated endpoints use Bearer access tokens.

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
  "password": "..."
}
```

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

- first successful create → revision 1;
- retry of identical `transactionId` + equivalent content → return existing transaction successfully;
- same ID with conflicting content → `409 Conflict`.

An `Idempotency-Key` header equal to transaction ID may also be accepted, but transaction ID is the durable identity.

### `PUT /transactions/{id}`

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

Successful update increments revision by exactly one and appends audit history.

### `DELETE /transactions/{id}`

Request body or query contains `expectedRevision`.

Delete:

- removes the visible row from monthly table;
- marks metadata tombstone;
- records full before-state in audit log.

No canonical soft-deleted row remains in the monthly table.

## 6. Workbooks

### `GET /workbooks`

List available years and basic metadata.

### `POST /workbooks/{year}/create`

Generate from approved template if absent.

Must fail rather than overwrite an existing year by default.

### `POST /workbooks/import`

Multipart upload.

Required behavior:

- stage;
- validate;
- migrate/reconcile;
- backup existing active year if replacing;
- atomically promote;
- return validation/migration summary.

Never replace the active workbook after a failed validation.

### `GET /workbooks/{year}/export`

Downloads actual `.xlsm`.

### `GET /workbooks/export-all`

Returns zip of annual `.xlsm` workbooks.

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
- `503` workbook temporarily unavailable/locked only if waiting policy is exhausted.

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
