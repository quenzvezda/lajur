# Monthly workbook template normalization strategy

This document records the strategy for making the 12 monthly sheets structurally consistent without overwriting private transactions or breaking workbook-native analytics.

## Recommendation

Use a **canonical monthly blueprint plus targeted repair**.

Do not copy one complete live monthly sheet over all other sheets. A whole-sheet replacement can silently break or duplicate:

- table names and structured references;
- month-specific and previous-month formulas;
- chart series;
- PivotTable and pivot-cache relationships;
- form controls and VML drawings;
- comments, validations, and conditional formatting;
- existing transactions.

The canonical blueprint should describe the invariant layout and objects. Month-specific values and formulas are parameters applied on top of that blueprint.

## Golden reference

Use `Feb` as the primary golden reference for a normal month because it contains:

- the complete A:F transaction table and validations;
- both charts;
- the category PivotTable;
- cash projection and spending-pace panels;
- previous-month comparison formulas;
- hidden AD:AO helpers;
- one correctly captioned `Data Baru` button;
- the expected row heights, column widths, conditional formatting, and page settings.

`Jan` remains a required special-case reference because it intentionally replaces previous-month comparison formulas with a first-month explanation.

The golden reference is not copied byte-for-byte. It provides expected layout/format/control definitions that are patched into each target sheet while preserving that sheet's identity and data.

## Invariants across all monthly sheets

The normalization manifest should define:

- column widths for A:V and hidden AD:AO;
- default and explicit row heights for the analytical area;
- hidden-column state;
- merged ranges, excluding documented January-only differences;
- table style and six-column header contract;
- list validation behavior for B:D through the supported entry range;
- conditional-formatting coverage for the transaction and analytical areas;
- two chart anchors and sizes;
- page size, orientation, and margins;
- exactly one `Data Baru` button bound to `TambahBarisBaru_ActiveSheet`;
- one category PivotTable where the category-summary design requires it;
- comments/notes and other package relationships preserved as found unless intentionally migrated.

## Month parameters

The following must vary by month and must never be copied unchanged from `Feb`:

- worksheet name;
- table name (`JanTable` through `DesTable`);
- month number in hidden date formulas;
- current monthly table references;
- previous monthly table references;
- January's first-month comparison behavior;
- chart-series references to month-local helpers;
- PivotTable/cache source references when applicable;
- current transaction rows and table end row.

## Observed inconsistencies

### Okt–Des

- column E is width 16 instead of the established 22.140625;
- explicit widths for H and I are absent;
- rows 20 onward use height 15 on Nov/Des, instead of the established 24.75 analytical layout height;
- each sheet contains two overlapping buttons: a correctly configured `Data Baru` button and an extra default-caption `Button 3`; both call the same macro;
- no PivotTable relationship exists, while Jan–Sep each have one;
- the broad baseline conditional-formatting block is absent;
- page setup is absent and margins differ from Jan–Sep;
- merged-range count is 20 instead of 21.

These sheets should be treated as incomplete template variants, not merely cosmetically different sheets.

### Additional drift

- `Mar` column H uses width 14 instead of the common 16.7109375;
- `Sep` column L uses width 13.7109375 instead of 9;
- `Sep` uses split validation records because its table has evolved through edits; effective validation should be checked, but XML record shape alone is not a defect;
- `Sep` row 60 is height 15, while earlier populated sheets use 24.75;
- some populated sheets contain transaction-specific conditional-formatting ranges. Normalization must not discard legitimate row-level formatting created during real use.

## Safe repair sequence

Perform all repair work on a new disposable copy, never on the only private workbook.

1. Record a package-part hash manifest and workbook structure snapshot.
2. Extract current A:F transactions and table metadata for the target month.
3. Remove only the known duplicate/default button object on Okt–Des.
4. Apply canonical widths, row heights, page settings, and baseline conditional formatting.
5. Restore or rebuild the category PivotTable and its relationships only when the target table contains enough valid data to build a stable cache. Do not seed the financial table with dummy transactions merely to force a PivotTable into an empty future month.
6. Preserve or reinsert the target month's transaction rows under its original table name.
7. Patch month-local formulas, chart series, helper dates, and previous-month references.
8. Validate tables, formulas, defined names, controls, drawings, comments, PivotTables, and ZIP relationships.
9. Compare all unrelated package parts with the pre-repair manifest.
10. Open the repaired copy in desktop Excel, enable macros, refresh/recalculate, and manually smoke-test `Data Baru`, charts, PivotTables, and visible layout.

