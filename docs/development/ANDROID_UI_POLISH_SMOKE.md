# Android toolbar reminders, Settings tabs and History filters

Use a disposable local account with synthetic rows. Build/debug installation is
emulator-only; preserve the physical phone, production workbook and release keys.

1. Have one synced transaction with blank payment. Home shows only a compact
   toolbar review icon with count and accessibility label, not a large reminder
   card. Tap it: History shows the incomplete row. Reset restores all rows; Home
   totals never change with filters/search. Complete payment later and refresh:
   badge count drops or disappears. A filtered search does not change badge count.
2. Open Filter & urutkan. Combine type/category/payment (including Belum diisi),
   dates, whole-IDR bounds and sync status. Date/amount bounds include equality.
   Nominal digits have thousands grouping and right alignment. Overflow and
   reversed bounds cannot be applied. Reset clears form; dismiss without Apply
   keeps the previous filters. Apply/Reset remain accessible while content scrolls.
3. Check newest/oldest and highest/lowest sorting, empty results, long category
   names, dark theme and narrow screens. Filters survive detail/edit navigation;
   changing month/account clears them. Insight drilldowns clear unrelated filters.
4. Settings has Akun, Keamanan, Data, Aplikasi tabs. Account recovery/invitation and
   signout remain reachable; app lock and screen privacy remain effective; Data
   opens Workbook; Aplikasi contains the installed version/update controls.
5. Without pressing Check, compare displayed version to Android Package Manager.
   Restart offline and return to Aplikasi; installed identity still matches. The
   ready/available state for a version already installed must clear without a
   network request. A genuinely newer available release remains available.
6. A physical official GitHub APK upgrade still needs release acceptance with
   the trusted signing key; a debug emulator package is not a signed release.
   No updater certificate/hash/HTTPS checks are relaxed by these UI changes.

JVM regression tests cover compound filters, inclusive bounds, deleted rows,
blank payment, sorting/sync state and installed-version reconciliation. Optional
device acceptance is not a mandatory flaky E2E CI suite. Store screenshots privately.
