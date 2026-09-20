# Phase 8 local-model development

## Current state

Phase 8B contains a real, fully local LiteRT-LM runtime adapter, but deliberately
does not commit or bundle a model. Without a compatible model, Smart Scan still
finishes OCR, reports **Model lokal belum terpasang**, and keeps manual entry
available. Normal launch and manual entry never initialize LiteRT-LM.

The app expects exactly this private application file:

```text
files/models/lajur-extractor.litertlm
```

The fixed path prevents uploaded names from influencing storage. Model formats,
licenses, prompt templates, and output quality differ; a random `.litertlm` file
is not automatically a valid Lajur extractor.

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
3. Run Smart Scan and confirm raw OCR remains visible.
4. Confirm the extraction status displays cold model-load plus local inference
   latency (or **Model siap** on a warm scan) rather than a missing-model message.
5. Review every value, evidence fragment, alternative, and confidence marker.
6. Confirm out-of-Master values are absent/blocked and missing category is not
   guessed.
7. Open **Isi transaksi manual** and confirm valid partial values prefill the
   ordinary editor; do not save unless all required fields are correct.
8. Scan again within 60 seconds and compare warm latency; wait beyond 60 seconds
   and scan once more to capture cold-load latency.

For each candidate record model name/version/hash, backend, device/build,
amount/date/payment/category metrics, structured-output success, cold/warm
latency, and peak memory. Do not select a production model from one successful
example.

## Still required before Phase 8 exit

- sanitized/diverse evaluation dataset;
- FunctionGemma/Qwen/Gemma-class candidate conversions compatible with the
  pinned runtime;
- POCO F6 quality, latency, and peak-memory benchmark;
- smallest acceptable model/backend selection;
- signed/checksummed delivery from the operator-owned VPS;
- airplane-mode acceptance using the selected production artifact.