## Applied normalization copy (2026-09-19)

A disposable normalized copy was generated at `private/output/Keuangan_Pribadi_2026_normalized.xlsm`; the private source remained byte-identical.

The applied repair:

- standardized monthly column widths, row heights, chart geometry, page setup, and baseline formats from `Feb`;
- reduced each monthly sheet to one `Data Baru` button with the original macro binding;
- repaired the observed `Mar`, `Sep`, and `Okt`–`Des` layout drift;
- preserved all existing Jan–Sep PivotTables.

An additional copy, `private/output/Keuangan_Pribadi_2026_normalized_with_pivots.xlsm`, adds valid PivotTables to Okt–Des. Each PivotTable starts at `H14`, leaves `H12` for the `Tipe` page filter, and uses its matching monthly table as the source. Because those tables do not yet contain a `Pengeluaran` item, the initial page filter is `(All)`. The cache refreshes when Excel opens the workbook; after a real `Pengeluaran` row exists, refresh the PivotTable and select `Pengeluaran`. No dummy ledger row is used.

The normalized workbooks retain private transactions and are repaired working copies, not clean reusable templates. A production template must be generated as a separate file by clearing explicitly approved transaction/input ranges while preserving formulas, validation, controls, charts, PivotTables, macros, master lists, and year-specific configuration.

Verification confirmed ZIP integrity, one correctly bound button per month, consistent dimensions/page settings, no temporary seed values, and a byte-identical `vbaProject.bin`.

## Clean production template (2026-09-19)

The private production template is generated separately at `private/output/Keuangan_Pribadi_Template_2026.xlsm`. It is derived from the latest user-reviewed `normalized_with_pivots` copy and does not overwrite that working copy.

The cleaning policy is deliberately narrow:

- each monthly transaction table is reduced to `A5:F6`, retaining its header plus one formatted empty input row;
- manual monthly balance inputs at `B1` and `D1` are cleared;
- legacy comments/notes are removed because the inspected copy included private notes;
- transaction history is removed from worksheet cells and PivotTable caches;
- Master categories, category kinds, budgets, transaction types, and payment methods are retained as user-owned configuration;
- formulas, defined names, data validation, charts, PivotTables, buttons, styles, hidden helpers, and VBA are retained;
- the user-reviewed removal of the right border on column F for Okt–Des is retained.

The empty table row intentionally causes each category PivotTable to display a `(blank)` member with the `Tipe` page filter at `(All)`. This is a valid empty-template state. After real rows are entered, refresh the PivotTable and select `Pengeluaran` when that item exists.

Verification covers desktop-Excel opening, one empty table/one button/one PivotTable per month, required header formulas, absent comments, empty pivot-cache values, absence of the original transaction descriptions in the Open XML package, ZIP integrity, and a byte-identical `vbaProject.bin`. The source copy remains byte-identical.

The initial clean-template generation preserved two legacy transaction-type conditional-formatting rules whose `Applies To` ranges had expanded into fragmented, near-sheet-wide ranges. These were replaced on every monthly sheet with two bounded expression rules on `B6:B505`: `Pengeluaran` uses the existing red style and `Pemasukan` uses the existing green style. The formulas use `TRIM($B6)` so accidental surrounding spaces do not suppress the cue. Verification covers both an existing input row and a row appended to the Excel table.

## Updated clean candidate (2026-10-04)

The separate private review candidate is
`private/output/Keuangan_Pribadi_Template_2026_candidate_v2_20261004.xlsm`, derived
from the owner's updated source. It has **not** replaced the production template
or any account workbook. Use V2: the first candidate failed package validation
because Excel left inverted legacy dropdown ranges after shrinking October's
table. That earlier draft is rejected, not approved for use.

