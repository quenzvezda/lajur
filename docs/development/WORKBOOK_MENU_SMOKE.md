# Android Data/Workbook smoke test

Entry: bottom bar Setelan → Data / Workbook. A new account also exposes
**Impor workbook yang sudah ada** during onboarding.

## Safe first pass on the daily-use account

1. Connect to the backend and open Data / Workbook. Refresh the list. Check
   available years, file size, last-modified local time and unsynchronized count.
2. Export one year as XLSM. Select a destination in the Android system Save As
   picker. Cancellation must not upload/import or change the server workbook.
3. Export all years as ZIP. Open the resulting ZIP and verify the expected annual
   XLSM files. This exports financial workbooks, not auth DB/server configuration.
4. Copy the annual export to the PC. Open the copy in desktop Excel. Check the
   monthly A:F tables, right-side analytics, PivotTables, VBA and Data Baru button.
   Recalculate/refresh; server writes do not calculate Excel formula caches.
5. With a dummy local pending transaction, reopen Data. Import must be disabled.
   Export must warn that pending changes are excluded. Do not discard real
   pending work just to unblock import. Sync or resolve it first.
6. Disconnect the backend. Refresh/export should show an error; existing manual
   entry remains available. Reconnect and refresh again.

Do not replace a daily-use year during this first pass. The updated owner's
OneDrive workbook has **not** been audited/promoted by this implementation.

## Replacement/import pass — disposable account or explicitly approved copy

1. Use a disposable account with no pending/failed outbox operations. Keep a
   separate export of its original year before any replacement.
2. Choose a compatible synthetic `.xlsm` from the system picker. Selecting the
   file only creates a private temporary copy; it does not upload yet. Android
   accepts at most 20 MiB, checks extension and actual streamed size, and does
   not parse/edit the workbook. The backend performs package/domain validation.
3. Enter its year. For an existing year, leave replacement unchecked: the action
   must remain disabled. Check replacement, open the confirmation, then cancel.
   No server mutation must occur.
4. Confirm the import. Verify year/count summary, server pre-replacement backup,
   current-year cache/Master reload and the resulting exported workbook.
5. Try wrong-year, incomplete-row, and invalid-package copies. Server validation
   must reject them and preserve the active file. Use synthetic content only.
6. On a connection failure during upload, do **not** assume it rolled back or
   automatically repeat import. Refresh/check canonical data and export first.
   The import client disables automatic connection retries. An expired-access
   401 may still refresh authentication and retry through the existing session
   mechanism, before a successful authenticated import.

## Cache behavior

Import holds the same remote-operation gate as sync/pull and excludes local
mutations while replacing. The real Room outbox is checked under that gate,
including failed operations and pending deletes, regardless of a stale UI count.
Normal background sync does not hold the local-entry gate.

Before upload, only the selected year's **SYNCED** rows and cached Master are
invalidated. This avoids editable stale revisions if the process dies or the
response is lost after the server commits. Failed imports may therefore also
invalidate that year's Android cache; the current month is reloaded when online,
and other months reload on navigation. Server canonical data and local pending
operations are never cleared by cache invalidation. No workbook generation
identifier is exposed in the current transport. Confirmed success selects the
imported year in Home and refreshes `/me`/Master/transactions.

Transfers use system document URIs with narrow grants; no broad storage permission.
Private staging files are deleted after transfer/discard. Picker state can be
lost after process death: choose the document again, never auto-import on restart.
Export failure is not success; a partial new output is deleted where supported
by the document provider. Check/remove a leftover partial file if necessary.

## Automated isolated checks

JVM: stream integrity/size/year validation, safety gate concurrency and HTTP
binary/multipart/auth/error contracts. Device: three synthetic Compose cases for
pending guard, replacement opt-in/confirmation and export warning; an in-memory
Room case checks year-scoped invalidation and failed/delete outbox counting.
Neither uses the actual account or imports a workbook.

Build/update install, never run AGP connected tests on the daily-use app:

```powershell
cd F:\lajur-agentic-spec\apps\android
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest :app:lintDebug
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s '<serial>' install -r app\build\outputs\apk\debug\app-debug.apk
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s '<serial>' install -r -t app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s '<serial>' shell am instrument -w -r -e class com.lajur.app.ui.WorkbookUiTest,com.lajur.app.data.local.WorkbookCacheTest -e lajur.runUiSmokeTests true com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

Keep the device awake/unlocked. UI tests are opt-in, not required CI acceptance.
These tests do not prove the system-picker → real-server transfer → desktop Excel
chain; that manual pass remains necessary.

Reference: [Android Storage Access Framework](https://developer.android.com/training/data-storage/shared/documents-files).
