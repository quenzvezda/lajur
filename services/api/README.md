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
- `GET /me` — current account/device/session;
- `DELETE /devices/{deviceId}` — revoke a device owned by the authenticated account.

Defaults are a 15-minute access token, 30-day refresh idle timeout, and 180-day
absolute refresh lifetime. Successful login revokes other sessions under the
v1 one-active-device policy. Refresh tokens are stored only as SHA-256 hashes;
passwords use ASP.NET Core's password hasher. Login and refresh endpoints are
rate limited.

## Workbook module status

The Phase 1 workbook spike is implemented under `src/Lajur.Infrastructure/Workbooks`.
It uses `DocumentFormat.OpenXml` without Excel COM/Interop and provides:

- read-only monthly A:F transaction and Master-data parsing;
- create, revision-checked update, and revision-checked delete operations;
- very-hidden `__AppIndex` identity/tombstone metadata;
- very-hidden append-only `__AuditLog` history;
- per-user/year in-process serialization;
- same-filesystem temporary-copy validation and atomic promotion.

Workbook tests use the synthetic XLSM fixture copied into the test output. Ordinary
mutations preserve VBA bytes and unrelated package parts. Existing legacy rows are
not assigned application transaction IDs by the spike; that migration belongs to
the import/onboarding phase.
