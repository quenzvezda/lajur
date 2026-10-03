# Lajur

> Working product name. A minimal, offline-first Android app for fast personal-finance entry while keeping macro-enabled Excel workbooks as the financial source of truth.

**Tagline:** Catat cepat, Excel tetap milikmu.

This repository specification is the handoff package for an agentic coding workflow. It intentionally separates durable product/architecture decisions from implementation plans so coding agents can load only the context they need.

## Product intent

Lajur exists to remove the friction of opening Excel for every personal-finance transaction. The Android app is optimized for:

- fast manual transaction entry;
- offline entry with deferred synchronization;
- receipt/payment-screenshot scanning;
- fully local OCR and local LLM extraction;
- mandatory human review before a scan becomes a transaction;
- lightweight current-month browsing, edit, and delete;
- import/export of real `.xlsm` files so the user is never locked into the app.

The server is self-hosted on the owner's Linux VPS. Financial transaction data is stored in the user's Excel workbook, not in a server SQL transaction database.

## Working name

The working product name is **Lajur**. The name is intentionally independent from the Microsoft Excel trademark and can be changed before public release. No trademark clearance is asserted by this specification.

Suggested code names:

- Android app/module: `lajur-android`
- Backend service: `lajur-api`
- Docker service: `lajur-api`
- Temporary Android application ID: `com.lajur.app` — replace before a public store release if desired.

## Start here

Coding agents should read, in order:

1. [`AGENTS.md`](AGENTS.md)
2. [`ARCHITECTURE.md`](ARCHITECTURE.md)
3. [`docs/index.md`](docs/index.md)
4. The document for the subsystem being changed.
5. The current execution plan in [`docs/exec-plans/active/`](docs/exec-plans/active/).

Humans can start with:

- [`docs/product/PRODUCT_SPEC.md`](docs/product/PRODUCT_SPEC.md)
- [`docs/product/UX_FLOWS.md`](docs/product/UX_FLOWS.md)
- [`docs/architecture/EXCEL_CONTRACT.md`](docs/architecture/EXCEL_CONTRACT.md)
- [`docs/exec-plans/active/0001-mvp.md`](docs/exec-plans/active/0001-mvp.md)
- [`docs/development/LOCAL_SETUP.md`](docs/development/LOCAL_SETUP.md)

## Local development

Open `services/api/Lajur.slnx` in Visual Studio for backend work and open
`apps/android/` in Android Studio for Android work. Both applications remain in
this repository but have independent build entry points.

From the repository root, restore, build, and test the backend with:

```powershell
dotnet restore services/api/Lajur.slnx
dotnet build services/api/Lajur.slnx --no-restore
dotnet test services/api/Lajur.slnx --no-build
```

Run the API directly with:

```powershell
dotnet run --project services/api/src/Lajur.Api
```

Build and test Android with the committed Gradle Wrapper:

```powershell
.\apps\android\gradlew.bat -p apps/android testDebugUnitTest assembleDebug
```

Run the local container stack with:

```powershell
docker compose up --build --detach
Invoke-WebRequest http://127.0.0.1:8080/health/ready
docker compose down
```

See [`docs/development/LOCAL_SETUP.md`](docs/development/LOCAL_SETUP.md) for SDK,
Android emulator/device, Docker, and local-only file guidance.

## Data / Workbook

Payment method is optional; date, type, category and positive integer IDR remain
required. Home offers a non-blocking **Perlu dilengkapi** reminder/filter for the
cached month. Filling payment later updates the original transaction, without a
new settlement status or duplicate expense. See
[the UX smoke checklist](docs/development/SMART_SCAN_UX_SMOKE.md#optional-payment-acceptance).

The Android **Pengaturan → Data / Workbook** menu now supports annual workbook
creation, confirmed XLSM import, annual export and all-years ZIP export. Import
blocks outstanding local operations and never merges or silently replaces a year.
See [the workbook menu smoke guide](docs/development/WORKBOOK_MENU_SMOKE.md) before
testing replacement on a daily-use account.

## Core technology direction

| Area | Direction |
|---|---|
| Android | Native Kotlin + Jetpack Compose |
| Offline cache/outbox | Room / SQLite |
| Preferences | Jetpack DataStore |
| Background sync | WorkManager |
| App unlock | Android BiometricPrompt |
| Secrets on Android | Android Keystore-backed encryption |
| Backend | ASP.NET Core on .NET 10 LTS |
| Auth/infrastructure DB | SQLite |
| Financial source of truth | Per-user, per-year `.xlsm` workbook |
| Workbook manipulation | Open XML package manipulation; preserve VBA package parts |
| OCR | On-device only |
| LLM | On-device only; no cloud inference |
| Deployment | Docker on Ubuntu VPS behind existing HTTPS reverse proxy |
| CI/CD | GitHub Actions |
| UI E2E tests | Not a required CI gate for v1 |

## Explicit non-goals for v1

- replacing the Excel dashboard with a full finance dashboard;
- collaborative/shared workbooks;
- simultaneous multi-device editing for one account;
- bank/e-wallet API integrations;
- cloud OCR or cloud LLM APIs;
- server-side financial transaction database;
- complex accounting/journaling features;
- automatic transaction save directly from OCR/LLM without review;
- live editing of the active server workbook from desktop Excel while the API is writing to it.

## Source workbook

The original development workbook supplied for this specification is:

`Keuangan_Pribadi_2026.xlsm`

Its observed structure is documented in [`docs/reference/WORKBOOK_OBSERVED_STRUCTURE.md`](docs/reference/WORKBOOK_OBSERVED_STRUCTURE.md).

A critical observation is that monthly transaction tables occupy **A:F only**, while the sheets also use columns to the right for pivots, projections, and helper formulas. Therefore v1 does **not** widen the monthly transaction tables with hidden system columns. Transaction identity/revision metadata is stored on hidden system sheets instead.
