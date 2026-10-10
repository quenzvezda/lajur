# Web workbook settings smoke test

Use a disposable account and the sanitized XLSM fixture. Never modify a real
ledger during acceptance. Temporary browser/Excel artifacts belong under
ignored `private/output/`.

1. In January, open Atur proyeksi. Enter 22 workdays and Rp35.000/day; confirm the
   live Rp770.000 estimate. Change salary/freelance estimates and payout days.
   Tinjau perubahan shows old/new values; save refreshes the projection without
   adding a transaction or changing recorded income. Open again: values persist.
2. In February, copy the preceding month. Values remain a draft; preview/save is
   required. January remains unchanged. Unsupported sources show a read-only
   reason, and unknown formulas cannot be overwritten.
3. Open Edit budget from Master or a monthly category panel. Change several
   targets; compare the batch and total before save. Targets apply to Jan–Dec in
   the selected annual book. Clear a target and set another to zero: missing and
   Rp0 remain different. Report/category budget/pace displays refresh after save.
4. Close/reopen an unsaved editor: draft remains in this tab. Change settings in
   another tab before saving: show server and draft, never overwrite silently.
   Retaining draft requires another preview against the current settings version.
5. Lose a response after server acceptance: inspect the operation receipt; do not
   replay the change or append another audit. Lose a request before acceptance:
   receipt 404 enables explicit retry with the same UUID/payload/version.
6. Invalid days, decimals, negatives, duplicate targets, wrong category/source IDs
   and multiplication/aggregate overflow leave the active file unchanged. One
   invalid field aborts the whole batch. Empty inputs and zero are accepted.
7. Keyboard labels/focus, Escape/close, preview return and recovery actions work.
   Check desktop and 390 px light/dark layouts, scrollable content and visible
   actions. Money groups while typing and aligns right. Refresh does not discard
   transaction-entry drafts; leaving with unsaved settings warns.
8. Verify backups, cache invalidation, browser auth/CSRF, tenant isolation and
   continued Android session access. Export the synthetic changed book; open and
   recalculate it with native Excel. Check meal estimate, salary/freelance,
   Master targets, formulas, VBA and package preservation against the fixture.

Category/payment add/edit/delete and lifecycle guards are the next slice; this
editor only updates recognized budget and projection inputs.

## Recorded validation — 2026-10-10

All 150 backend checks passed (25 unit, 46 integration, 79 workbook); final
focused rechecks also passed. Isolated Chromium exercised the above save/copy,
batch/null-zero, preview, draft, conflict, receipt/retry and responsive flows
without page errors. Source and served Docker assets match; readiness is 200
and the container is healthy. Native Excel opened, recalculated and refreshed
the synthetic export, preserved the meal formula and matched the expected
salary/freelance/budget inputs without adding errors to the fixture baseline.
