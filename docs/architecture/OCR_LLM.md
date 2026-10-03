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
Text + OCR line positions → reconstructed reading order
  ↓
Explicit evidence rules
  ↓
Structured extraction result
  ↓
Deterministic validator
  ↓
Editable transaction draft
  ↓ (optional user action: local LLM suggestions, merge, revalidate)
Updated review draft
  ↓
Human review
  ↓
Local save/outbox
```

Explicit numeric dates and labelled totals use evidence rules. The local LLM
supplies type/payment/category/merchant/description suggestions only. Dates and
amounts absent from explicit OCR evidence require manual entry. Supported OCR fields
survive missing or failed model output; multiple distinct explicit candidates
stay missing with alternatives even if the model guesses one. See ADR 0007.

## 3. Lazy lifecycle

The LLM must not load during:

- app process startup;
- login;
- Home;
- manual transaction entry;
- normal sync.

Load only when the user requests optional AI suggestions on a scan draft.
Image selection, OCR and rules-only review must not load a model.

Proposed default model idle-unload timeout: 60 seconds, configurable.

The lifecycle uses a per-request lease. The explicit AI action starts lazy
initialization; runtime work is serialized. Nearby
requests reuse the resident engine; the last released lease schedules a
60-second idle unload. An abandoned preparation follows the same cleanup.
Generation failure or cancellation unloads the engine in a non-cancellable
cleanup block. A fresh-engine experiment was reverted because repeated tests
also stalled under that policy while POCO was Dozing and its test wake lock
was disabled. Those timings do not establish a native-runtime or reuse defect;
repeatable target-device evaluation must keep the device awake.

An in-memory review session retains the rules draft while the optional job runs.
Cancel, discard, image replacement, and entry into the editor invalidate late
results. Native synchronous inference may finish before cancellation cleanup;
the UI must not wait for it or allow it to overwrite the editor.

## 4. Extraction schema

Conceptual result:

```json
{
  "schemaVersion": 1,
  "documentKind": "payment_success",
  "date": {
    "value": "2026-09-19",
    "alternatives": [],
    "confidence": "high",
    "evidence": "19 Sep 2026 13:42"
  },
  "amountIdr": {
    "value": 128500,
    "alternatives": [125000],
    "confidence": "low",
    "evidence": "Total pembayaran Rp128.500"
  },
  "transactionType": {
    "value": "Pengeluaran",
    "alternatives": [],
    "confidence": "medium"
  },
  "paymentMethod": {
    "value": "Shopee Pay",
    "alternatives": [],
    "confidence": "high"
  },
  "category": {
    "value": null,
    "alternatives": [],
    "confidence": "missing"
  },
  "merchant": {
    "value": "PT Toko ABC",
    "alternatives": [],
    "confidence": "medium"
  },
  "description": {
    "value": "Pembayaran ke PT Toko ABC",
    "alternatives": [],
    "confidence": "medium"
  },
  "warnings": []
}
```

Phase 8A implements this as serializable Android domain classes independent of
the eventual model runtime. `schemaVersion` rejects incompatible model output
instead of attempting a best-effort parse.

The deterministic validator:

- accepts only ISO `YYYY-MM-DD` dates and positive whole-rupiah amounts;
- case-insensitively canonicalizes transaction type, payment method, category,
  and their alternatives to the spelling in the cached workbook Master;
- rejects values outside Master and categories belonging to another
  transaction type;
- preserves valid partial values for manual-editor prefill;
- converts missing values to `missing` confidence and a value incorrectly
  marked `missing` to `low` with a warning;
- never manufactures a description from merchant or other evidence.

Missing Master data and unsupported schema versions are blocking validation
issues. Validation never turns an extraction directly into a saved transaction.

## 5. Confidence/ambiguity UX

Do not require the model to pretend every field is unambiguous.

The engine returns:

- one primary candidate when possible;
- zero or more alternatives;
- coarse status: `high`, `medium`, `low`, `missing`.

UI normally hides `high`.

UI highlights low/missing values.

Each field also identifies `ocr_rules`, `local_model`, or unknown source. These
coarse confidence labels are review hints, not calibrated probabilities. Direct
primitive model outputs receive low confidence because confidence is absent.

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

Required finance fields are date, type, category and positive whole-IDR amount.
Payment is optional: a missing/blank extraction value is not a blocking issue,
and becomes an empty string only when converted to a reviewed transaction draft.
An unknown nonblank suggestion is still rejected against Master. This policy
does not change the OCR/evidence algorithm, prompt, runtime or model benchmark.

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

The current small-model policy bounds semantic OCR prompt input to 1,600
characters, the complete prompt to 3,500 characters and model responses to
64,000 characters. These character guards are not token estimates. Rules always
inspect the full OCR reading-order text, not the clipped semantic prompt.
OCR text is JSON-escaped and explicitly identified as
untrusted data in the prompt. The parser tolerates a surrounding Markdown fence
but requires an explicit `schemaVersion`; incompatible or malformed output is
shown as an extraction failure while the raw OCR and manual-entry route remain
available.

At the untrusted model-output seam, an explicit JSON `null` for one of the
extracted field objects is normalized to the ordinary missing-field object. This
preserves the model's safe abstention semantics across small-model variations.
A direct primitive for one of those fields is wrapped as a low-confidence value;
its declared Kotlin type must still decode correctly. Unsupported arrays or
object shapes remain decoding failures, and the validator still owns all format,
Master, category/type compatibility, and required-field checks.

An unrecognized primitive document-kind label becomes `unknown` with a warning;
it cannot invalidate otherwise type-compatible fields. Explicit receipt evidence
can classify the merged draft. Fixed example dates/amounts are excluded from the
prompt to reduce contamination; actual values come only from the OCR payload.

Before validation, a deterministic normalizer handles narrowly supported receipt
conventions. Valid `D/M/YYYY` dates become ISO dates. Explicit OCR evidence of
`QRIS` maps to the canonical `Bank` payment method only when `Bank` exists in the
workbook Master; otherwise payment is left missing rather than guessed as Cash.
For receipt documents, exact structural labels `TOTAL`, `SUBTOTAL`, `PPN`, and
`KEMBALI` are discarded when a small model emits one as the description. These
rules use source evidence and Master values; they do not silently invent ledger
data.

Since 2026-10-03, OCR retains line bounding boxes. Lines on the same vertical row
are joined left-to-right, and rows are sorted top-to-bottom; absent/incomplete
geometry falls back to raw text. Explicit valid numeric `D/M/YYYY`, `D-M-YYYY`,
`D.M.YYYY`, ISO dates and Indonesian/English month-name dates (four-digit year)
are extracted from OCR independently of model output.
Known expiry/due-date rows are excluded. A unique date is suggested with evidence;
different dates stay missing with alternatives. A whole-IDR value on the same
reconstructed row as TOTAL/GRAND TOTAL/total-payment labels is preferred over
model guesses. When no TOTAL label is present, explicit nominal/transfer,
payment-amount and trip-cost labels are also supported. Subtotal, tax, balance,
tender and change are not used as total,
and an unpaired total label never selects a nearby/largest number. Conflicting
explicit totals stay missing. The earlier full-schema replay parser still accepts
legacy numeric suggestions for diagnostics, but live drafts discard model dates
and amounts. Supported OCR type/payment likewise ignore conflicting suggestions.
A receipt with SUBTOTAL/TOTAL/KEMBALI can suggest
Master's Pengeluaran with medium confidence; category remains a semantic/manual
choice. No merchant/header heuristic is added in this slice.

The rules draft appears immediately after OCR, before any model is requested.
Local model inference starts only from the optional AI button.
Missing-model, generation and parse failures retain a validated rules draft.
An explicit debug-only **Simpan laporan uji (debug)** action stores the latest
JSON report in `no_backup/scan-diagnostics/latest.json`; it contains raw OCR,
line positions, reading-order text, Master snapshot, rules, raw/parsed model
output, validation issues, final fields, model hash and timings, including failed
parse attempts. Runtime diagnostics include total KV tokens after generation,
configured context/output limits and whether JSON constraints were enabled;
they do not claim a native stop reason or separate input/output token counts. Reports omit
image URIs, account/session data and credentials. Receipt content is not written
to logcat. See the Phase 8 development runbook for ADB capture and removal.

Rules-only reports have outcome "rules_only" with no model generation or
inference timings. Normal UI emphasizes source/evidence rather than prominent
coarse confidence labels. Raw OCR and technical details are collapsed by default.

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

### Phase 8B runtime baseline

The first production adapter is pinned to LiteRT-LM Android `0.16.0`, using its
stable Kotlin interface and CPU backend as a conservative baseline. Google now
recommends LiteRT-LM over the older MediaPipe LLM Inference API, which is in
maintenance-only mode. This selects a replaceable runtime adapter, not the
winning model or backend.

The current development policy explicitly uses a 1,280-token input-plus-output
context, 256 output tokens and greedy sampling (topK=1, temperature=0, seed=0).
These conservative limits match the current Qwen artifact and must be revisited
when another artifact is benchmarked. The compact model transport requests only
`schemaVersion=1`, `transactionType`, `paymentMethod`, `category`, `merchant`, and
`description`; nullable strings are converted to the unchanged domain field
objects with low-confidence model provenance. JSON Schema constrained decoding
forbids additional properties and bounds string lengths. The prompt requests
null for known OCR type/payment; live drafts preserve those rules values even
if the model disagrees. Master vocabulary and category/type compatibility are
enforced by the ordinary validator, not native grammar enums. An enum-based
grammar experiment was removed; device Doze confounded the subsequent stalled
tests, so enum grammar is not established as their cause. Constraints enforce
shape, not factual correctness; Master and human review remain mandatory.
Malformed/truncated output is not repaired by inventing missing delimiters or
values: it retains the OCR draft and a friendly failure message.

Pinned implementation references:

- [LiteRT-LM 0.16 configuration](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.16.0/kotlin/java/com/google/ai/edge/litertlm/Config.kt)
- [LiteRT-LM 0.16 JSON response format](https://github.com/google-ai-edge/LiteRT-LM/blob/v0.16.0/kotlin/java/com/google/ai/edge/litertlm/ResponseFormat.kt)
- [Current Qwen artifact's 1280-token KV cache](https://huggingface.co/litert-community/Qwen3-0.6B-int4/blob/main/README.md)

The app looks only for the fixed private file
`files/models/lajur-extractor.litertlm`. It never scans an untrusted filename,
bundles a model in the APK, or downloads one from a third-party inference
provider. A production download from the operator's own VPS still needs a
signed/checksummed delivery design. Development side-loading and benchmark
instructions live in `docs/development/PHASE8_LOCAL_MODEL.md`.

References checked for Phase 8B:

- [LiteRT-LM Kotlin API](https://github.com/google-ai-edge/LiteRT-LM/blob/main/docs/api/kotlin/getting_started.md)
- [Google AI Edge Android LLM guide](https://developers.google.com/edge/mediapipe/solutions/genai/llm_inference/android)

### Phase 8C evaluation boundary

Model evaluation is an opt-in instrumented workflow, not a launch-time app
feature or required CI test. A shared evaluator scores structured output,
validator readiness, exact fields, category coverage/false positives, latency,
and sampled process PSS. The committed OCR-only dataset is synthetic and exists
to prove the harness; production selection requires sanitized representative
data and repeated POCO F6 runs. Candidate facts and the current benchmark order
are recorded in `docs/reference/LOCAL_MODEL_CANDIDATES.md`.
