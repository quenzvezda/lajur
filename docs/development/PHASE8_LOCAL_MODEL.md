# Phase 8 local-model development

## Current state

Phase 8B contains a real, fully local LiteRT-LM runtime adapter, but deliberately
does not commit or bundle a model. Smart Scan immediately shows an OCR/evidence
draft without initializing LiteRT-LM. **Tambahkan saran AI** explicitly starts
optional local suggestions; without a compatible model, the evidence draft and
manual entry remain available. Normal launch and manual entry never initialize
LiteRT-LM.

The app expects exactly this private application file:

```text
files/models/lajur-extractor.litertlm
```

The fixed path prevents uploaded names from influencing storage. Model formats,
licenses, prompt templates, and output quality differ; a random `.litertlm` file
is not automatically a valid Lajur extractor.

The current Qwen development runtime reserves an explicit 1280-token total
context and at most 256 output tokens, with greedy sampling. A constrained JSON
transport requests only type/payment/category/merchant/description suggestions;
dates and amounts come from explicit OCR rules or manual entry, never live model
guesses. Unsupported numeric layouts may therefore need manual completion.
JSON constraints prevent extra Master-list properties but cannot prove semantic
accuracy or guarantee completion if a token limit is reached.
Type/payment/category choices are checked against Master by the deterministic
validator; known OCR type/payment retain the rules value even when the model
disagrees. The shipped grammar restricts shape and string lengths only; native
Master enums remain an unproven experiment, not a requirement.
Lazy loading starts only from the AI button, not alongside OCR. Nearby explicit
AI requests reuse the engine, with a 60-second idle cleanup after the
last request. Generation failure/cancellation performs non-cancellable cleanup.
A fresh-engine experiment did not resolve stalled device tests and was reverted;
Doze disabled their test wake lock, so these attempts do not isolate a native
runtime defect.

## Development side-load

Use an ARM64 physical device for meaningful testing. The existing Pixel AVD is
useful for the no-model fallback but is not evidence for target-device LLM
performance.

With a debug build already installed and a compatible local model at
`C:\path\to\model.litertlm`:

```powershell
adb push "C:\path\to\model.litertlm" /data/local/tmp/lajur-extractor.litertlm
adb shell run-as com.lajur.app mkdir -p files/models
adb shell run-as com.lajur.app cp /data/local/tmp/lajur-extractor.litertlm files/models/lajur-extractor.litertlm
adb shell rm /data/local/tmp/lajur-extractor.litertlm
```

Record the local artifact hash before testing:

```powershell
Get-FileHash -Algorithm SHA256 "C:\path\to\model.litertlm"
```

Never commit the model, screenshots, receipts, OCR output, or private workbook
data. Model extensions and common local model directories are ignored by Git.

## Smoke test

1. Force-stop and reopen Lajur; confirm Home/manual entry remain responsive.
2. Enable airplane mode before selecting a sanitized screenshot or receipt.
3. Run Smart Scan and confirm the draft appears without waiting for AI. Expand
   **Lihat teks struk & detail** to inspect OCR if needed.
4. Tap **Tambahkan saran AI**. Confirm AI progress appears and the draft remains
   usable. Cancel or proceed into the editor if you do not want to wait.
5. After completion, review each source label, evidence and alternative.
   Model-load/inference timing is under the expandable technical details.
6. Confirm out-of-Master values are absent/blocked and missing category is not
   guessed.
7. Open **Periksa & lengkapi transaksi** and confirm valid partial values prefill the
   ordinary editor; do not save unless all required fields are correct.
8. Request AI on another scan within 60 seconds and verify warm completion. After the idle unload,
   confirm a new cold load. Keep the screen awake until each scan finishes;
   background/Doze runs are not comparable foreground latency measurements.

For each candidate record model name/version/hash, backend, device/build,
amount/date/payment/category metrics, structured-output success, cold/warm
latency, and peak memory. Do not select a production model from one successful
example.

## Repeatable benchmark

Phase 8C adds an opt-in instrumented benchmark. It is deliberately excluded
from ordinary CI because it needs a private model artifact and meaningful
physical-device conditions. The committed `cases.synthetic.json` dataset checks
the harness only; use a larger sanitized dataset before comparing candidates.

From `apps/android`, with one unlocked physical device connected and the model
side-loaded at the fixed path above, build the two APKs, update-install them, then
run instrumentation directly. Avoid Gradle's `connectedDebugAndroidTest` on the
owner's daily-use install: the tested AGP runner removed the app before a
Xiaomi-restricted reinstall, erasing local state. Direct ADB update-installs
preserve existing data. Xiaomi may require on-device installation consent.

```powershell
.\gradlew.bat :app:assembleDebug :app:assembleDebugAndroidTest
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb install -r app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk
adb shell am instrument -w -r `
  -e class com.lajur.app.scan.eval.LocalModelBenchmarkInstrumentedTest `
  -e lajur.runModelBenchmark true `
  com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

The test prints `LAJUR_MODEL_BENCHMARK_REPORT=...` and writes a JSON report to
the app-specific external `benchmarks` directory. Copy reports into an ignored
private directory, for example:

```powershell
adb pull /sdcard/Android/data/com.lajur.app/files/benchmarks `
  ..\..\private\smart-scan-eval\reports
