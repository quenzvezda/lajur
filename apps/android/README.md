# Lajur Android

Native Kotlin/Compose client for manual, offline-first personal-finance entry.
Room is the local UI source of truth; WorkManager sends a compacted outbox to the
Lajur API when a network is available. The Android app never opens or edits XLSM
files directly.

## Local API address

Debug builds default to `http://127.0.0.1:8080/api/v1/`. Before using a local
Docker backend from an emulator or USB-connected device, forward the port once
for the current ADB session:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" reverse tcp:8080 tcp:8080
```

The login screen keeps the server field editable. `adb reverse` is preferred
over the emulator-only `10.0.2.2` alias because a backend bound to the Windows
loopback interface is not reachable through that alias on every Docker setup.

For a physical device, use the development computer's LAN address, for example
`http://192.168.1.10:8080/api/v1/`, and make the API listen on that interface.
Use HTTPS outside a trusted local development network. Cleartext HTTP is enabled
only in debug builds.

Override the compiled debug default when needed:

```powershell
.\gradlew.bat assembleDebug -PLAJUR_API_BASE_URL=https://lajur.example.com/api/v1/
```

## Verify

Signed builds from `main` are available as CI artifacts after all checks pass.
See [Android release operations](../../docs/operations/ANDROID_RELEASE.md) for
signing, versioning, backup and the debug-to-release transition.

```powershell
.\gradlew.bat testDebugUnitTest assembleDebug compileDebugAndroidTestKotlin lintDebug
```

Run the device-backed Room persistence test after creating an emulator in
Android Studio Device Manager or connecting a USB-debuggable device:

```powershell
.\gradlew.bat connectedDebugAndroidTest
```

An opt-in device-to-backend login probe is also available. It requires a local
test account and accepts credentials only as transient instrumentation
arguments; do not put credentials in source files.

## Phase 5 smoke test

1. Run the API, install its workbook template, and create an operator account.
2. Start the app, log in, and create the current-year workbook when prompted.
3. Refresh once so the Master choices are cached locally.
4. Enable airplane mode and add a transaction. It must immediately appear as
   `pending`.
5. Force-stop and reopen the app. The transaction must still be visible.
6. Disable airplane mode. The pending state must become synchronized without a
   second save.
7. Refresh/export the workbook and confirm exactly one canonical transaction row
   and one CREATE audit entry exist for that transaction ID.

Repeat with an offline edit and delete. A transaction created and then deleted
before its first sync should disappear locally without ever reaching the API.

The complete emulator runbook, including virtual fingerprint enrollment,
app-lock timeout, Recents privacy, background sync while locked, and the incoming
image gate, is in `docs/development/EMULATOR_PHASE5_6_SMOKE.md`.

## Phase 7 Smart Scan

Home exposes local OCR through **Pilih gambar** and **Kamera**; sharing an image
to Lajur enters the same pipeline after the app-lock gate. The Latin OCR model is
bundled in the APK and does not upload the image or recognized text.

Phase 8A/8B add the structured extraction contract, deterministic Master
validation, and a lazy LiteRT-LM adapter. No LLM artifact is bundled yet; when
the private model file is absent, OCR and manual entry continue normally with a
clear unavailable status. See `docs/development/PHASE8_LOCAL_MODEL.md` for the
fixed development path and physical-device benchmark procedure.

See `docs/development/PHASE7_SMART_SCAN_SMOKE.md` for the manual and opt-in
offline OCR checks.

## Local security

The refresh credential is encrypted with an AES-GCM key held by Android
Keystore. The encrypted blob is stored in private app preferences; access tokens
remain in memory only. Android backup is disabled so finance cache and session
material are not copied into device backups.
