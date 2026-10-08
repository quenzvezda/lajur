# Local development setup

Lajur uses one Git repository with two independently buildable applications:

- Android client under `apps/android/`;
- ASP.NET Core API under `services/api/`.

They share product contracts, documentation, CI, Docker configuration, and sanitized workbook fixtures, but they do not share a runtime or IDE project model.

## Recommended IDEs

### Backend

Use Visual Studio with the **ASP.NET and web development** workload, or use the `.NET` CLI from any editor.

Required:

- .NET 10 SDK;
- Docker Desktop only when running the containerized development stack.

Run Backend verification from PowerShell:

```powershell
cd services/api
dotnet restore Lajur.slnx
dotnet build Lajur.slnx --no-restore
dotnet test Lajur.slnx --no-build
```

Run the API with:

```powershell
dotnet run --project src/Lajur.Api
```

Create a local operator-managed account before exercising authenticated routes:

```powershell
cd services/api
dotnet run --project src/Lajur.Api -- account create --username rama
```

The command prompts for a password without echoing it. Local auth data and the
generated JWT signing key live below the ignored `.local/lajur/` directory.

Install the approved clean workbook template before using the Create New Workbook
endpoint:

```powershell
New-Item -ItemType Directory -Force .local/lajur/templates
Copy-Item private/output/Keuangan_Pribadi_Template_2026.xlsm `
  .local/lajur/templates/finance-template.xlsm
```

Both paths are intentionally ignored by Git because the approved local template
may originate from a private workbook. Automated tests use only the committed
sanitized fixture. Do not substitute the owner's sole original workbook; always
keep that original outside the runtime directory.

### Android

Install the latest stable Android Studio and let its Setup Wizard install:

- Android SDK;
- Android SDK Platform-Tools (`adb`);
- Android Emulator;
- at least one SDK platform selected by the generated project;
- at least one emulator system image if a physical device is not used.

Use Android Studio's bundled JDK for Android builds unless the generated Gradle/Android Gradle Plugin configuration explicitly requires another version.

A separate global Gradle installation is not required. The repository will commit and use the Gradle Wrapper.

Run Android verification from PowerShell:

```powershell
cd apps/android
.\gradlew.bat testDebugUnitTest
.\gradlew.bat assembleDebug
.\gradlew.bat compileDebugAndroidTestKotlin lintDebug
```

The debug default is `http://127.0.0.1:8080/api/v1/`. Use ADB reverse to forward
the Android device's port 8080 to the host's port 8080; this also works with a
paired wireless-debugging device and keeps the Docker API bound to host loopback.
For an emulator without ADB reverse, override the login address with
`http://10.0.2.2:8080/api/v1/`. A different build default can be supplied with
`-PLAJUR_API_BASE_URL=https://host/api/v1/`.

### Wireless debugging and localhost

Enable Wireless debugging on Android and pair it with the host if it is not
already paired. Pairing and connection use separate ports shown by Android;
enter the pairing code in the local terminal, not in repository files.

```powershell
adb mdns services
adb devices -l
# If the paired device is not connected, use its current connection endpoint:
adb connect '<device-ip>:<connection-port>'
# Select the serial reported by adb devices; quote it if it contains spaces.
adb -s '<device-serial>' reverse tcp:8080 tcp:8080
adb -s '<device-serial>' reverse --list
adb -s '<device-serial>' install -r apps/android/app/build/outputs/apk/debug/app-debug.apk
```

Run installation from the repository root after building the APK. In Lajur, use
`http://127.0.0.1:8080/api/v1/` as the server address. `install -r` updates the app
while retaining its data. Repeat the reverse command after an ADB disconnect or
device restart; wireless connection ports can also change. Do not publish the
Docker API on the LAN just to use a connected development device.

Create an emulator system image in Android Studio Device Manager, or connect a
USB-debuggable device, before running:

```powershell
cd apps/android
.\gradlew.bat connectedDebugAndroidTest
```

The complete Phase 5 offline/restart/reconnect smoke procedure is documented in
`apps/android/README.md`.

For the combined Phase 5/6 device acceptance procedure, including emulator
fingerprint setup, see `docs/development/EMULATOR_PHASE5_6_SMOKE.md`.

The Gradle Wrapper JAR and both wrapper scripts are repository files and must be committed. Gradle caches, IDE state, `local.properties`, APK/AAB outputs, and signing material must remain untracked.

IntelliJ IDEA can open Kotlin/Gradle sources, but Android Studio is the recommended Android IDE because it bundles the supported Android tooling, SDK Manager, Device Manager, emulator integration, Compose preview, and layout inspection.

## Physical Android device

The target POCO F6 is valuable for biometric, camera, share-intent, WorkManager, OCR, local-LLM, memory, and latency testing.

Enable Developer options and USB debugging when device testing begins. Use sanitized receipts/screenshots for committed test assets.


## Local Docker stack

Run from the repository root:

```powershell
docker compose config
docker compose up --build --detach
docker compose ps
Invoke-WebRequest http://127.0.0.1:8080/health/ready
docker compose down
```

By default, the API is bound only to `127.0.0.1:8080`. Persistent local directories are created under ignored `.local/lajur/`. For the VPS, copy `.env.example` to an untracked `.env` and set the host paths to `/srv/lajur/*`.

## Expected repository layout

```text
apps/
  android/
services/
  api/
    src/
    tests/
tests/
  workbook-fixtures/
docs/
docker-compose.yml
```

Keep Backend and Android in the same repository, but give each subsystem its own build entry point:

- Backend: .NET solution/project files and `dotnet` commands;
- Android: Gradle wrapper and Android project files.

Root CI should invoke both builds independently.

## Local-only files

Do not commit:

- Android `local.properties`;
- IDE state such as `.idea/` and `.vs/`;
- signing keystores or passwords;
- environment secrets;
- real workbooks, receipts, or financial data;
- runtime data/backups.

Sanitized `.xlsm` fixtures belong only under `tests/workbook-fixtures/`.

## Bootstrap sequence

1. Scaffold the .NET 10 solution under `services/api/`.
2. Scaffold a native Kotlin + Jetpack Compose project under `apps/android/`.
3. Add the Gradle Wrapper generated by Android Studio.
4. Add a root `docker-compose.yml` for the API and persistent local mounts.
5. Add separate Backend and Android CI jobs.
6. Create a sanitized macro-enabled workbook fixture.
7. Prove the workbook mutation/preservation spike before expanding the app feature set.

This follows the active MVP execution plan: workbook integrity and offline synchronization come before Smart Scan.