```

The report identifies the dataset, SHA-256 model hash, runtime/backend and
device. It records per-case output/errors, structured-output and validator-ready
rates, exact field metrics, category coverage and false-positive rate, cold-load
and inference latency, and maximum sampled process PSS. Run at least one cold
pass after force-stop and repeated warm passes under similar battery and thermal
conditions. Keep raw reports private until reviewed for sensitive OCR text.

Candidate rationale and licensing/performance caveats are maintained in
`docs/reference/LOCAL_MODEL_CANDIDATES.md`.

## Evidence-first receipt acceptance and diagnostics

The debug build shows a rules draft immediately after OCR, before any model is
requested. The card identifies field sources and offers expandable evidence.
Different explicit dates/totals stay missing with alternatives; absence of a
category is expected when neither the model nor the source supports one.

For the owner's synthetic minimarket receipt, check the Smart Scan card itself
before opening the editor: date `2026-09-20`, amount `68820`, payment `Bank`, no
`KEMBALI` description. The editor's default date is not proof of extraction.

1. Connect the POCO with Wireless debugging, start the Docker API, and restore
   `adb reverse tcp:8080 tcp:8080`; keep the app URL
   `http://127.0.0.1:8080/api/v1/`.
2. Select the same dummy image, inspect the OCR-only draft and required-field
   warnings, then expand **Lihat teks struk & detail** and tap **Simpan laporan
   uji (debug)**. Wait for the saved message before navigating away. This report
   has outcome "rules_only"; a model has not run. Optionally request AI next
   and save/read a second report to compare. Each save replaces the latest report.
3. Ask the agent to retrieve the latest report. No screenshot or manual log copy
   is needed. With a single connected device, the exact read is:

   ```powershell
   & "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" exec-out run-as com.lajur.app cat no_backup/scan-diagnostics/latest.json
   ```

4. Repeat a scan with an explicit AI request and save/read its report before overwriting it with another
   run. Test offline by disabling connectivity while keeping Wi-Fi available for
   Wireless ADB, or retrieve the report after reconnecting. Turning Wi-Fi off
   disconnects Wireless ADB but does not stop local OCR/extraction.
5. For a full transaction test, open the editor, verify all values and explicitly
   select any missing category. Save only dummy data. Confirm Pending offline,
   restart/unlock and check persistence, then reconnect/restore ADB reverse and
   confirm one Synced row. Export the year and verify one matching A:F workbook
   row in Excel. The JSON scan report proves extraction stages; it does not by
   itself prove the save/sync/workbook stages.

The report includes device/model hash, image dimensions, raw OCR/line coordinates,
reconstructed reading order, Master snapshot, rules fields, raw and parsed model
output, final validation issues and timings, including failed parsing runs.
`trace.generation` records total KV tokens, configured context/output limits and
whether JSON constraints were used. It is not an actual native stop reason or
separate input/output token count. It excludes image URIs, session
tokens, usernames and credentials. It is debug-only, saved explicitly, kept in
private no-backup storage, and overwritten on each save. Do not commit it or use
the save action on real receipts unless deliberately sharing their contents.
Logcat only emits a content-free confirmation under tag `LajurScan`:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" logcat -d -s LajurScan:I '*:S'
```

Remove the saved report when finished:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" shell run-as com.lajur.app rm no_backup/scan-diagnostics/latest.json
```

An opt-in device test replays the exact dummy image through the actual image
preprocessor, ML Kit, local model, rules and validator, writes the same report,
and asserts date/total/Bank without saving a financial transaction. Side-load
only the synthetic test image into the app's private no-backup test directory;
the test creates its own temporary FileProvider cache copy. Do not store the
source in the camera cache, which normal startup cleans:

```powershell
adb push "C:\path\to\dummy-receipt.png" /data/local/tmp/lajur-eval-receipt.png
adb shell run-as com.lajur.app mkdir -p no_backup/scan-eval
adb shell run-as com.lajur.app cp /data/local/tmp/lajur-eval-receipt.png no_backup/scan-eval/receipt.png
adb shell rm /data/local/tmp/lajur-eval-receipt.png
adb shell am instrument -w -r `
  -e class com.lajur.app.scan.eval.ReceiptImageExtractionInstrumentedTest `
  -e lajur.runReceiptImageTest true `
  com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

Ordinary CI skips this test. Model-quality selection still needs a larger dataset;
this replay proves only the supplied dummy receipt and the hybrid extraction.
Device evaluation acquires a test-only partial wake lock with a bounded ten-minute
timeout and releases it after the test; no production wake lock or device-setting
change is added. Doze can disable this lock: confirm the screen remains awake
and do not label a suspended test as a native inference hang without further
evidence.

To repeat with the actual cached Master from a deliberately saved dummy report,
preserve the report first (test runs overwrite `latest.json`):

```powershell
adb shell run-as com.lajur.app cp no_backup/scan-diagnostics/latest.json no_backup/scan-eval/captured-report.json
adb shell am instrument -w -r `
  -e class com.lajur.app.scan.eval.ReceiptImageExtractionInstrumentedTest `
  -e lajur.runReceiptImageTest true `
  -e lajur.useCapturedMaster true `
  -e lajur.receiptRepetitions 3 `
  com.lajur.app.test/androidx.test.runner.AndroidJUnitRunner
```

The test now also requires a completed parseable model suggestion, enabled JSON
constraints and KV usage below the configured context limit. Up to five repeated
runs keep per-run private reports under `no_backup/scan-eval/replay-N.json`.
The captured Master/report remains private; no transaction is saved. Keep other
Lajur use idle during instrumentation, then relaunch the app afterwards.

## Still required before Phase 8 exit

- sanitized/diverse real-world evaluation dataset (the committed synthetic set
  is only a harness smoke test);
- FunctionGemma/Qwen/Gemma-class candidate conversions compatible with the
  pinned runtime;
- POCO F6 quality, latency, and peak-memory benchmark;
- smallest acceptable model/backend selection;
- signed/checksummed delivery from the operator-owned VPS;
- airplane-mode acceptance using the selected production artifact.