Owner-approved scope and results:

- Retain the latest Master configuration, defined names, monthly business
  formulas, relative date helpers and formula-driven December balance carry.
- Clear monthly transactions, manual balance constants, private comments and
  historical pivot members; keep an empty `A5:F6` table on each month.
- Normalize reviewed widths/heights, chart/button geometry and button typography.
  Rebuild monthly master-list validation before shrinking the tables, then apply
  only `B6:B505`, `C6:C505` and `D6:D505`. Keep payment blanks allowed.
- Restore bounded red/green type cues while retaining analytical conditions.
- Include all 29 current Master expense categories in annual Rekap. Expand the
  category matrix, totals and Top 10 dependencies, retaining the original
  ranking tie-break formula. No monthly A:F table was widened.

Native Excel was used for editing, recalculation, pivot refresh and previews, not
openpyxl saving. Excel rewrote internal VBA project bytes on save; the original
opaque `vbaProject.bin` was restored after checking workbook/worksheet code-name
identities. Package verification and native reopening confirm the retained
project is byte-identical to the **new source**, not the older template.
No VBA was executed.

Verification on the final candidate covers ZIP integrity, unchanged Master and
monthly formulas/names, 12 empty tables, 12 pivots, 27 charts, 12 macro bindings,
valid bounded dropdown/CF ranges, absence of comments and historical pivot text,
and absence of 615 sampled original transaction descriptions from XML text.
The cached hidden chart-helper `#N/A` values are intentional and date-dependent;
no unexpected finance formula error was found. All 14 native sheet previews were
reviewed. Unsaved synthetic native tests reconcile every expense category, annual
ledger/category totals, Top 10, January pivot totals, existing/appended-row type
colors and December balance carry.

An isolated probe using the actual backend store reads the candidate, imports
it empty, and generates/exports 2027 and 2028 while retaining VBA. It does not
touch deployed storage. Targeted native calendar checks confirm February 28 in
2027, February 29 in 2028, October 31 and November 30, plus the retained December
formula. The application-wide calculation state remains `xlPending` on both
generated files, even after an explicit rebuild. These targeted values pass;
full generated-workbook desktop calculation is **not** certified. Manual
macro/visual/Refresh All acceptance remains a review gate; do not equate backend
generation success with complete native workbook acceptance.

Monthly fixed ranking helpers still follow the supplied source and do not cover
every current expense category. They were not silently expanded under the
owner's approval for **annual Rekap**. Review their coverage separately before
claiming every monthly analytic is complete. Master budgets remain personal
configuration: this private candidate is not a sanitized regression fixture.

### Interactive V2 review findings

The owner's Aug/Okt smoke test exposed a gap in the earlier tail-append tests.
Data Baru inserts at the top and copies formats from the next row. Native
reproduction confirms this moves the two type rules below the populated input
rows; anchoring the rule at the header does not resolve it. A mixed pivot also
includes income unless its page is explicitly filtered. V2 is therefore not
approved for promotion. Its output file now contains owner-entered test rows:
preserve that file rather than cleaning it again.

A clean pre-test backup is retained for a separately named revision. The planned
repair intentionally changes VBA source (unlike the earlier byte-preserving
normalization): repair bounded type cues after insertion and enforce expense-only
pivots after native refresh. Empty/income-only pivot behavior has been tested
with a no-match label filter, not fake financial rows. Full acceptance must test
the actual button, type edits, mixed data, Refresh All and save/reopen events.
The owner subsequently enabled VBA-project editing temporarily; the V3 migration
and native checks below are now complete. The owner may disable that editing
access again without changing their normal workbook macro-execution setting.

### V3 expense-pivot/type-cue repair

The clean private candidate is
`private/output/Keuangan_Pribadi_Template_2026_candidate_v3_20261004.xlsm`.
The separate `private/output/Uji_Template_V3_20261004.xlsm` was used for owner
smoke tests, preserving the clean candidate. V4 below supersedes V3 because its
font-only cues did not match the owner's intended font/fill appearance.
Both started transaction-free; the owner's populated V2 was not touched.

