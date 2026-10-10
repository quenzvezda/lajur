# Lajur architecture

## System context

```text
┌──────────────────────── ANDROID ───────────────────────┐
│ Kotlin + Jetpack Compose                               │
│                                                        │
│ Manual entry       Image / Share Intent                │
│      │                    │                            │
│      │             Local OCR                           │
│      │                    │                            │
│      │        Evidence rules + Local LLM                │
│      │                    │                            │
│      └─────────────► Draft / Review ◄──────────────────┘
│                             │                          │
│                           Room                         │
│                    cache + durable outbox              │
│                             │                          │
│                         WorkManager                    │
└─────────────────────────────┬──────────────────────────┘
                              │ HTTPS / JSON
                              ▼
┌────────────────────────── VPS ─────────────────────────┐
│ Existing reverse proxy + TLS                           │
│                │                                       │
│          ASP.NET Core API                              │
│          ├─ Auth/session                               │
│          ├─ Master data                                │
│          ├─ Transaction API                            │
│          ├─ Workbook service                           │
│          ├─ Import/export                              │
│          └─ Backup/audit                               │
│              │                  │                      │
│              │                  └── auth SQLite        │
│              ▼                                         │
│   per-user/year macro-enabled XLSM                     │
│   financial source of truth                           │
└────────────────────────────────────────────────────────┘
```

## Architectural boundaries

### Android

Owns:

- UI/navigation;
- biometric app lock;
- local transaction cache;
- durable offline outbox;
- share-intent ingestion;
- camera/gallery image acquisition;
- OCR;
- local LLM inference;
- evidence-based scan fields with OCR reading order and source tracking;
- draft validation before submission;
- retry scheduling.

Does **not** own:

- canonical finance storage;
- workbook mutation;
- server account verification;
- cross-user data isolation.

### API

Owns:

- username/password authentication;
- access/refresh sessions;
- one-active-device policy for v1;
- authorization and tenant isolation;
- canonical validation;
- workbook serialization;
- idempotency;
- revision checks;
- audit trail;
- atomic workbook replacement;
- browser workbook UI and a separate cookie session with antiforgery-protected
  monthly transaction commands (ADR 0009);
- structured budget and monthly projection input commands (ADR 0010);
- import/export;
- backup lifecycle.

Does **not** own:

- OCR/LLM inference;
- receipt image storage by default;
- a SQL copy of the transaction ledger.

### Excel workbook

Owns:

- canonical transaction rows;
- current master categories/types/payment methods;
- dashboard/reports/formulas/macros;
- hidden application metadata and audit trail.

## Data ownership rule

When components disagree:

1. For financial content already synchronized, the active workbook wins.
2. Pending unsynchronized Android operations are local commands that have not yet become canonical.
3. Server auth SQLite wins for account/session state.
4. Android DataStore wins for user-device preferences.
5. The API must not manufacture a SQL “canonical ledger” to simplify reads.

## Consistency model

This is not a collaborative editor.

- v1: one active Android device per account.
- all workbook writes for a user/year are serialized by the API;
- Android and the browser use optimistic revisions for update/delete;
- create is idempotent by client-generated transaction ID;
- direct concurrent editing of the active server file is unsupported;
- Excel manual editing is supported through export → edit → import, with reconciliation where metadata is available.

## Why metadata uses hidden sheets

The supplied workbook's monthly transaction table is `A:F`, but monthly sheets also contain active pivots/helper/projection content to the right. Adding hidden table columns adjacent to `F` could shift or overwrite those structures.

Therefore:

- keep monthly tables `A:F`;
- use hidden `__AppIndex` for transaction IDs, revisions, snapshots, hashes, and tombstones;
- use hidden `__AuditLog` for financial change history.

See `docs/architecture/EXCEL_CONTRACT.md`.

## Technology baseline

- Android: Kotlin, Jetpack Compose, Room, WorkManager, DataStore, BiometricPrompt, CameraX/Photo Picker as appropriate.
- Networking: a conventional HTTP client with explicit DTOs and timeouts; Retrofit/OkHttp is an acceptable default.
- Backend: .NET 10 LTS, ASP.NET Core, SQLite for auth infrastructure.
- XLSM: `DocumentFormat.OpenXml`/Open Packaging manipulation. No Microsoft Excel COM/Interop; server must run on Linux.
- Deployment: Docker, existing Nginx/Certbot or equivalent reverse proxy.
