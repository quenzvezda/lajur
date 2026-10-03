# Local extraction model candidates

Status: Phase 8C research shortlist, not a production-model decision.

Lajur needs a local model that turns untrusted Indonesian OCR text into its
strict extraction JSON. Public model-card numbers are useful for ordering tests,
but they are not evidence of Lajur extraction quality or POCO F6 performance.
Every artifact must therefore pass the repository benchmark with its exact hash.

## Recommended benchmark order

### 1. Qwen3 0.6B dynamic INT4

This is the first zero/few-shot baseline to try. The upstream Qwen checkpoint is
Apache-2.0, and the LiteRT Community repository currently publishes a 328 MB
dynamic-INT4 `.litertlm` artifact as well as larger INT8 and mixed-INT4 variants.
That makes it the shortest path to exercising the existing runtime without a
conversion pipeline. Its Indonesian extraction quality and memory use remain
unknown until measured on Lajur data and the POCO F6.

- [Upstream Qwen3 0.6B model card](https://huggingface.co/Qwen/Qwen3-0.6B)
- [LiteRT Qwen3 0.6B artifacts](https://huggingface.co/litert-community/Qwen3-0.6B)

### 2. Gemma 3 1B IT dynamic INT4

Use this as the larger quality baseline if Qwen3 0.6B is not useful enough.
Google describes Gemma 3 as supporting more than 140 languages. The published
LiteRT dynamic-INT4 reference is materially larger than Qwen3 0.6B, so it must
justify its storage and memory cost with better Lajur metrics. Gemma uses the
Gemma terms rather than Apache-2.0; distribution must be reviewed before model
delivery is implemented.

- [Upstream Gemma 3 1B IT model card](https://huggingface.co/google/gemma-3-1b-it)
- [LiteRT Gemma 3 1B IT artifacts](https://huggingface.co/litert-community/Gemma3-1B-IT)

### 3. Fine-tuned FunctionGemma 270M

Treat this as the smallest long-term specialization candidate, not as a
drop-in zero-shot extractor. Google's model card positions FunctionGemma as a
base for task-specific function-calling fine-tuning, and its published mobile
result is for a different Mobile Actions task. Lajur would need its own
sanitized training/evaluation split, fine-tuning, LiteRT conversion, and the
same device benchmark before it can compete with the general models.

- [FunctionGemma model card](https://ai.google.dev/gemma/docs/functiongemma/model_card)
- [FunctionGemma fine-tuning guide](https://ai.google.dev/gemma/docs/functiongemma/finetuning-with-functiongemma)

## Interpretation rules

- Do not compare vendor latency/RSS numbers as if they were POCO F6 results;
  device, backend, context length, quantization and prompt differ.
- Do not select on aggregate field accuracy alone. Track structured-output
  failures, required-field exactness, category coverage, and category false
  positives separately. Abstaining is preferable to inventing a category.
- The committed synthetic dataset verifies repeatability but cannot establish
  representative quality. Add sanitized receipts/payment screenshots with
  decoy totals, noisy OCR, ambiguous categories and missing fields privately.
- Pin the exact model hash, prompt/schema version, LiteRT-LM version and backend
  in every comparison. Community artifacts are inputs to verify, not trusted
  production releases.
- Start with CPU for the current reproducible baseline. Benchmark another
  backend only after packaging and device support are explicit.

## Decision gate

Choose the smallest artifact only after it meets an agreed usefulness threshold
on the held-out sanitized dataset, produces no unacceptable unsupported values,
and stays within acceptable cold/warm latency and peak memory on the POCO F6.
Until those thresholds and measurements exist, Phase 8 model selection remains
open.
