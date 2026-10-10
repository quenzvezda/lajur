# Android app-update acceptance

Use a disposable emulator/account first. Never uninstall an owner's installation
with pending offline changes. Release and debug signing keys differ.

## Automated checks

Run `testDebugUnitTest assembleDebug assembleDebugAndroidTest lintDebug`, and
`lintRelease assembleRelease --no-configuration-cache` with private signing
environment variables. JVM coverage includes metadata/URL rejection, daily
throttling, exact APK identity/certificates, size/hash failures and cancellation
cleanup. The UI smoke is optional, not a mandatory CI emulator suite:

```sh
adb -s <disposable-emulator> install -r app-debug.apk
adb -s <disposable-emulator> install -r app-debug-androidTest.apk
adb -s <disposable-emulator> shell am instrument -w \
  -e class com.lajur.app.ui.AppUpdateUiTest \
  -e lajur.runUpdateUiSmokeTests true \
  com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

The synthetic host exists only in the debug source set, is not exported and
never downloads or installs packages. It covers download/cancel callbacks,
progress, permission action, error/retry and incompatible-key presentation in
dark and light modes. Platform accessibility avoids the existing Espresso
`InputManager.getInstance` incompatibility on the API 37 emulator. Screenshot
artifacts are written to the debug app cache, not committed.

## Real signed-release flow

1. Install an older official-key signed build containing the updater on the
   disposable emulator. Its version code must be below the latest GitHub Release.
2. Open Login without backend connectivity. A newer version is offered. Manual
   **Cek pembaruan** works independently of login and the backend address.
3. Go offline and manually check: show a retry message without crashing. Existing
   offline entry remains available to a signed-in account. Restore connectivity.
4. Start **Unduh APK**, cancel and retry. Progress clears and no partial file is
   offered for installation. Let the second download finish.
5. Force-stop/reopen: the verified APK is available without downloading again.
   Deleting/corrupting cache instead requires a new download, never an install.
6. Tap **Izinkan pemasangan**. Grant permission in Android, return, then explicitly
   tap **Pasang pembaruan**. Confirm Android's package update dialog.
7. Reopen: verify the newer version, unchanged account/preferences, cached
   transactions and pending outbox. Reconnect and verify a pending transaction
   synchronizes once. Use synthetic finance data only.
8. With the latest version installed, **Cek pembaruan** reports up to date. In
   Settings, disable automatic checks, restart and verify the switch persists.

Repeat on the owner's phone after release installation, including an app-lock
timeout during the permission/installer round trip. Installation itself can
interrupt the process; first save any unsaved editor draft.

## Validation record

Local JVM/build/lint and optional emulator observations are recorded in the
active execution plan. Device-specific installer/permission behavior and finance
outbox retention are separate acceptance items; a synthetic UI pass alone does
not prove them.

On 2026-10-10, the isolated API 37 Pixel 7 passed the optional UI test and a real
official-key 1.0.3-preview → public 1.0.4 update via the platform installer. Public
discovery, cancellation/retry, completed-cache recovery after force-stop and the
permission return requiring a second tap passed. Package firstInstallTime was
unchanged. The test used the signed-out screen and did not seed finance/outbox
data or exercise the owner's physical phone. Play Protect can offer an optional
scan for a new sideloaded APK; this is additional system UI outside Lajur.
