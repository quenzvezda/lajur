# Lajur API

ASP.NET Core backend for Lajur, targeting .NET 10.

## Projects

- `src/Lajur.Api` — HTTP boundary, middleware, endpoint composition, and authentication entry points.
- `src/Lajur.Application` — use cases and application orchestration.
- `src/Lajur.Domain` — finance-domain types and rules without infrastructure dependencies.
- `src/Lajur.Infrastructure` — workbook adapter, auth persistence, filesystem, backup, and other external concerns.
- `tests/Lajur.UnitTests` — domain and application unit tests.
- `tests/Lajur.IntegrationTests` — hosted API and infrastructure integration tests.
- `tests/Lajur.WorkbookTests` — XLSM mutation and preservation tests.

Financial transactions must not be stored canonically in SQL. The per-user annual XLSM workbook remains the financial source of truth.

## Commands

Run from this directory:

```powershell
dotnet restore Lajur.slnx
dotnet build Lajur.slnx --no-restore
dotnet test Lajur.slnx --no-build
dotnet run --project src/Lajur.Api
```

Initial health endpoints:

- `GET /health/live`
- `GET /health/ready`

Readiness includes a connection check against the auth SQLite database.

## Account and authentication

Phase 2 authentication is backed by SQLite under the configured storage root
(`.local/lajur/auth/lajur-auth.db` by default). The JWT signing key is generated
once under `.local/lajur/keys/` and reused across restarts. Both locations are
ignored by Git and mapped to persistent volumes by Docker Compose.

Create the first account from an interactive terminal:

```powershell
dotnet run --project src/Lajur.Api -- account create --username rama
```

Reset a password (this revokes every existing session for that account):

```powershell
dotnet run --project src/Lajur.Api -- account reset --username rama
```

The command prompts for the password without echoing it. Usernames are 3–64
letters, digits, `.`, `_`, or `-`; passwords must contain 12–256 characters.
For the containerized stack, use the equivalent commands:

```powershell
docker compose run --rm api account create --username rama
docker compose run --rm api account reset --username rama
```

Implemented endpoints under `/api/v1`:

- `POST /auth/login` — username/password plus an optional prior `deviceId` and device label;
- `POST /auth/refresh` — opaque rotating refresh token;
- `POST /auth/logout` — revoke the authenticated session family;
- `GET /me` — current account/device/session plus workbook onboarding state and available years;
- `DELETE /devices/{deviceId}` — revoke a device owned by the authenticated account.

Defaults are a 15-minute access token, 30-day refresh idle timeout, and 180-day
absolute refresh lifetime. Successful login revokes other sessions under the
v1 one-active-device policy. Refresh tokens are stored only as SHA-256 hashes;
passwords use ASP.NET Core's password hasher. Login and refresh endpoints are
rate limited.

## Finance API

Authenticated Phase 3 endpoints are available under `/api/v1`:

- `GET /master?year=2026`;
- `GET /summary?year=2026&month=9`;
- `GET /transactions?year=2026&month=9&query=coffee`;
- `POST /transactions`;
- `PUT /transactions/{id}?year=2026`;
- `DELETE /transactions/{id}?year=2026&expectedRevision=3`.

The API resolves storage exclusively from the authenticated immutable user ID.
Create is idempotent by client-generated transaction UUID; update and delete
require the expected revision. Summary values are calculated from canonical A:F
rows rather than Excel formula caches. Stable ProblemDetails codes distinguish
missing workbooks/transactions, ID conflicts, revision conflicts, request
validation, and workbook validation failures.

Legacy rows become visible to transaction listing after Phase 4 import migration
assigns their application identities. Summary calculation always includes every
valid visible transaction row.

## Workbook onboarding, import, and export

Authenticated Phase 4 endpoints are available under `/api/v1`:

- `GET /workbooks`;
- `POST /workbooks/{year}/create`;
- `POST /workbooks/import` (multipart);
- `GET /workbooks/{year}/export`;
- `GET /workbooks/export-all`.

Install the approved clean template at
`.local/lajur/templates/finance-template.xlsm` when running directly. The Docker
Compose templates volume maps the same logical location to
`/srv/lajur/templates/finance-template.xlsm` in the container.

Imports require `confirmNoPendingChanges=true`. Replacing an existing year also
requires `replaceExisting=true`; a protected byte-for-byte backup is written before
promotion. Uploads are staged, size/package checked, structurally validated,
migrated, validated again, and promoted atomically. A failure leaves the active
workbook unchanged.

## Workbook module status

The workbook adapter is implemented under `src/Lajur.Infrastructure/Workbooks`.
It uses `DocumentFormat.OpenXml` without Excel COM/Interop and provides:

- read-only monthly A:F transaction and Master-data parsing;
- create, revision-checked update, and revision-checked delete operations;
- very-hidden `__AppIndex` identity/tombstone metadata;
- very-hidden append-only `__AuditLog` history;
- per-user/year in-process serialization;
- same-filesystem temporary-copy validation and atomic promotion.
- known-node year generation from an approved clean template;
- staged legacy import and metadata reconciliation;
- protected pre-import backups and full-package export.

Workbook tests use the synthetic XLSM fixture copied into the test output. Ordinary
mutations preserve VBA bytes and unrelated package parts. Lifecycle tests cover
clean year generation, legacy indexing, invalid-import rollback, and protected
replacement backups.
