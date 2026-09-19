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
