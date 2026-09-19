# Test strategy

The project favors high-value deterministic tests over a large flaky UI automation suite.

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

### Android instrumented tests

Use selectively for:

- Room transaction behavior;
- URI/image integration if JVM tests cannot represent it;
- biometric abstraction boundaries with fakes around platform UI.

Do not make broad Compose UI E2E a required v1 gate.

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
