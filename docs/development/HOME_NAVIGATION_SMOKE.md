# Home/navigation smoke test

Use dummy data and the existing daily-use installation. Do not clear app data,
uninstall, or run AGP `connected*AndroidTest` tasks on that installation.

## Manual checks

1. Open Home and select the test workbook month. Confirm income, expense,
   transaction count and **Selisih bulan ini** against the backend rows. The
   difference is not an opening/closing balance. Payment may remain blank.
2. Tap **+ Tambah**, then system Back/gesture without saving. Return to Home,
   not the launcher. Repeat using toolbar **Kembali**. Unsaved form input is
   discarded; no transaction should be created.
3. On Home press Back once: toast **Tekan sekali lagi untuk keluar**, app stays.
   Wait more than two seconds and press again: another toast, not exit. Press
   twice within two seconds: app closes without logout. Reopen (unlock if needed).
   Moving to another tab or backgrounding between presses resets the exit window.
4. Use the fixed bottom bar: Home / History / center Scan / Insight / Settings.
   System Back from any other primary tab returns Home, not the launcher.
5. Income rows are green, expenses red; both also show type labels. Check light
   and dark themes. Tap a row; Back returns the originating Home/History. Edit it;
   Back without saving returns detail. Delete confirmation Back dismisses the
   confirmation first. Do not delete real data for this check.
6. History provides month navigation and search. Enter a search that matches no
   row, then return Home: totals and preview remain unfiltered. **Lihat semua**
   clears the search/review filter. The completeness reminder routes to History;
   optional blank payment still contributes to totals, with no unpaid status.
7. Center Scan shows image/camera actions, not a Home card. Select the existing
   dummy receipt, review OCR, and cancel with Back: return to Scan. Incoming
   shared images retain the unlock gate and a pending badge on the Scan tab.
   Optional AI suggestions and reviewed save remain unchanged.
8. Insight clearly says coming soon/TODO; it has no fake chart or save action.
   Settings still offers Data/Workbook, app lock, privacy and **Keluar akun**.
   Check bottom navigation does not cover content under gesture/three-button nav.
9. Open the category sheet/calendar/type/payment menu in the editor. Back closes
   the overlay before leaving the form. On Data, existing busy transfer guards
   must still prevent accidental navigation during a transfer.

## Isolated automated checks

`AppNavigationTest` covers route targets, exit timing and reset. Opt-in
`AppNavigationUiTest` uses actual Back dispatcher/editor and dummy UI callbacks;
`TransactionEntryUiTest` retains the payment/review/search regressions. These do
not log in, save transactions, or modify account workbooks. Device testing needs
an awake/unlocked phone. Preserve the daily-use app with update installs:

```powershell
cd F:\lajur-agentic-spec\apps\android
.\gradlew.bat testDebugUnitTest assembleDebug assembleDebugAndroidTest lintDebug
$adbPath = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adbPath devices -l
& $adbPath -s '<exact-device-serial>' install -r app\build\outputs\apk\debug\app-debug.apk
& $adbPath -s '<exact-device-serial>' install -r -t app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk
& $adbPath -s '<exact-device-serial>' shell am instrument -w -r -e class com.lajur.app.ui.AppNavigationUiTest,com.lajur.app.ui.TransactionEntryUiTest -e lajur.runUiSmokeTests true com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

Reference: [Android Compose Back handling](https://developer.android.com/guide/navigation/custom-back),
[Material navigation bar](https://developer.android.com/develop/ui/compose/components/navigation-bar).

## Latest verification — 2026-10-05

Build, all JVM tests and lint pass (no lint errors; existing dependency/KTX
warnings remain). Five route/exit JVM cases and 16 opt-in POCO UI cases pass.
The UI run covers system/toolbar Back, modal-category Back priority, two-press
exit, bottom-menu entry, placeholder, unfiltered Home, payment and scan review
regressions. Dummy light/dark screenshots were visually checked. Updated main
and test APKs were installed with `-r`; app data was not cleared. Owner manual
gesture/lifecycle and real-picker → backend → Excel checks remain separate gates.
