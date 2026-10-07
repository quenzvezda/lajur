# Test strategy

Android workbook-menu coverage includes bounded binary transfer/year validation,
multipart/error/auth contracts and remote/local safety-gate concurrency JVM tests.
Opt-in synthetic Compose tests check import pending guard, replacement confirmation
and export warning; an isolated in-memory Room test checks year-scoped cache
invalidation and failed/delete outbox counting. Real system document providers,
server round-trip and desktop Excel preservation still require the manual
`development/WORKBOOK_MENU_SMOKE.md` pass. Do not import into the owner's active
year as a test or treat a callback-only UI pass as full end-to-end acceptance.

The project favors high-value deterministic tests over a large flaky UI automation suite.

Optional-payment regressions cover nullable/omitted/blank API input normalization,
unknown nonblank rejection, idempotent retry, same-row payment edits with unchanged
totals, legacy import/reimport identity, blank C-cell styling and unrelated package
preservation. Android JVM checks cover editor/scan validation, serialization,
outbox compaction and review filtering; opt-in UI/Room checks cover empty save,
clear payment, search-independent reminder and persistence across database reopen.
Full offline-to-Excel owner acceptance remains separate.

## 1. Test pyramid

### Backend unit tests

Cover:

- validation;
- month/table mapping;
- amount/date rules;
- idempotency decisions;
- revision rules;
- auth/session policy;
- backup-retention logic;
- year mapping/generation logic.

### Backend integration tests

Use ASP.NET `WebApplicationFactory` and temporary storage.

Cover:

- login/refresh/revocation;
- cross-user isolation;
- create/update/delete;
- duplicate create;
- import rejection/promotion;
- export;
- current-month summary.

SQLite does not require a container merely for the sake of Testcontainers.

### Workbook preservation tests

Use sanitized `.xlsm` fixture copies.

For mutations:

- package remains readable;
- VBA part exists;
- ordinary transaction writes keep VBA bytes unchanged;
- monthly table ref expands/shrinks correctly;
- formulas/defined names still exist;
- right-side monthly content remains untouched;
- system sheets are valid/hidden;
- no unexpected table/sheet deletion.
- bounded monthly DATE/AD1-relative helper recognition in import/generation;
- mixed explicit/shared series, split shared groups, short months/leap years;
- unchanged relative expressions/shared attributes, cleared generated date caches;
- malformed/missing helpers and conflicting day/month/year rejection with rollback;
- byte-identical unrelated package parts on generation/import, including shared strings.

These are critical tests.

### Android JVM/domain tests

Cover:

- form validation;
- outbox compaction;
- sync state machine;
- conflict handling;
- settings logic;
- app-lock timeout decisions;
- extraction-result validation.
- evidence-first OCR dates/totals, row reconstruction, ambiguity and model-failure fallback.
- OCR-only review without runtime loads, explicit AI requests, cancellation,
  stale-result rejection and revalidation against updated Master data.
- integer-IDR editing/grouping/cursor offsets, calendar UTC conversion,
  Master-constrained editor validation and case-insensitive category search.

### Android instrumented tests

Use selectively for:

- Room transaction behavior;
- URI/image integration if JVM tests cannot represent it;
- biometric abstraction boundaries with fakes around platform UI.

Do not make broad Compose UI E2E a required v1 gate.

Focused opt-in Compose smoke tests cover selector width, searchable category
selection, date-picker behavior, integer-IDR display, disabled invalid save and
draft-first review. These use an isolated empty host and synthetic values, not
the user's account or ledger. Build/update-install both APKs and invoke the
specific instrumentation class directly; do not run the uninstalling Gradle
connected-test workflow on a daily-use install. Synthetic screenshots remain
in ignored private storage. See the Smart Scan UX smoke runbook.

## 2. Manual smoke tests before release

Minimal device checklist:

- fresh login;
- biometric unlock;
- short app switch does not relock;
- >5 minute timeout relocks;
- manual online save;
- manual offline save then reconnect/sync;
- image scan;
- share-intent scan;
- edit synchronized transaction;
- delete synchronized transaction;
- export workbook;
- open exported workbook in desktop Excel;
- verify macro/button behavior expected by template;
- import a copied workbook.

## 3. OCR/LLM evaluation

Separate from ordinary CI.

Run when:

- OCR engine changes;
- model changes;
- model quantization changes;
- prompt/schema changes materially;
- fine-tune dataset/model changes.

Record:

- field accuracies;
- invalid output rate;
- cold load;
- end-to-end latency;
- peak memory;
- test device/model/runtime versions.

Target-device benchmark must include a POCO F6-class phone.

Phase 8C provides an opt-in Android instrumentation benchmark and a committed
synthetic OCR-only smoke dataset under `tests/smart-scan-eval/`. The harness
records the exact model hash, runtime/backend/device, structured-output and
field metrics, category abstention behavior, latency, and sampled process PSS.
It is not an ordinary CI gate. Candidate selection additionally requires a
private sanitized real-world dataset and repeated physical-device runs; see
`docs/development/PHASE8_LOCAL_MODEL.md`.

The opt-in `ReceiptImageExtractionInstrumentedTest` replays the supplied synthetic
minimarket image through image preprocessing, actual ML Kit OCR, local-model
inference, evidence rules and validation on the physical device. It asserts
date/amount/Bank and writes a private diagnostic report without saving a
transaction. Manual debug scans can explicitly save the same JSON report for
ADB inspection; report contents never enter ordinary logcat. This is extraction
acceptance, not evidence that the editor/outbox/API/workbook save path passed.

## 3a. Container smoke test

`services/api/tests/container-smoke.sh <image> <fixture>` runs in the CI `container` job. It
starts the image like `docker-compose.yml` (read-only root, dropped capabilities, bind mounts),
creates an account, logs in, imports the sanitized fixture, reads a summary and loads the web
login page. A second container without the `staging` mount must fail readiness, guarding the
2026-10-07 import regression. It is deterministic and needs no emulator or browser.

## 4. CI philosophy

CI should fail for deterministic regressions.

Do not fail every PR because:

- emulator animation was slow;
- screen rendering missed an arbitrary timeout;
- on-device LLM benchmark varied under thermal throttling.

Performance/evaluation regressions are reviewed through dedicated benchmark runs.

## 5. Fixture privacy

Never commit the owner's real workbook/receipts.

Create sanitized fixtures preserving:

- workbook package complexity;
- macros;
- representative formulas;
- monthly tables;
- Master names;
- right-side content.

Keep real private evaluation data outside the public repository.
