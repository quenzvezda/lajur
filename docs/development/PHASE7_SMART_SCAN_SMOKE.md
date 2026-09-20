# Phase 7 Smart Scan smoke test

This runbook verifies local image acquisition, bounded preprocessing, and OCR.
Use only synthetic or sanitized images.

## Automated checks

From the repository root with an emulator connected:

```powershell
.\apps\android\gradlew.bat -p apps/android `
  testDebugUnitTest assembleDebug compileDebugAndroidTestKotlin lintDebug
.\apps\android\gradlew.bat -p apps/android connectedDebugAndroidTest
```

The bundled-model OCR probe is opt-in because it performs real inference:

```powershell
.\apps\android\gradlew.bat --% -p apps/android connectedDebugAndroidTest `
  -Pandroid.testInstrumentationRunnerArguments.lajurOcrE2e=true
```

It generates a synthetic `LAJUR / TOTAL RP 150000` bitmap on-device and asserts
that both the word and amount are recognized. The test also logs recognition
latency under the `LajurOcrBenchmark` tag. It can be run with airplane mode on;
the Latin OCR model is bundled in the APK.

## Gallery path

1. Open Lajur and unlock it.
2. On Home, select **Pilih gambar** in the Smart Scan card.
3. Choose a sanitized receipt or payment screenshot.
4. Confirm the Smart Scan screen opens and eventually shows **Hasil OCR**.
5. Confirm image dimensions and OCR time are displayed.
6. Confirm **Draft transaksi** marks date, amount, payment, and category as not
   yet extracted. Phase 7 must not guess or save them.
7. Select **Isi transaksi manual** and confirm the ordinary editor opens empty,
   or select **Buang hasil** to return Home.

## Camera path

1. Select **Kamera** from the Smart Scan card.
2. Photograph synthetic text with good lighting.
3. Accept the photo and confirm OCR results appear.
4. Cancel once and confirm no stale scan card/result remains.

Camera images are created only in the private application cache. They are
deleted after successful OCR, cancellation, replacement, or the next cold app
start.

## Share and lock gate

1. Lock Lajur.
2. Share a sanitized image from Photos/Files to Lajur.
3. Confirm biometric/device authentication appears before OCR results.
4. Unlock and confirm Smart Scan runs locally.
5. Use **Buang hasil** and confirm Home no longer contains pending scan state.

## Offline proof

1. Enable airplane mode before selecting the image.
2. Run Gallery, Camera, or Share OCR.
3. Confirm text is produced without enabling connectivity.
4. Restore connectivity for transaction synchronization afterward.

## Safety cases

- Inputs must use a granted `content://` URI; raw `file://` paths are rejected.
- Encoded input is limited to 20 MB when its length is available.
- Source dimensions are limited to 50 megapixels.
- Decode is sampled so the processed longest edge is at most 2048 pixels.
- Empty OCR is a review warning, never an automatic transaction.
- Images and OCR text are not sent to the Lajur API.

## Recorded emulator acceptance

On 2026-09-20, the Pixel 7 API 37 AVD passed:

- Gallery OCR for a 409×864 synthetic/dummy-data UI screenshot in 1756 ms;
- OCR with airplane mode enabled;
- Share Intent through the app-lock gate into OCR results;
- **Isi transaksi manual** opening the ordinary empty transaction form.

The Camera path was intentionally skipped because emulator webcam input was not
configured. It remains untested rather than failed. The screenshot used proves
the acquisition/OCR integration, but a sanitized representative payment image
or receipt is still required for the Phase 7 exit criterion.
