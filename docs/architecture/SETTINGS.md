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
| Protect screen and Recent Apps | ON (currently affected by AND-001) |

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
| Smart Scan | Local OCR available without a model |
| Local AI suggestions | Explicit optional action on each scan draft |
| Keep original receipt image | OFF |

The AI action is not a new persisted setting. It starts model loading only when
requested; installing a model does not make scans run AI automatically.

### Appearance

| Setting | Default |
|---|---|
| Theme | System |

### Application updates

| Setting/action | Default |
|---|---|
| Check automatically on foreground | ON, at most every 24 hours, release builds only |
| Check now | Available in Settings and before login |
| Download/install update | Explicit actions; Android confirms installation |

Automatic checks use public GitHub Releases independently of the configured
backend. Disabling checks is persisted in DataStore. A failed automatic check
does not interrupt entry. Debug builds support manual discovery but cannot
install an official release over their different signing key.

### Data

Actions:

- Import Workbook
- Export Workbook
- Export All
- Sync Now

Implemented at **Pengaturan → Data / Workbook**, with an available-years list,
file size/last-modified timestamp and a count of all outstanding outbox operations.
The menu also creates absent years from the server template. Imports are explicit
year-confirmed replacements (not merges), blocked by any pending/failed outbox,
and require a separate confirmation. Choosing a file does not upload it.
Exports use the system Save As picker and include only canonical server data;
unsynchronized local changes trigger a warning. Import's Android limit is 20 MiB.

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
