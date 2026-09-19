# Settings matrix

Settings are centralized. Do not scatter magic behavior constants across UI/workers.

Three categories:

- **User-facing** — normal Settings UI.
- **Advanced** — optionally exposed later or behind an advanced section.
- **Server/internal** — configuration, not ordinary UI.

## 1. User-facing defaults

### Security

| Setting | Default |
|---|---|
| Biometric unlock | ON after user opts in |
| Auto lock | 5 minutes |
| Device credential fallback | ON |
| Hide content in Recents | ON |
| Allow screenshots | OFF |

### Sync

| Setting | Default |
|---|---|
| Auto sync | ON |
| Wi-Fi only | OFF |
| Retry when connectivity returns | ON |
| Manual “Sync now” | available |

### Transaction

| Setting | Default |
|---|---|
| Default type | Pengeluaran |
| Default date | Today |
| Remember last payment method | ON |
| Confirm before delete | ON |

### Smart Scan

| Setting | Default |
|---|---|
| Smart Scan | ON if model available |
| Keep original receipt image | OFF |

### Appearance

| Setting | Default |
|---|---|
| Theme | System |

### Data

Actions:

- Import Workbook
- Export Workbook
- Export All
- Sync Now

## 2. Advanced candidates

Do not expose unless useful:

- LLM idle unload timeout;
- selected OCR engine;
- selected local model;
- scan diagnostics;
- sync retry diagnostics.

If exposed, label these as advanced and avoid requiring ordinary users to understand them.

## 3. Server configuration

Examples:

```text
AccessTokenMinutes = 15
RefreshIdleDays = 30
RefreshAbsoluteDays = 180
MaxWorkbookUploadBytes
WorkbookLockTimeout
BackupRetentionCount = 30
PreMigrationBackupDays = 90
MinimumSupportedAppVersion
CurrentWorkbookSchemaVersion
```

Do not trust Android client values for server security policy.

## 4. Internal constants

Examples:

- HTTP timeout;
- max OCR input dimensions;
- model context/token caps;
- logging redaction;
- table/sheet mapping.

Internal constants that define workbook compatibility should be grouped in one workbook-schema component rather than repeated.

## 5. Persistence

- preferences: DataStore;
- sensitive credentials: Keystore-backed encrypted storage;
- Room: transactional app data/outbox, not preference flags.
