# Product specification

## 1. Product

**Name:** Lajur  
**Status:** working name / pre-development  
**Primary platform:** Android  
**Backend:** self-hosted VPS  
**Primary user:** an individual who already maintains a structured annual `.xlsm` personal-finance workbook.

## 2. Problem

Entering each expense/income directly into Excel is too slow on a phone. The user wants a fast Android front-end without abandoning the existing workbook, reports, formulas, and macros.

## 3. Product principles

1. **Excel remains owned by the user.**
   The app must have a clean exit path: export the real workbook and stop using Lajur at any time.

2. **Offline manual entry is first-class.**
   Loss of connectivity must not block capturing a transaction.

3. **Scanning assists; it does not decide.**
   OCR/LLM creates an editable draft. The user is the final reviewer.

4. **Privacy by default.**
   Receipt/screenshot OCR and semantic extraction run locally on the Android device.

5. **Minimal UI, not feature poverty.**
   The interface should optimize transaction capture and correction, without becoming a full accounting application.

6. **Self-hosted operation.**
   The API, workbook storage, auth DB, and backups live on infrastructure controlled by the operator.

## 4. v1 personas/account model

The backend may host multiple accounts, but accounts are isolated.

For v1:

- one account → one user identity;
- one account → one active Android device;
- one account → one set of annual workbooks;
- users never share a workbook;
- user A cannot query, export, import, or mutate user B's workbook.

A later multi-device mode must be introduced deliberately with an ADR.

## 5. Core v1 capabilities

### Authentication

- login with username + password;
- after successful login, day-to-day foreground unlock uses fingerprint/biometric or allowed device credential;
- configurable auto-lock timeout, default 5 minutes;
- normal app switching within the grace period does not re-prompt;
- background sync does not require biometric interaction;
- new-device login invalidates old active sessions in v1.

### Onboarding

After login, if no workbook exists for the account:

- **Create New Workbook**, generated from the server template; or
- **Import Existing Workbook**, used for migration from the user's current data.

### Manual transaction entry

Required fields:

- date;
- type;
- payment method;
- category;
- amount.

Optional:

- description.

Defaults:

- date = today;
- type = `Pengeluaran`;
- payment method may remember the last chosen value;
- category is not auto-filled unless explicitly configured later.

Manual save must work offline.

### Smart Scan

Input sources:

- camera;
- image picker/gallery;
- Android share intent from a screenshot.

Pipeline:

`image → local OCR → local LLM → structured draft → deterministic validation → user review → save`

No cloud inference.

The engine may propose one primary value plus alternatives for ambiguous fields. Missing/uncertain required fields remain editable and cannot be silently fabricated.

### Browsing

v1 is intentionally lightweight:

- current month by default;
- previous/next month or month picker;
- transaction list;
- simple text search;
- sync status;
- transaction detail.

No requirement for advanced charts or replacing the workbook's `Rekap` sheet.

### Edit/delete

Synchronized transactions can be edited/deleted from Android.

- update uses optimistic revision checking;
- delete removes the transaction row from the monthly table;
- every create/update/delete is recorded in workbook audit metadata;
- delete requires confirmation;
- a UI snackbar-style Undo may be supported when it can be implemented safely without a recycle-bin subsystem.

### Import/export

User can:

- import a compatible `.xlsm`;
- export a specific annual `.xlsm`;
- export all annual workbooks as a zip.

Exported workbooks remain independently usable in desktop Excel.

## 6. Financial data boundaries

Financial transaction data must not be stored canonically in server SQL.

Allowed non-canonical copies:

- Android Room cache/outbox;
- hidden metadata/audit sheets inside the workbook;
- temporary files during an atomic write;
- timestamped workbook backups.

Allowed server SQLite data:

- users;
- password hashes;
- device/session information;
- refresh-token hashes;
- infrastructure metadata that is not the finance ledger.

## 7. Manual Excel editing

v1 supports manual editing through:

`export → edit in Excel → import`

Direct concurrent desktop editing of the active workbook file on the VPS while the API is writing is out of scope.

On re-import, the backend reconciles app metadata when possible and never replaces the current active workbook unless validation succeeds.

## 8. Year lifecycle

Each year is a separate workbook:

- `Keuangan_Pribadi_2026.xlsm`
- `Keuangan_Pribadi_2027.xlsm`
- etc.

A workbook for a new year is generated from the approved template/migration mechanism. Year-dependent formulas/text must be updated safely.

## 9. Success criteria

Lajur is successful when the user can:

- record a manual expense in seconds;
- record transactions with no internet and later see them synchronized exactly once;
- share a payment screenshot into Lajur, review an automatically filled draft, and save it;
- edit/delete a mistake without opening desktop Excel;
- continue to use the exported workbook normally without Lajur;
- upgrade/deploy the API without risking workbook loss.

## 10. Non-goals

See `README.md`. In particular:

- no bank API aggregation;
- no shared/collaborative workbook;
- no auto-save from AI;
- no cloud AI dependency;
- no full double-entry accounting system;
- no attempt to run desktop Microsoft Excel on the Linux VPS.
