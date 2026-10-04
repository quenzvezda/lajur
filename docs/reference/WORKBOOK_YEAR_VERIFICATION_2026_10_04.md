# V4 generated-year verification — 2026-10-04

## Result

After the approved backend calculation-marker fix, **fresh V4 exports for 2027
and 2028 reach `xlDone` without diagnostic flag edits**. Calendar, native
financial calculations and saved dummy-file caches all pass. The earlier
`xlPending` observation below records the pre-fix diagnosis; it is not the
current generated-file result. Never promote a diagnostic or populated test file
as the production template.

| Check | 2027 | 2028 |
|---|---|---|
| February length | 28 days | 29 days |
| All monthly daily-helper slots | 372 valid | 372 valid |
| Dummy transactions | 360 | 361, including February 29 |
| Dummy annual expenses | IDR 5,446,200 | IDR 5,453,977 |
| Expense categories reconciled | 29 | 29 |
| Native-saved formula caches checked | 4,622 | 4,622 |
| Unexpected cached formula errors | 0 | 0 |
| Original pre-fix export calculation state | `xlPending` | `xlPending` |
| Flag-only control calculation state | `xlDone` | `xlDone` |
| Fresh post-fix backend export calculation state | `xlDone` | `xlDone` |

These amounts are deterministic synthetic test values, not the owner's finances.

## Scope and method

- Source: the retained clean private V4 candidate, not the populated owner test
  copy. The actual backend store previously generated/exported both years into
  isolated private storage, preserving the V4 VBA part.
- Open copies using the Excel Interop skill's owned-session helper, with VBA and
  events disabled. Native environment: Excel version 16.0, build 20430.
- Verify every `AD1`, month-length `AD4` and all `AG2:AG32` cells. Slots past a
  short month's end intentionally overflow into the following month, matching the
  existing calendar contract; do not truncate or rewrite them.
- Verify empty monthly income/expense/closing balances and retained
  `Des!D1 = Nov!D4`.
- On new dummy copies, write expenses for every Master expense category plus an
  income on the last actual day of each month. Add a separate IDR 7,777 expense on
  February 29, 2028. Expense payment cells remain blank.
- Independently verify monthly income/expense/closing balances, December carry,
  expense-only pivot sums, each of the 29 annual category totals and Rekap's
  ledger/category-matrix totals. Pivot pages are selected directly for this
  formula test; automatic VBA filtering is covered by the earlier V4 lifecycle
  tests, not falsely claimed as newly executed here.
- Save only owned dummy files through Excel. Read their caches without rewriting
  them, verifying all 372 daily dates, all 4,622 formulas and matching totals.
  Twelve valid empty-string `B2` caches are distinguished from absent caches.
- Scan all worksheets for native and cached errors. Hidden chart helpers return
  intentional `#N/A` (379 in 2027, 378 in 2028) via existing `NA()` formulas;
  hidden formats can suppress their displayed text. No unexpected error remains.
- Run existing `OpenXmlWorkbookDateHelperTests`: **28 passed, 0 failed**.

Excel COM was isolated per workbook/year in separate PowerShell processes after
multi-session runs encountered transient/reused COM-reference failures. Failed
test-harness runs are retained privately but are not the accepted evidence.

## Calculation-marker finding

During the initial verification, `OpenXmlWorkbookEditor.RequestFullCalculation` set both
`FullCalculationOnLoad=true` and `ForceFullCalculation=true` for import/generation.
The native unmodified files remain `xlPending` after full rebuild, and even after
temporarily setting the Force property false within the same session. Calendar
and financial values nevertheless calculate correctly, including saved caches.

A separate control changes **only** `forceFullCalc` from true to false before
opening a fresh Excel process. `fullCalcOnLoad=true`, automatic mode, all formulas
and all other package parts are retained; VBA and every other ZIP entry remain
byte-identical. Both years then reach `xlDone` and pass the calendar/error scan.
This controlled comparison supports the marker diagnosis, not a formula repair.
The 2028 flag-only control also passes the same synthetic financial and saved-cache
checks with `xlDone` and exactly the same totals as the unmodified export.

