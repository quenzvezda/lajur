# Observed structure of `Keuangan_Pribadi_2026.xlsm`

This document records a read-only structural inspection of a private workbook copy on 2026-09-19. It is a preservation map for implementation and tests, not a license to hardcode every cell forever.

Transaction values, amounts, descriptions, category values, payment-method values, and comment text are intentionally excluded. The private source workbook must never be committed.

## 1. Package inventory

The inspected file is a valid macro-enabled Open XML package with 191 package parts.

Observed package features:

- `xl/vbaProject.bin` is present;
- 14 visible worksheets and no hidden worksheets;
- 12 monthly Excel tables;
- 27 charts in 13 drawing parts;
- 9 PivotTables backed by 18 pivot-cache parts;
- 13 legacy comment parts and associated VML drawings;
- form-control properties and macro buttons on monthly sheets;
- no external-link parts;
- no workbook connections;
- no embedded image/media parts.

Package counts describe the inspected snapshot. Preservation tests should compare the relevant part identities/content instead of assuming these counts can never change.

## 2. Sheet order

The visible sheets, in order, are:

1. `Rekap`
2. `Jan`
3. `Feb`
4. `Mar`
5. `April`
6. `May`
7. `June`
8. `July`
9. `Aug`
10. `Sep`
11. `Okt`
12. `Nov`
13. `Des`
14. `Master`

These names are part of the current workbook contract. Month names are not a consistent English-abbreviation scheme.

## 3. Monthly transaction tables

Each monthly sheet has one table starting at `A5:F...`:

| Month | Sheet | Table |
|---|---|---|
| 1 | `Jan` | `JanTable` |
| 2 | `Feb` | `FebTable` |
| 3 | `Mar` | `MarTable` |
| 4 | `April` | `AprilTable` |
| 5 | `May` | `MayTable` |
| 6 | `June` | `JuneTable` |
| 7 | `July` | `JulyTable` |
| 8 | `Aug` | `AugTable` |
| 9 | `Sep` | `SepTable` |
| 10 | `Okt` | `OktTable` |
| 11 | `Nov` | `NovTable` |
| 12 | `Des` | `DesTable` |

The table range grows and shrinks with real transactions, so the current ending row is deliberately not recorded as a contract.

Every table uses the same six visible columns:

```text
A  Tanggal
B  Tipe
C  Pembayaran
D  Kategori
E  Jumlah (Rp)
F  Keterangan
```

Data validation applies to the entry area through row 505:

- column B uses `ListTipe`;
- column C uses `ListPembayaran`;
- column D uses `ListKategori`.

The `Sep` sheet stores equivalent validation coverage in multiple ranges rather than one continuous validation record. Code must preserve effective validation, not assume identical XML layout on every month.

## 4. Monthly analytical layout

Monthly sheets extend through column `AO`. The transaction table is only the left-hand input area.

### H:I — category summary

- category-based spending summary;
- pie chart anchored near `H1`;
- `Jan` through `Sep` currently have PivotTable relationships in this area;
- `Okt` through `Des` retain the dashboard/chart layout but have no PivotTable relationship in the inspected snapshot.

Both variants are valid workbook states and must be preserved.

### K:P — cash projection

The `A. PROYEKSI KAS BULAN INI` panel contains:

- expected income sources;
- expected receipt date;
- estimate, recorded actual, not-yet-received amount, and status;
- manually editable yellow assumption cells;
- a projection summary for recorded income, pending income, projected income, projected spending, projected difference, and projected closing balance.

Actual income is derived with structured `SUMIFS` references to the monthly transaction table. Status formulas also depend on `TODAY()` and hidden month/date helpers.

### K:M — spending pace

The `B. LAJU BELANJA` panel contains:

- elapsed days;
- days in month;
- spending through the current day;
- daily average;
- projected month-end spending;
- historical average monthly spending from `Rekap`;
- total monthly budget from `Master`;
- a textual above/below-history status.

The month-end projection is a linear extrapolation from spending to date.

### R:V — rankings and comparison

The right-side panels contain:

- five largest transactions for the month;
- five largest categories for the month;
- totals for the ranking panels;
- comparison with the previous month for income, spending, transaction count, and average transaction value.

January contains a first-month explanatory message instead of a previous-month comparison.

### R onward — cumulative daily spending

The `E. BELANJA KUMULATIF HARIAN` panel has a line chart anchored near `R26`. Its series depend on hidden daily helper data.

### AD:AO — hidden helpers

Columns `AD:AO` are hidden on every monthly sheet. They contain calculation and chart-support data, including:

- first/last date of the month;
- elapsed-day and days-in-month helpers;
- daily date series;
- cumulative spending;
- average spending pace;
- ranking and chart-support formulas.

These columns are part of the analytical implementation. They must not be shifted, cleared, or reused for application metadata.

