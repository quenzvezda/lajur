# Smart Scan and transaction editor UX smoke test

Updated 2026-10-03. Use dummy receipts and dummy transactions only. OCR and AI
are local; the API is needed for initial Master data and subsequent sync, not
for reading an image. This checklist does not accept a production LLM model.

## Owner checklist

1. Open **+** for manual entry. Enter `68820`: it should display **Rp 68.820**.
   Date starts at today; tap it to choose another date from the calendar.
2. Open **Pembayaran**: its short menu should match the field width. Pick Bank.
3. Open **Kategori**: a bottom sheet should show **Cari kategori**, scrollable
   choices and a close action. Try part of a category name, mixed case, and a
   query with no match. Clear search and select a result.
4. Change type: the old category should clear; the new sheet must show only the
   selected type's Master categories. Save stays disabled while required values
   are missing or invalid. No negative, decimal or overflowing IDR value can save.
5. Scan the same synthetic minimarket receipt. The draft should appear after
   OCR, without waiting for the model. Expect date **2026-09-20**, amount
   **Rp68.820**, payment **Bank** (QRIS). A missing category is expected, not an
   error. All fields still require human review.
6. **Lihat teks struk & detail** expands OCR and timings. The ordinary view
   should emphasize the draft, source labels and expandable evidence.
7. Optionally tap **Tambahkan saran AI**. You may cancel or tap **Periksa &
   lengkapi transaksi** while AI runs. A late result must never change the editor.
   Missing/failed models must leave the evidence draft usable.
8. A missing scan date/type must remain empty in the editor, not silently become
   today's date/default expense type. Complete it explicitly.
9. For extraction evidence, expand details and tap **Simpan laporan uji (debug)**.
   Wait for confirmation, then ask the agent to read it via ADB. An OCR-only
   report has `rules_only`; an explicit AI report includes model timings/output.
   Each save overwrites the latest report, so retrieve it before saving another.
10. Optional full end-to-end: save one fully reviewed dummy transaction offline,
    restart/unlock, reconnect and confirm one Synced row. Export the XLSM and
    verify one matching A:F row in Excel. Do not confuse a scan report or the
    isolated UI tests below with proof of save/sync/workbook behavior.

For report capture/privacy and offline Wireless ADB caveats, see
[Phase 8 diagnostics](PHASE8_LOCAL_MODEL.md#evidence-first-receipt-acceptance-and-diagnostics).

### Optional payment acceptance

Use a dummy account/workbook, not the owner's historical ledger:

1. Offline, create a valid date/type/category/positive-IDR transaction without
   choosing payment. Save succeeds; Home displays **Pembayaran belum diisi** and
   a compact **Perlu dilengkapi** toolbar icon/count. Missing date/type/category/amount still prevents save.
2. Restart/unlock and reconnect. The same transaction syncs once; it is not
   marked Failed merely because payment is empty.
3. Search for another transaction. The reminder's count must remain unchanged.
   Tap the review icon: search/other filters clear and only incomplete rows appear.
4. Edit that row and choose a Master payment. After sync, the reminder disappears
   if no other incomplete row remains. Transaction count and amount totals stay
   unchanged. **Kosongkan pembayaran** clears it again without a new transaction.
5. Export and inspect the same A:F row in Excel: C is blank or the selected
   method; there is no repayment/status row or column. Do not modify financial
   formulas to manufacture a settlement ledger.

Isolated UI checks below cover blank save, clearing, reminder and filter with
dummy callbacks. Backend fixture tests cover import/reimport, identity/revision,
summary, retry and package preservation. They do not replace this full
offline-to-server-to-desktop-Excel acceptance check.

## Isolated opt-in UI tests

These tests use an empty Compose host, synthetic Master data and an in-memory
save callback. They do not log in, run a model, create an outbox or save a real
transaction. They are not a mandatory UI E2E CI gate.

From `apps/android`, with one connected unlocked device. Keep the screen awake
through the run; Doze/keyguard can prevent the empty Compose host from appearing,
which is a test-environment failure rather than a selector regression:

```powershell
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest :app:lintDebug
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices -l
```

Use the exact serial from that list in place of `<device-serial>` on each command:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s <device-serial> install -r app/build/outputs/apk/debug/app-debug.apk
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s <device-serial> install -r -t app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s <device-serial> shell am instrument -w -r -e class com.lajur.app.ui.TransactionEntryUiTest -e lajur.runUiSmokeTests true com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

Update-install with `-r`; do not uninstall, clear app data, or run Gradle's
connected-test workflow on the daily-use install. The latter previously removed
the app before a Xiaomi-restricted reinstall, losing Android-local state.
Xiaomi may require installation consent on the unlocked phone.

Tests capture synthetic layout PNGs in app-specific external cache. Retrieve
them only into ignored private storage:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s <device-serial> pull /sdcard/Android/data/com.lajur.app/cache/ui-smoke ../../private/ui-smoke
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s <device-serial> reverse tcp:8080 tcp:8080
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s <device-serial> shell am start -n com.lajur.app/.MainActivity
```

The localhost API URL needs the reverse route and a running API. No model,
receipt, screenshot, debug report or private workbook may be committed.