Microsoft documents that forced mode ignores dependencies and marks every sheet
for full calculation on each trigger, with the setting retained for the session:
[Workbook.ForceFullCalculation](https://learn.microsoft.com/en-us/office/vba/api/excel.workbook.forcefullcalculation).

The initial proposed follow-up, **not implemented by the verification-only request**, was to use a
one-time full-calculation-on-load request without persistent forced mode, then
regenerate via the actual backend and repeat the native checks. Include ordinary
transaction-write and import regression tests before changing the shared policy.
Business formulas, category mappings and Master budgets must not change. The
subsequent explicitly approved implementation is recorded below.

## Approved backend fix and fresh-generation verification

`RequestFullCalculation` now sets automatic mode, `fullCalcOnLoad=true` and
`forceFullCalc=false`. The policy is applied centrally by successful editor saves,
including import, year generation and ordinary create/update/delete. Idempotent
no-op retries remain byte-preserving; existing account files are not proactively
rewritten. Server-side formula evaluation and workbook business formulas are
unchanged.

Five new sanitized regression cases first fail against the old policy and then
pass after the fix. The full backend solution passes **82 tests**: 2 unit, 22
integration and 58 workbook tests. New cases cover generation for both years,
import with both legacy force states, CRUD from legacy/manual mode, VBA/source
preservation, optional payments and byte-identical create retries.

Fresh exports generated through the actual backend store use isolated private
storage at `post-fix-backend-probe/`. Both reach native `xlDone` on their first
as-generated full rebuild, with persistent force mode false and no diagnostic
reset. Macros/events are disabled and each year uses its own owned Excel process.
The same calendar, monthly/annual/category/pivot, optional-payment, December
carry and February 29 checks pass; all 4,622 saved formula caches per year pass
with zero missing caches or unexpected errors.

Accepted post-fix evidence under the same private V4 review directory:

- `native-years-92ac61fc20984a3b8db43f15b6cff9c2/`: fresh 2027 native/preservation report.
- `native-years-cca70545083448479a45face7c95a28a/`: fresh 2028 native/preservation report.
- `post-fix-finances-2027/` and `post-fix-finances-2028/`: native synthetic reports,
  saved dummy copies and cache-verification reports.
- `pre-promotion-package-verification.json`: clean V4 package/source/style recheck.

The local Docker API image was rebuilt with the fix. The existing API container
was already stopped and has not been started; use Compose recreation when later
starting it so the new image, rather than the old stopped container image, runs.

## Private evidence and preservation

Evidence is under `private/workbook-review/candidate-20261004-v4/`:

- `verified-original-2027/` and `verified-original-2028/`: native synthetic reports,
  saved dummy XLSM copies and `cache-verification-<year>.json`.
- `calculation-controls-preserved-xml/control-manifest.json`: exact flag-only
  comparison with all other parts byte-identical. An earlier generic XML
  serialization control was rejected because it dropped namespace declarations
  used by `mc:Ignorable`; it is not accepted evidence.
- `native-years-b2b7e519b8654b10a8ba795645704509/` (original 2027) and
  `native-years-6f01712ed511464083f0cefc99ea54f4/` (original 2028): complete native
  calendar/error and preservation reports, with the strict `xlDone` gate failing
  as explicitly recorded.
- `native-years-e7c56f9ceb5d49c5b7b8f6c1d38ef408/` (control 2027) and
  `native-years-13db66683d244be2a85542ba59857035/` (control 2028): complete native
  calendar/error and preservation reports, with `xlDone`.
- `verified-control-2028/`: matching native synthetic totals and saved-cache
  checks on the flag-only control. Other failed/partial `native-years-*` runs are
  not accepted results.

Before the separately approved promotion, the candidate, owner V4 test file,
daily source, production template and original backend exports retained their
pre-test SHA-256 hashes. No active API account,
deployed storage, phone, Trust Center setting or workbook business formula was
changed. The owner elected to keep VBA-project editing enabled. No VBA ran in
these year-verification sessions. Private XLSM/configuration/scripts/results
remain ignored by Git; only privacy-safe documentation and sanitized backend
code/tests are tracked.

## Approved clean V4 promotion

After all post-fix gates passed, the retained clean V4 was installed byte-for-byte
at `private/output/Keuangan_Pribadi_Template_2026.xlsm` and the verified Docker
mount `.local/lajur/templates/finance-template.xlsm`. Separate old copies and
atomic-replacement backups are retained under
`.local/lajur/backups/templates/v4-promotion-20261004T154031Z-85362530/` with a
successful promotion manifest. Both installed files match the clean candidate's
SHA-256; all existing account/source XLSMs and owner test copies are unchanged.

An isolated actual-store probe from the **installed runtime template** imports
2026 empty and generates/exports empty 2027/2028 with the corrected calculation
markers, complete Master and byte-identical V4 VBA. It does not write deployed
account storage. The API container stays stopped; no phone access or service
restart occurred. See [the template history](WORKBOOK_TEMPLATE_NORMALIZATION.md#v4-promotion--2026-10-04)
for active paths and template-only rollback guidance.

## Remaining gates

1. Backend calculation-marker adjustment and fresh native `xlDone` gate: passed.
2. Clean V4 template promotion: explicitly approved and completed with verified
   backups; existing account workbooks were not replaced.
3. Exercise the Android Data/Workbook flow on a dummy account before deployment.