The deliberate VBA migration adds one repair call after Data Baru's original
format-copy/validation path, a support module and workbook events. Bounded type
rules are restored after insertion, type edits and sheet activation, and expand
to the actual table end if it grows beyond row 505. Analytical CF is retained.
Pivot refresh events and workbook initialization select the Pengeluaran page
when it exists. If an empty/income-only cache has no such item, a named no-match
category filter displays no financial rows/total and H11 says
`Belum ada Pengeluaran`; the page may still display `(All)` in this state.
That fallback is filter metadata, not a synthetic ledger entry. It is removed
when the expense page becomes available. Macros must be enabled for event-driven
repairs/automatic filtering; this does not make pivot refresh live on every edit.

Owned dummy native tests passed on all 12 sheets: the actual button macro,
manual head insertion, red/green colors, optional payments, empty/income-only and
mixed-data pivots, deletion of every expense, Refresh All, dynamic coverage and
actual save/reopen Workbook_Open behavior. The annual dummy totals reconcile.
Aug/Okt dummy print previews were rendered and inspected. Tests did not save
transactions into the clean candidate.

Read-only package/source verification retains Master, defined names, monthly
formulas, 12 empty tables, 12 pivots, 27 charts and 12 button bindings. Original
VBA sources are unchanged outside the reviewed migration; workbook/worksheet
code names remain stable. Historical cache strings/comments and 615 sampled
source descriptions are absent. An isolated actual-backend probe imports the
empty candidate and generates/exports 2027/2028 with its new VBA byte-identical.
The original source, tested V2 and production template remain unchanged. The
prior generated-year full-native-calculation caveat is not waived by this probe.

### V4 — exact owner-approved type colors and fills

The owner saved the desired appearance in Okt of their now-populated V3 test
copy. Read-only XML and macro-disabled native DisplayFormat establish the golden
styles: Pengeluaran has regular font `#9C0006` and solid fill `#FFC7CE`;
Pemasukan has regular font `#006100` and solid fill `#C6EFCE`. These saved rules,
not a guessed red/green font alone, are the appearance reference.

The new clean candidate is
`private/output/Keuangan_Pribadi_Template_2026_candidate_v4_20261004.xlsm`;
the dedicated owner test copy is `private/output/Uji_Template_V4_20261004.xlsm`.
V4 copies the retained clean V3 and updates only type-rule presentation in the
support VBA module. Its lifecycle repair applies the exact font and solid fill
to all monthly sheets after Data Baru, type edits, activation and reopening.
The owner's styled V3 is preserved byte-for-byte, rather than reset or cleaned.

All 12 native sheet tests now assert exact rendered font/fill/pattern/regular
style on new and older rows, manual head insertion, dynamic coverage and actual
Workbook_Open. Expense-only pivots, payment blanks, Refresh All, deletion and
annual dummy reconciliation still pass. Aug/Okt previews were rendered/reviewed.
Package checks confirm clean tables/caches, retained formulas/Master/names and
drawings/control counts, and only the reviewed VBA sources changed. An isolated
actual-store probe imports V4 and generates/exports 2027/2028 preserving its VBA.
At this initial V4 build step, the production template and original source were
untouched. Full generated-year calculation was a separate gate, resolved below.

Owner acceptance checklist (only the dedicated **V4** test copy):

1. Enable workbook macros. VBA-project editing is a separate setting; the owner
   elected to leave it enabled for development. Do not change Trust Center policy
   automatically.
2. On Aug and Okt, use Data Baru several times, set both transaction types and
   verify Pengeluaran dark-red text/light-red fill and Pemasukan dark-green
   text/light-green fill on new and older rows, matching the saved Okt reference.
3. Fill dummy expenses/income, allowing payment blanks. Refresh All: the category
   pivot should include expenses only, not income.
4. Save, close and reopen; verify colors/filter persist. An empty or income-only
   month should show no expense total and the explanatory notice.
5. Report acceptance before promoting the retained clean V4; the test copy's
   entered rows must not become the production template.

