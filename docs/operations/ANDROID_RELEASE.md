# Signed Android release builds

## Automatic builds

The `CI` workflow tests backend, Android and the container. A successful push
to `main`, or a manual CI dispatch on `main`, then runs `Signed Android APK`.
Pull requests and other branches never run the signing job. The job also checks
the repository identity so forks cannot publish official Lajur builds.

Actions are pinned to full commit SHAs. The Android SDK setup action makes
`sdkmanager` available before installing API 37 and build-tools 36.0.0.
Its bootstrap package list is explicitly `platform-tools`; the action's default
also requests obsolete `tools`, which the SDK repository no longer provides.
Release packaging runs lint and requires signing credentials; it cannot
silently produce an unsigned APK. Domain tests run in the preceding Android
job (`testDebugUnitTest`); the current AGP configuration has no release unit-test
task.

Each build uses `versionCode = 100000 + github.run_number` and
`versionName = 1.0.<run_number>`. Retain the CI workflow's run-number sequence;
if the workflow is replaced/reset, choose a larger version-code base before
distributing any APK. Rerunning the same workflow run keeps its version code.

APK verification checks the expected signing certificate before uploading:

- `lajur-<version>.apk`;
- `SHA256SUMS`;
- `build-metadata.json` containing version, application ID, source commit and
  public certificate fingerprint.

Download the `lajur-release-<run_number>` artifact from the completed Actions
run. Retention is 30 days. Actions artifacts currently require a GitHub login;
this stage does not publish GitHub Releases or implement the in-app updater.

## Signing secrets

The GitHub environment `android-release` permits only branch `main` and holds:

| Secret | Purpose |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | PKCS12 keystore encoded for transport |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | Signing-key alias |
| `ANDROID_KEY_PASSWORD` | Signing-key password |

Base64 is encoding, not encryption. Treat the encoded keystore as sensitive.
Secrets are passed only to the build step, never as Gradle command-line
arguments. The decoded keystore is created with private permissions in the
runner's temporary directory and removed when the step exits. Configuration
caching is disabled for signing builds.

Environment variables (public GitHub Variables) hold:

| Variable | Value/meaning |
| --- | --- |
| `ANDROID_API_BASE_URL` | `https://lajur.solusi-program.site/api/v1/` |
| `ANDROID_SIGNING_CERT_SHA256` | Expected public signing-certificate SHA-256 |

The proposed hostname is not yet deployed. The existing login screen lets the
operator change the server address; release builds allow HTTPS only. DNS,
Cloudflare and VPS bootstrap are a later stage.

## Local key custody and backup

A dedicated RSA-4096 release key was generated on 2026-10-10 and retained in
ignored `private/signing/`, with Windows access limited to the current user and
SYSTEM. Its validity is 10,000 days. GitHub stores the same key in environment
secrets; no secret values are committed or included in artifacts.

Back up the keystore and its credentials in an encrypted location outside this
checkout before deleting/reinstalling the workstation. A copy on an independent
device is recommended. GitHub Secrets cannot be downloaded to recover a lost
key. Do not regenerate or replace the key for routine updates.

The public certificate SHA-256 is:

```text
5f2bb88e0435324ddb7a89dca1f0924e2bd53ca49e39ac0778dab64d9f6efe1b
```

## Local release build

Set these variables locally without committing their values:

- `LAJUR_SIGNING_STORE_FILE` — absolute keystore path;
- `LAJUR_SIGNING_STORE_PASSWORD`;
- `LAJUR_SIGNING_KEY_ALIAS`;
- `LAJUR_SIGNING_KEY_PASSWORD`;
- `LAJUR_VERSION_CODE` and `LAJUR_VERSION_NAME`;
- `LAJUR_RELEASE_API_BASE_URL` — optional HTTPS `/api/v1/` endpoint. When absent,
  the release login server field starts empty rather than pointing at localhost.

Then run `./gradlew lintRelease assembleRelease --no-configuration-cache`.
Debug builds keep their existing localhost default and do not require signing
secrets. No LLM artifact is bundled; OCR/manual entry retain their existing
fallback behavior.

## Transition from development builds

The application ID remains `com.lajur.app`. Existing debug installations use
a different key, so this release cannot update them in place. Do not uninstall
a debug app with pending offline transactions: synchronize them and verify
server/workbook data first. This task does not uninstall, replace, clear data
or install anything on the owner's Android device.

After the first release installation, subsequent release builds use the same
key and a higher version code for in-place updates. Installer and offline-data
retention acceptance will be verified when implementing the updater.

References: [Android app signing](https://developer.android.com/studio/publish/app-signing),
[GitHub Actions secrets](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).
