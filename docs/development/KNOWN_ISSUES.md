# Known issues

Issues listed here are confirmed but intentionally deferred. They must not be
silently treated as passing acceptance criteria. A test-only intermittent failure
is labeled separately and is not a confirmed production defect.

## API-001 — Intermittent concurrent-create test HTTP 500

- **Status:** Observed once, cause unconfirmed; investigate if repeated.
- **Environment:** Windows .NET 10 local full solution test run, 2026-10-03.
- **Observed:** One of ten concurrent identical creates returned HTTP 500 in the
  existing integration test. The focused rerun and subsequent full solution run
  passed without changing the create/locking implementation. New optional-payment
  tests passed in both full runs; this does not prove the intermittent fault fixed.
- **Follow-up:** Capture failing request/problem and server exception under a
  repeated concurrent harness; distinguish workbook file access from auth/test
  infrastructure contention before proposing a fix. The assertion now includes
  the failed response body. Do not suppress this regression or weaken CI assertions.
- **Limit:** The requested five-run shell loop was blocked by the terminal
  allowlist; no high-reproduction-rate stress result is claimed.

## AND-001 — Recent Apps content protection is ineffective on the test AVD

- **Status:** Deferred
- **Priority:** Minor for the current private MVP workflow
- **Security impact:** The Recent Apps card can expose visible financial data;
  users must not rely on the current toggle as a privacy boundary.
- **Environment:** Pixel 7 Android Virtual Device, API 37, Android debug build.
- **Observed:** Transaction details remain readable in Recent Apps. After adding
  the user-facing **Lindungi layar & Recent Apps** option, both enabled and
  disabled states still show application content in the Recents card.
- **Expected:** Enabled hides the task snapshot and blocks screenshots; disabled
  permits them.
- **Evidence already collected:** The active application window contains
  `FLAG_SECURE`. The Android 13+ `setRecentsScreenshotEnabled(false)` path is
  also invoked when protection is enabled, but the visual AVD check still fails.
- **Workaround:** Do not leave sensitive details visible when opening Recent
  Apps. Lock or sign out of Lajur when device privacy is important.
- **Follow-up:** Reproduce on a physical target device and a stable non-preview
  emulator image; inspect launcher/task-snapshot behavior; then decide whether
  to use a custom non-sensitive task snapshot or hide Lajur from Recents while
  protection is enabled.
