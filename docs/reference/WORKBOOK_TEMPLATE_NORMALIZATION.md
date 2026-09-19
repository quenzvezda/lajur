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

## Two artifacts, two purposes

Maintain two separate workbook artifacts outside the private source:

- a **sanitized regression fixture** committed under `tests/workbook-fixtures/`, containing representative structures and fake values;
- a **production template** stored privately/deployed with the API, containing the complete normalized workbook layout but no real transaction history.

Tests should use the sanitized fixture. New-year workbook generation should use the production template and then patch only known year/month-specific nodes.

## Automation policy

The long-term normalization tool should be idempotent:

- audit mode reports deviations without editing;
- repair mode changes only explicitly approved invariant properties;
- a second repair run produces no further changes;
- every repair writes to a new output file and never overwrites the source directly;
- promotion to the active workbook occurs only after package and workbook validation succeeds.

This approach gives consistent layout while retaining the workbook as the canonical financial artifact.
