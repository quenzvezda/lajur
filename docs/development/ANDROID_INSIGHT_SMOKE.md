# Native Android Insight acceptance

Use a disposable account and a synthetic workbook against a local API. Do not use
the owner account, sole original file, or production imports for testing.

1. Build backend and Android debug; point login to the local API. On Windows use
   `adb -s <emulator-serial> reverse tcp:<port> tcp:<port>`, then `install -r` the APK.
   The new `/api/v1/reports?year=` endpoint requires the updated local backend.
2. Home has an inline Add button below its summary. History has a small plus-icon
   FAB. Both open the same manual editor; editor Back returns to the origin.
3. Insight shows Ringkasan/Kategori/Tren, fixed navigation, month arrows and a
   tappable month/year picker. Pick another month, year with no workbook, and
   return; missing data is not fabricated. List position resets per tab/month.
4. Ringkasan totals agree with web for the same canonical workbook. Select
   Harian/Kumulatif, tap the chart, check day/cumulative amounts, close the dialog.
   Balance cells and pace show only recognized values. Saving rate without income
   remains unavailable, not infinity. Check an empty month and large amounts.
5. Kategori bars compare amounts; budget usage is separately labelled. Over-budget
   values use both text and color. Payments are monthly, excluding income. Tap a
   category: History shows only that category with a removable filter. A top-expense
   UUID drills down to the exact row even when descriptions repeat.
6. Tren shows annual/monthly income/expense/net and the selected month's projection.
   Unknown source inputs stay unavailable; projections do not create ledger rows.
7. Check light/dark and narrow screens, long category descriptions, and text scaling.
8. Load reports online, disconnect backend access, force-stop/reopen app. Insight
   still shows the prior account/year snapshot with offline state and fetch time.
   An uncached year shows a recoverable empty state. Restore connectivity/refresh.
9. Create a pending/failed local transaction. Its count is shown as not included in
   server reporting. Sync, then refresh report and confirm no double counting.
10. Sign out/login another account or server. Previous account report must not
    appear, including during delayed requests. Export workbook and compare VBA if
    any financial mutation was exercised; report reads themselves never mutate it.

Private debug evidence stays ignored. These manual/opt-in device checks are not a
mandatory flaky UI E2E CI suite. JVM and backend integration tests run normally.
