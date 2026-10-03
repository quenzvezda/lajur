# ADR 0007 — Evidence-first Smart Scan drafts

**Status:** Accepted (2026-10-03). Updates the pipeline decision in ADR 0003;
its local-only privacy constraint remains in force.

The POCO F6 dummy-receipt test read the date successfully in OCR but lost it in
small-model extraction, and flattened OCR separated TOTAL from its right-hand
amount. Smart Scan now retains line positions, reconstructs reading order, and
extracts explicit numeric dates and labelled totals before merging local-model
suggestions. QRIS maps to the workbook's Bank option. The local model still
helps with semantic fields; missing/failed model output cannot erase supported
OCR fields. Multiple distinct dates/totals stay missing with alternatives;
category is never manufactured by these rules.

After a captured user run returned an unfinished JSON string and copied Master
lists into its response, live model suggestions are narrowed to transaction
type, payment, category, merchant and description. Dates and integer-IDR amounts
are now evidence-only: if OCR rules cannot establish them, the user fills them.
Supported OCR type/payment also win without consulting a model guess. The full
versioned draft schema remains unchanged; the compact model transport uses a
constrained six-key JSON object with nullable strings. This trades numeric-format
coverage for keeping unsupported model guesses out of financial fields.

Some repeated physical tests stalled while the device was Dozing with its
test-only partial wake lock disabled. A fresh-engine experiment also stalled,
so neither engine reuse nor enum grammar is established as the cause. The
experiment was reverted: nearby scans reuse the engine with a 60-second idle
unload. Generation failure/cancellation performs non-cancellable cleanup.
Repeated target-device evaluation still requires an awake device.

The trade-off is limited format coverage and explicit rule maintenance instead
of requiring a model to rediscover clear receipt evidence. Every field retains
its source and evidence, every draft passes the same Master validator, and a
human still reviews before saving. Confidence labels are coarse review hints,
not calibrated probabilities. Debug reports require an explicit save action,
remain in private no-backup storage, and never send receipt content to logcat or
the backend.

The owner approved an opt-in model UX on 2026-10-03. Selecting an image now
produces a validated OCR/rules draft without loading or running a model.
**Tambahkan saran AI** explicitly starts optional local semantic suggestions.
This avoids making ordinary entry wait for the multi-second model path.
Review can proceed while AI is running; entering the editor cancels that request,
and cancelled/replaced scans cannot publish late results or overwrite user edits.
Cancellation invalidates results immediately but does not promise immediate
interruption of synchronous native inference. Source labels and expandable
evidence replace prominent uncalibrated confidence labels; raw OCR and timing
details are collapsed by default. Model selection and quality acceptance remain
open.
