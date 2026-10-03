# Updated workbook audit — 2026-10-03

## Conclusion

The owner's new private source retains the main workbook layout, but it is **not
an immediately deployable replacement for the production template or an
import-ready workbook**. Changes include Master configuration, date-helper
formula variants and VBA package bytes, not just button styling.

Recommended sequence: finish remaining backend compatibility, resolve category
decisions, then generate a separate clean
template with targeted normalization. Do not replace a live account or copy whole
monthly sheets over one another.

## Scope and evidence

Input: `private/source/Keuangan_Pribadi_2026-new.xlsm`, the owner-provided copy of
the updated daily-use/OneDrive workbook. Compared against the old private source,
normalized-with-pivots working copy, clean private template and synthetic fixture.
This is an audit of that copy, not of the live OneDrive path.

Two complementary inspection paths were used:

- Python 3/openpyxl 3.1.5 in memory plus ZIP/XML inspection. Shared formulas were
  expanded before comparison. No workbook was saved with openpyxl.
- Microsoft Excel 16.0 COM through the Excel interop skill's owned-session helper.
  Disposable copies were opened read-only, VBA/events disabled, external links
  and connections checked absent, and `CalculateFullRebuild` requested.

The new source and normalized comparison reached `xlDone`. The old source and
clean template reported `xlPending` at the sampling checkpoint, so their final
recalculation is **not** counted as proven. Native formula-error locations of the
new source match the successfully recalculated normalized copy. Before/after
SHA-256 checks confirm every audited source/template/fixture remained unchanged.

Raw snapshots, formula comparisons, native reports, scratch probes and owner
checklist stay under ignored `private/workbook-review/`. This document excludes
ledger amounts, descriptions, category/payment lists and private configuration
values. No active API workbook was imported, replaced or modified.

## Main contract and native features

| Item | Finding |
| --- | --- |
| Sheets | Same 14 visible sheet names/order as the existing contract |
| Monthly tables | Same 12 names and six headers, still starting at A5 in A:F |
| PivotTables | One per month, 12 total; native sources point to matching monthly tables |
| Charts | 27 retained, including two on every monthly sheet |
| Buttons | One per month, caption `Data Baru`, macro binding `TambahBarisBaru_ActiveSheet` |
| Button typography | Calibri 18pt, bold across all months |
| Hidden helper areas | AD:AO remain part of the monthly analytical layout |
| External links/connections | None found |
| VBA | Present, but `vbaProject.bin` differs from all previous baselines |

Okt–Des no longer contain the old duplicate/default `Button 3`. Their button
positions and widths still vary slightly from Feb, so this is not full geometry
normalization. Button binding inspection does not prove macro execution. VBA
source equivalence was not established; binary difference alone is not proof of
a functional macro change. No macro was run.

## Formula and configuration changes

Expanded formula comparison found **153 changed/added formula cells** relative
to the clean template (also the old source and normalized working copy):

- `Rekap!P23:P45`: budget lookup bounds extend from Master row 81 to row 86.
- Every monthly `M31`: budget sum bounds extend to Master row 86.
- Every monthly `AJ23`: helper reference changes from `Master!A28` to `A33`.
  The referenced label is not the same between the two snapshots. Confirm the
  intended mapping rather than assuming this is only an equivalent row shift.
- `Okt`/`Nov`/`Des!AG2:AG32`: direct `DATE(2026,month,AFrow)` changes to
  `$AD$1+AFrow-1`; AD1 still holds the month's first-day DATE formula.
- Okt–Des `S11`, `T11`, `S18`, `T18`: formula-driven month labels replace
  constants using AD1/EDATE/CHOOSE.
- `Des!D1`: now a previous-month balance formula referencing `Nov!D4`, rather
  than a blank/manual input.

`ListKategori` now counts through Master row 86. The populated category list has
grown from 31 to 36 entries: six names added and one previous name removed.
Transaction-type and payment-name definitions retain their prior ranges.
The current populated category rows end at row 40, so the backend's fixed read
bound of row 81 does not omit a current entry. That fixed bound is nevertheless
a future compatibility limit if Master is extended further.

## Findings needing action

### 1. Intentional payment blanks and the approved application contract

23 populated financial rows have no payment method: 18 in Sep and five in Okt.
Owner clarification after the audit: payment blanks are used for expenses
temporarily paid by someone else, which the owner reimburses later in a lump sum.
This is meaningful workbook content, not automatically an incomplete financial
record. The original recommendation to fill all such cells is withdrawn.
Exact cell references are in the ignored owner checklist. A separate Jan row has
a category absent from the current Master list; this is a configuration/history
consistency issue, not a demonstrated import rejection on its own.

