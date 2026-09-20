# Phase 7 OCR baseline

## Selected implementation baseline

Phase 7 uses the bundled Latin ML Kit text-recognition artifact behind Lajur's
own `OcrEngine` interface. The bundled artifact was selected for the first
vertical slice because it is immediately available offline, supports the
project's Android API range, and avoids a separate runtime model-download state.
This is not a permanent model lock-in.

The application pipeline is:

```text
content URI
  -> MIME/size/dimension validation
  -> sampled bitmap decode + EXIF orientation
  -> bundled local OCR
  -> text blocks + bounding boxes
  -> human-visible OCR result
  -> empty manual draft (Phase 8 performs semantic extraction)
```

Hard limits for the baseline are 20 MB encoded size when reported by the
provider, 50 megapixels source resolution, and 2048 pixels on the processed
longest edge.

## Measurements on the Pixel 7 API 37 AVD

- bundled OCR completed offline with airplane mode enabled;
- synthetic 1200×500 text produced two blocks in 470 ms on one debug run;
- a 3000×1000 camera-style JPEG was sampled to 1500×500;
- the current debug APK is 58,539,070 bytes (debug packaging, not a release-size
  commitment).

Synthetic results prove integration and offline availability, not receipt
accuracy.

## Alternative path assessment

PaddleOCR/ONNX and Tesseract with explicitly bundled trained data remain viable
fully local candidates. Adding either now would introduce another runtime,
native/model assets, and significant APK/maintenance cost before the project has
a sanitized representative receipt dataset. A comparative engine benchmark is
therefore deliberately left open rather than claiming quality from synthetic
text alone.

The next benchmark must use sanitized examples covering payment screenshots,
QRIS, bank transfers, minimarket/restaurant receipts, faded thermal print,
rotation, and total-versus-balance ambiguity. It should compare exact text for
financial anchors, latency, peak memory, APK/model size, and maintenance cost on
the POCO F6-class target device.
