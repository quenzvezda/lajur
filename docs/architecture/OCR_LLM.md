# OCR + local LLM contract

## 1. Hard privacy requirement

OCR and semantic extraction run locally on the Android device.

No receipt/screenshot image or OCR text may be sent to a third-party AI/OCR inference service.

The server does not need receipt images to save transactions.

## 2. Primary pipeline

```text
Image
  ↓
Image preprocessing
  ↓
OCR (local)
  ↓
Text (+ optional layout annotations)
  ↓
Local LLM
  ↓
Structured extraction result
  ↓
Deterministic validator
  ↓
Editable transaction draft
  ↓
Human review
  ↓
Local save/outbox
```

The LLM is the semantic extractor. Deterministic rules are validation/normalization/fallback helpers, not the main understanding engine.

## 3. Lazy lifecycle

The LLM must not load during:

- app process startup;
- login;
- Home;
- manual transaction entry;
- normal sync.

Load only when Smart Scan requires it.

OCR and LLM loading may run concurrently after an image is selected.

Proposed default model idle-unload timeout: 60 seconds, configurable.

## 4. Extraction schema

Conceptual result:

```json
{
  "schemaVersion": 1,
  "documentKind": "payment_success",
  "date": {
    "value": "2026-09-19",
    "alternatives": [],
    "status": "high",
    "evidence": "19 Sep 2026 13:42"
  },
  "amount": {
    "valueIdr": 128500,
    "alternativesIdr": [125000],
    "status": "low",
    "evidence": "Total pembayaran Rp128.500"
  },
  "transactionType": {
    "value": "Pengeluaran",
    "status": "medium"
  },
  "paymentMethod": {
    "value": "Shopee Pay",
    "alternatives": [],
    "status": "high"
  },
  "category": {
    "value": null,
    "alternatives": [],
    "status": "missing"
  },
  "merchant": {
    "value": "PT Toko ABC",
    "status": "medium"
  },
  "description": {
    "value": "Pembayaran ke PT Toko ABC"
  },
  "warnings": []
}
```

Exact wire/class structure may change, but behavior must remain.

## 5. Confidence/ambiguity UX

Do not require the model to pretend every field is unambiguous.

The engine returns:

- one primary candidate when possible;
- zero or more alternatives;
- coarse status: `high`, `medium`, `low`, `missing`.

UI normally hides `high`.

UI highlights low/missing values.

Do not display numeric “confidence percentages” unless a future calibrated model justifies them.

## 6. Null policy

If evidence is insufficient, return `null`.

This is especially important for category.

Example:

```text
TRANSFER BERHASIL
Rp500.000
BCA
```

Amount/payment may be extracted; category should remain null rather than guessed without support.

Required fields must be filled by the user before Save.

## 7. Master-constrained values

Provide current cached Master values to the extractor/validator:

- transaction types;
- payment methods;
- categories.

Model output for constrained fields must be one of the allowed values or null.

The deterministic validator rejects out-of-vocabulary output.

## 8. Candidate models

Do not permanently lock the model before benchmarking.

Initial benchmark set:

### FunctionGemma 270M

Strong candidate after task-specific fine-tuning because it is designed for structured function calling and on-device use.

Potential production target if extraction accuracy is sufficient.

### Qwen3 0.6B-class quantized model

Useful multilingual/Indonesian baseline with more general language capability.

### Gemma 3 1B-class quantized model

Useful general baseline if the very small specialized model is not accurate enough.

The project should select the smallest model that meets accuracy/latency requirements on the target device.

## 9. Function/schema prompting

Prefer constrained function/JSON-schema-style extraction over free-form chat.

Define one semantic action similar to:

```text
extract_transaction(
  date,
  transaction_type,
  payment_method,
  category,
  amount_idr,
  merchant,
  description,
  alternatives,
  warnings
)
```

Parser failures must not crash the app. Invalid output becomes an incomplete draft/fallback state.

## 10. OCR engine

OCR implementation must sit behind an interface.

Candidates may include a fully local PaddleOCR/ONNX pipeline or another bundled on-device engine.

Selection criteria:

- Indonesian/Latin receipt accuracy;
- payment-app screenshot accuracy;
- latency;
- APK/model footprint;
- offline behavior;
- maintenance cost.

Do not choose solely from synthetic benchmarks.

Phase 7 implements the first baseline with the bundled Latin ML Kit recognizer
behind the project-owned `OcrEngine` interface. Input decoding and EXIF
orientation live behind a separate `ImagePreprocessor` interface. This choice
proves the local/offline pipeline but does not close the comparative engine
benchmark; see `docs/reference/OCR_BASELINE.md`.

## 11. Evaluation dataset

Build a private/sanitized evaluation set covering:

- ShopeePay;
- GoPay;
- DANA;
- banks/virtual accounts;
- QRIS;
- digital payment success screenshots;
- minimarket receipts;
- restaurant receipts;
- thermal/faded receipts;
- rotated/perspective images;
- subtotal/tax/discount/admin-fee ambiguity;
- balance vs transaction-total ambiguity.

Prefer diversity over repeated examples from one template.

## 12. Metrics

At minimum measure:

- amount exact-match accuracy;
- date exact-match accuracy;
- payment method accuracy;
- category suggestion accuracy/coverage separately;
- valid structured-output rate;
- end-to-end scan latency;
- model cold-load latency;
- peak memory on POCO F6-class hardware.

A candidate model must not be accepted solely because it generates valid JSON.

## 13. Performance rule

Target device class: POCO F6 or better according to owner intent.

No hard latency number is specified before measurement, but UX goal is “a few seconds”, not tens of seconds.

Manual-entry startup must remain unaffected by model initialization.

## 14. Training/fine-tuning

Rules/history can improve personalization without retraining.

If FunctionGemma-class specialization is used:

- generate a synthetic diversified training set;
- add corrected real examples after sanitization;
- keep training artifacts/versioning outside the production transaction store;
- never upload private receipts to a third-party training service by default.

## 15. Model delivery

Packaging remains intentionally open until benchmark selection.

Acceptable options:

- bundle with app if size is acceptable;
- download signed/checksummed model artifact from the operator's own VPS and cache locally.

Third-party runtime download as a mandatory inference dependency is not desired.
