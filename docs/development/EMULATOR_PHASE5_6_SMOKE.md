# Emulator smoke test — Phase 5 and Phase 6

This runbook verifies the Android offline transaction flow and the local app
lock. Use only synthetic transaction data.

## 1. Create and start an Android Virtual Device

The Android Emulator executable can be installed without an AVD being created.
If `emulator -list-avds` returns nothing:

1. Open Android Studio.
2. From the Welcome screen choose **More Actions → Virtual Device Manager**. If
   a project is open, choose **Tools → Device Manager**.
3. Select **Create virtual device**.
4. Choose a phone such as **Pixel 7** and select **Next**.
5. Download/select a stable Google APIs x86_64 system image, preferably API 36
   or newer.
6. Finish the wizard and press the Play button for the new device.
7. Wait until the Android home screen is fully loaded.

Confirm the device from PowerShell:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices -l
```

## 2. Configure device credential and fingerprint

1. In the emulator open **Settings → Security & privacy → Device unlock →
   Screen lock**.
2. Configure a test PIN. Do not reuse a real PIN.
3. Open the fingerprint enrollment screen.
4. In the emulator toolbar select **… (Extended controls) → Fingerprint**.
5. Enter Finger ID `1` and press **Touch sensor** whenever Android asks for a
   fingerprint touch. Repeat until enrollment completes.

The same touch can be sent from PowerShell while the emulator is running:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -e emu finger touch 1
```

## 3. Start the local backend

From the repository root, install the private local template and run Docker:

```powershell
New-Item -ItemType Directory -Force .local/lajur/templates
Copy-Item private/output/Keuangan_Pribadi_Template_2026.xlsm `
  .local/lajur/templates/finance-template.xlsm -Force
docker compose up --build --detach
docker compose ps
```

Create a local-only account if one does not already exist:

```powershell
docker compose exec api dotnet Lajur.Api.dll account create --username emulator
```

Use `account reset` instead of `account create` when the username already
exists. The command asks for a password of at least 12 characters without
echoing it.

## 4. Install and open Lajur

From the repository root:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" reverse tcp:8080 tcp:8080
.\apps\android\gradlew.bat -p apps/android installDebug
```

Alternatively press **Run** in Android Studio. On the login screen use:

- server: `http://127.0.0.1:8080/api/v1/`;
- the local operator username/password created above.

The ADB forwarding command must be repeated after restarting the emulator or
ADB server. It lets the app reach a Docker API that remains safely bound to the
development computer's loopback interface.
Create the current-year workbook when the onboarding screen appears, then press
Refresh once so Master values are cached.

## 5. Phase 5 offline/restart/reconnect test

1. Enable airplane mode in the emulator.
2. Add a synthetic transaction and save it.
3. Confirm it appears immediately with status `pending`.
4. Force-stop Lajur from Android Settings, then open it again.
5. Confirm the pending transaction is still present.
6. Disable airplane mode.
7. Wait for WorkManager; press **Refresh** only after the pending indicator has
   disappeared.
8. Confirm the transaction becomes synchronized and is not duplicated.

Run the Room restart instrumentation test while the emulator is connected:

```powershell
.\apps\android\gradlew.bat -p apps/android connectedDebugAndroidTest
```

## 6. Enable and verify the app lock

1. Open **Pengaturan** from Lajur Home.
2. Enable **Kunci aplikasi**.
3. Complete the fingerprint/PIN prompt. The setting is saved only after a
   successful prompt.
4. Confirm **Fallback kunci perangkat** is enabled.
5. Confirm the default timeout is **5 menit**.
6. Switch briefly to another app and return before five minutes. Lajur should
   remain open.
7. Return to Settings, choose **Segera**, leave Lajur, and open it again. The
   unlock gate must appear.
8. Test once with fingerprint and once with the emulator PIN.
9. Restore the timeout to **5 menit**.

If fingerprint-only mode is desired, disable device-credential fallback. Lajur
requires a successful biometric prompt before saving that change, preventing a
device with no enrolled biometric from being locked out accidentally.

## 7. Recents privacy

1. In **Pengaturan**, confirm **Lindungi layar & Recent Apps** is enabled.
1. Open a transaction detail screen.
2. Open Android Recent Apps.
3. The Lajur card must not contain readable financial content.
4. A normal screenshot of Lajur should be blocked because `FLAG_SECURE` is on.
5. Disable **Lindungi layar & Recent Apps**, repeat the checks, and confirm
   screenshots and the Recents preview are now allowed.
6. Enable the protection again before continuing the security smoke test.

## 8. Background sync while UI is locked

1. Set app-lock timeout to **Segera**.
2. Disable emulator networking/enable airplane mode.
3. Add a synthetic transaction and confirm it is pending.
4. Leave Lajur so the UI becomes locked.
5. Restore networking without reopening/unlocking Lajur.
6. Wait about 30 seconds for WorkManager.
7. Reopen Lajur, unlock it, and verify the transaction is synchronized.

No biometric prompt should appear merely because WorkManager ran.

## 9. Share-intent lock gate skeleton

1. Lock Lajur using **Kunci sekarang** or the immediate timeout.
2. In the emulator Photos/Files app, share any synthetic image to **Lajur**.
3. Lajur must show its unlock gate before showing that an image was received.
4. After unlock, Home displays a temporary **Gambar diterima** card.
5. Select **Buang gambar**. OCR/image decoding intentionally starts in Phase 7.

## 10. Stop the backend

When testing is finished:

```powershell
docker compose down
```

The ignored `.local/lajur/` directories retain local auth and workbook data.