At audit time, before the optional-payment change, an isolated read-only probe
using the actual `OpenXmlWorkbookReader` confirmed:

- Master is readable;
- Jan–Aug and empty Nov/Des tables are readable;
- Sep first fails at row 9, missing `Pembayaran`;
- Okt first fails at row 8, missing `Pembayaran`;
- workbook-year detection then stopped at that blank-payment Sep row.

Do not force unpaid expenses to Cash/Bank, manufacture a payment method, or
silently delete historical rows. The import failure is a mismatch between the
owner's workflow and the former nonblank-payment contract. The owner subsequently
confirmed that payment is filled on the original row; no separate repayment or
settlement status is wanted. Only payment is optional; date/type/category/positive
amount remain required. The application now represents blank payment as an empty
string and allows it through editor/API/workbook read/write/import. A non-blocking
Home reminder helps locate these transactions. Filling payment keeps identity,
increments revision, and leaves expense/count totals unchanged. Do not equate
payment completeness with Android synchronization outbox state. Sanitized fixture
tests cover these changes; the private source has not been reimported or promoted.
The separate historical category decision remains open. The probe did not call
an import API or initialize metadata.

### 2. Year validation/generation needs a compatible helper variant

`OpenXmlWorkbookReader.DetectWorkbookYear` requires a recognizable direct DATE
formula at both AD1 and AG2. `OpenXmlWorkbookEditor.PatchDateFormula` likewise
expects DATE at AG2 during year generation. The new AD1-relative AG2 formula in
Okt–Des fails those implementation expectations even though it can be valid
Excel logic. This second blocker is established from exact formula/code
inspection; the original public year-read probe encountered payment blanks first.
Optional-payment support does not resolve this independent formula variant.

Recommended change: explicitly recognize the bounded, known AD1-relative helper
pattern, validate AD1's year/month, and preserve the relative series when
generating a new year. Retain old-template support and reject unrelated formulas.
Add sanitized import/generation/preservation regressions, including the final
day of short months and leap years. Do not weaken validation to arbitrary
formula evaluation or silently rewrite the owner's business logic.

### 3. Annual category breakdown is not complete

The fixed category labels in `Rekap!A23:A45` do not cover seven current expense
categories (one was already missing in the previous template, six are new).
The annual category matrix formulas were not expanded to match those additions.
Therefore the main ledger-derived totals may remain correct while this category
breakdown is incomplete. Rankings fed from that breakdown also need coverage
review. Audit does not certify every chart/ranking value as correct.

Record the intended category mapping/coverage and update only the affected
annual analytical ranges, formulas and chart sources on a new copy. This is not
a reason to widen monthly transaction tables.

### 4. Layout/conditional-format normalization has drifted

- Column E is the expected 22.140625 width, including Okt–Des.
- Checked analytical rows 20/30/40 have 24.75 height in Feb and Okt–Des.
- Nov/Des column H is 11.28515625 rather than Feb/template's 16.7109375.
- Okt–Des column L is 13.7109375 rather than the template's 9.
- Transaction-type conditional formatting again includes fragmented, very large
  ranges reaching XFD/row 1048576, unlike the clean template's `B6:B505` rules.

XML differences alone do not prove a visible validation/color defect. Effective
validation, appended-row color and visible layout still need targeted native
smoke tests on a future repaired copy. Restore the reviewed bounded formatting
policy rather than importing large fragmented rules indiscriminately.

### 5. Formula-error baseline and limits

The saved new file contains 103 cached `#N/A` cells. Native inspection finds 103
formula-error cells at the same addresses as the recalculated normalized copy,
in the hidden chart-helper area. The helper formulas deliberately use NA for
out-of-period chart points. These are not 103 newly broken finance formulas.
No new formula-error location was observed in that comparison.

This is not a full business-logic proof: existing errors, incomplete category
coverage, stale pivot caches and logical mistakes can exist without new error
cells. Pivot source/binding presence was inspected; a separate explicit refresh,
macro-button execution and visual layout acceptance were not performed.

## Template promotion policy

Keep the current production template unchanged until a new candidate passes
compatibility and preservation tests. Use the new workbook as a reference for
approved improvements, not as an automatic replacement.

A future clean candidate must remove ledger history, private notes/cache data
and manual inputs while retaining approved Master configuration and native
features. Revisit the old blanket B1/D1 clearing rule: the new Des D1 is a
formula, so clearing it indiscriminately would discard a reviewed roll-forward.
The generator's clearing behavior must also be reviewed before adopting that
formula. No cleaning/promotion was performed in this audit.

The committed fixture must remain synthetic and distinct from the private
production candidate. Regenerate/update it only after the new contract variants
and intended analytics are agreed.