The owner subsequently accepted V4's appearance and requested verification of
generated 2027/2028. [The native year-verification report](WORKBOOK_YEAR_VERIFICATION_2026_10_04.md)
records successful full calendar and synthetic monthly/annual/category/cached
formula checks. The initially observed `xlPending` state was isolated to the backend's
persistent force-full-calculation marker: a flag-only control reaches `xlDone`
without formula changes. The verification-only request did not change the
backend policy or promote a template. The subsequent owner-approved fix and
promotion are now complete.

### V4 promotion — 2026-10-04

The clean, retained V4 candidate is now byte-for-byte installed at both local
template targets:

- `private/output/Keuangan_Pribadi_Template_2026.xlsm` (canonical private artifact);
- `.local/lajur/templates/finance-template.xlsm` (Docker bind mount at
  `/srv/lajur/templates/finance-template.xlsm`).

Whole-file SHA-256 is
`34A563C63BCF509861D1C2A92BA9CEA2D2080FEEBD04B72908D5E95755A22250`.
The source candidate remains unchanged and transaction-free; owner/synthetic
test files were not copied into production. The two prior template files were
backed up separately, hash-verified, then replaced atomically while the API was
stopped. Backup/evidence directory:
`.local/lajur/backups/templates/v4-promotion-20261004T154031Z-85362530/`.
Its `promotion-manifest.json` records the old/new hashes and protected files.

Fresh actual-backend generation passes native `xlDone` for 2027/2028 under the
corrected calculation-on-load policy. A further isolated probe using the
installed runtime template verifies empty import/generation/export, all 36
Master categories, corrected markers and byte-identical VBA. Existing account
workbooks, source XLSMs and owner V3/V4 test copies retain their hashes. Promotion
affects future creation from this template; it does **not** migrate existing
workbooks, even when a later successful transaction write updates their markers.

The Docker image has been rebuilt but the API remains stopped. On the next
requested start, use `docker compose up -d api` to recreate the service with the
new image; `docker start` alone would reuse the old container image.
For template-only rollback, keep the API stopped, verify the manifest's old
hashes, restore `canonical-before-v4.xlsm` and `runtime-before-v4.xlsm` to their
respective exact target paths, then recheck both hashes. This does not roll back
the application image or replace any account workbook. VBA-project editing
remains enabled by the owner's choice; Trust Center was not changed.

## Two artifacts, two purposes

Maintain two separate workbook artifacts outside the private source:

- a **sanitized regression fixture** committed under `tests/workbook-fixtures/`, containing representative structures and fake values;
- a **production template** stored privately/deployed with the API, containing the complete normalized workbook layout but no real transaction history.

Tests should use the sanitized fixture. New-year workbook generation should use the production template and then patch only known year/month-specific nodes.

The regression fixture is now available as `tests/workbook-fixtures/LajurWorkbookFixture.xlsm`. It contains synthetic Master values and eight fake transactions distributed across January, February, September, and December so tests cover populated, empty, early-year, and late-year monthly sheets. Verification covers package integrity, native Excel opening, tables, PivotTables, charts, controls, conditional formatting, formula-error parity with the clean template, absence of sampled private configuration strings, and byte-identical `vbaProject.bin` preservation.

## Automation policy

The [2026-10-03 updated-source audit](WORKBOOK_SOURCE_AUDIT_2026_10_03.md) records
new Master/date-helper/balance-formula variants and import blockers. The source
itself is not deployed; the reviewed, cleaned V4 derived from it has now been
promoted as recorded above. Future cleaning must preserve
approved formulas such as the new Des D1 roll-forward rather than blindly
clearing every B1/D1 cell as if it were a manual value.

The long-term normalization tool should be idempotent:

- audit mode reports deviations without editing;
- repair mode changes only explicitly approved invariant properties;
- a second repair run produces no further changes;
- every repair writes to a new output file and never overwrites the source directly;
- promotion to the active workbook occurs only after package and workbook validation succeeds.

This approach gives consistent layout while retaining the workbook as the canonical financial artifact.