## 5. Monthly drawings, controls, and notes

Each monthly sheet has two charts:

- a category pie chart near `H1`;
- a cumulative-daily-spending line chart near `R26`.

Every monthly sheet also contains a form button wired to:

```text
TambahBarisBaru_ActiveSheet
```

The button, its control properties, VML drawing, worksheet relationship, and VBA implementation are one preservation unit. In the inspected snapshot, `Okt`, `Nov`, and `Des` contain two button/control objects while earlier months contain one. Mutation code must preserve the package as found rather than normalizing this difference silently.

Legacy notes/comments exist on every monthly sheet and on `Master`. Comment text is intentionally not recorded here.

## 6. Rekap

`Rekap` is an annual calculated dashboard, not an input sheet.

### Annual summary

The main summary at `A4:G17` contains:

- month;
- income;
- spending;
- difference;
- saving rate;
- transaction count;
- cumulative difference.

Rows below it contain annual totals, the count of populated months, and average spending per populated month.

### Category-by-month matrix

The matrix at `A22:Q46` aggregates monthly spending by category and includes:

- one column per month;
- annual total;
- average per populated month;
- monthly budget read from `Master`;
- difference against budget.

It is built mainly from structured `SUMIFS` references to all monthly tables.

### Rankings and payment summary

The lower area contains:

- an automatic top-ten category summary;
- a payment-method summary.

### Charts and helpers

`Rekap` contains three charts:

- income versus spending per month;
- top ten spending categories for the year;
- cumulative income-minus-spending difference.

Column `AH` is hidden and contains chart/ranking helper data. `Rekap` freezes the first column.

## 7. Master and defined names

`Master` is the workbook-owned source for categories, category kind, monthly budget, transaction types, and payment methods. Application code must read the values; it must not copy the current private lists into source code or documentation.

Observed defined names:

```text
ListKategori
  = OFFSET(Master!$A$5,0,0,COUNTA(Master!$A$5:$A$81),1)

ListPembayaran
  = OFFSET(Master!$G$5,0,0,COUNTA(Master!$G$5:$G$24),1)

ListTipe
  = Master!$E$5:$E$6
```

`ListKategori` and `ListPembayaran` are dynamic `OFFSET`/`COUNTA` ranges. The workbook adapter should preserve the defined-name formulas and read their resolved data without hardcoding the observed entries.

## 8. Formula dependency patterns

The workbook uses both structured table references and ordinary cell references.

Important patterns:

- `Rekap` derives monthly totals/counts from the 12 named monthly tables;
- the annual category matrix derives category totals from all monthly tables;
- monthly projections read their own monthly table, `Master`, and `Rekap`;
- month-over-month panels read the current and previous monthly tables;
- volatile `TODAY()` formulas determine elapsed time and projection status;
- charts depend on visible summaries and hidden helper ranges;
- cached formula values may be stale until desktop Excel recalculates.

Appending/deleting a transaction must update the table definition, not merely write or clear cells. Correct table mutation lets structured references continue to follow the canonical rows.

## 9. Year-specific content

Each monthly sheet contains two hard-coded year formulas in hidden helpers:

```text
AD1  = DATE(2026,<month>,1)
AG2  = DATE(2026,<month>,AF2)
```

`Rekap!A1` contains the visible year in its title.

New-year generation must update the known formula and text nodes for the requested year. It must not perform an unrestricted binary replacement over the XLSM package.

## 10. Preservation boundary

An ordinary transaction mutation may change only the intended transaction row, its monthly table range/metadata, and application-owned hidden metadata/audit sheets once those sheets exist.

It must preserve unrelated workbook features, including:

- VBA project bytes;
- form controls, control properties, macro bindings, and VML drawings;
- formulas and cached-value policy;
- chart parts, anchors, series, and worksheet relationships;
- PivotTables and pivot-cache parts;
- defined names;
- comments/notes;
- data validation;
- conditional formatting;
- cell styles, merged cells, column widths, hidden states, and page settings;
- visible analytical panels and hidden helper columns;
- unrelated ZIP/OPC package parts.

The preservation test suite should compare package parts and targeted XML before/after mutation. It should allow only a small explicit set of expected differences.

## 11. Inspection limitations

This inspection parsed the Open XML package without opening Excel and without executing VBA.

Therefore:

- macro behavior has not yet been run or validated;
- PivotTable refresh has not been performed;
- formulas have not been recalculated;
- visual rendering in desktop Excel has not yet been compared;
- the source workbook was not modified.

Those checks belong to later golden-workbook and manual Excel verification steps performed on disposable copies.

The consistency audit and proposed repair process are documented in [`WORKBOOK_TEMPLATE_NORMALIZATION.md`](WORKBOOK_TEMPLATE_NORMALIZATION.md).
