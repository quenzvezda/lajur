# Open questions / intentionally deferred decisions

These items are not blockers for starting the MVP. They should be resolved at the phase where evidence exists.

## Model/OCR

- Which local OCR engine wins on the private representative dataset?
- Which local LLM is smallest while still useful: FunctionGemma 270M fine-tuned, Qwen3 0.6B-class, Gemma 3 1B-class, or another local model?
- Which runtime/delegate is best on POCO F6?
- Bundle model in app or distribute from own VPS with integrity verification?

## UX

- Exact visual styling/component composition after the first Compose prototype.
- Whether bottom navigation ever becomes justified.
- Whether synchronized delete needs a server-side restore endpoint for Undo.

## Operations

- External backup target.
- Container registry if not GHCR.
- Exact production subdomain.
- Exact Android production application ID/signing setup.

## Workbook

- Whether later template revisions should migrate old annual workbooks automatically or only on explicit operator action.
- Whether an explicit workbook compatibility manifest/custom property should be added in addition to hidden-sheet schema version.

## Product growth

Out of v1 unless explicitly approved:

- multiple active devices per account;
- shared workbooks;
- Android category/master editing;
- advanced analytics;
- bank/e-wallet integrations.
