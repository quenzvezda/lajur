# Excel workbook contract

## 1. Principle

The macro-enabled Excel workbook is the canonical financial store.

The backend must mutate it conservatively and preserve all unrelated workbook features.

The detailed, privacy-safe inventory of the supplied workbook is maintained in [`../reference/WORKBOOK_OBSERVED_STRUCTURE.md`](../reference/WORKBOOK_OBSERVED_STRUCTURE.md).

## 2. File model

One account owns one workbook per year:

`Keuangan_Pribadi_<YYYY>.xlsm`

Examples:

- `Keuangan_Pribadi_2026.xlsm`
- `Keuangan_Pribadi_2027.xlsm`

## 3. Required visible sheets

Baseline template:

- `Rekap`
- `Jan`
- `Feb`
- `Mar`
- `April`
- `May`
- `June`
- `July`
- `Aug`
- `Sep`
- `Okt`
- `Nov`
- `Des`
- `Master`

The exact names are part of the current workbook contract. Do not derive month sheet/table names by a naive English abbreviation rule.

## 4. Required monthly tables

| Month | Sheet | Table |
|---|---|---|
| 1 | Jan | JanTable |
| 2 | Feb | FebTable |
| 3 | Mar | MarTable |
| 4 | April | AprilTable |
| 5 | May | MayTable |
| 6 | June | JuneTable |
| 7 | July | JulyTable |
| 8 | Aug | AugTable |
| 9 | Sep | SepTable |
| 10 | Okt | OktTable |
| 11 | Nov | NovTable |
| 12 | Des | DesTable |

Every table uses visible columns:

```text
A Tanggal
B Tipe
C Pembayaran
D Kategori
E Jumlah (Rp)
F Keterangan
```

Column C is optional on an otherwise valid transaction row. Readers, metadata
snapshots and imports represent its blank value as an empty string; writes keep
the existing cell style and blank content. No synthetic payment method, extra
status column or separate repayment row is introduced. A/B/D/E remain required.
Filling C later is an ordinary revisioned edit to the same transaction.

## 5. Do not add transaction system columns to A:F table

Earlier design discussion considered `TransactionId`, `Revision`, and timestamp columns adjacent to the transaction table.

**Rejected for v1 after inspecting the real workbook.**

Reason:

- monthly sheets have active content to the right of column F;
- monthly sheets use H:I for category analytics, K:P for cash projection and spending pace, R:V and farther-right areas for rankings/comparisons/charts, and hidden AD:AO helpers;
- these areas include charts, PivotTables, cached pivot data, comments, controls, conditional formatting, and formulas;
- inserting table columns risks moving/breaking formulas, pivots, drawings, macros, or expected addresses.

Use hidden system sheets instead.

## 6. Hidden system sheets

Create if absent:

### `__AppIndex`

Application transaction identity/revision metadata. See `DATA_MODEL.md`.

Visibility should be **very hidden** if supported safely by the workbook package, otherwise hidden.

### `__AuditLog`

Append-only application financial change history.

### `__MasterIcons`

Very-hidden presentation mapping introduced by guarded Master commands:
`PaymentMethod`, `Icon`. Fixed local icon keys do not alter canonical payment
names or financial formulas. GET/default display does not initialize this sheet.

### `LajurMaster` (visible supplemental report)

Guarded Master commands create/regenerate a managed annual category/payment
matrix covering every current Master expense category and method, outside-Master
expense totals and blank payment. Formulas reference the existing A:F tables;
the original Rekap and monthly formula/chart/pivot layouts remain unchanged.
Owned/shared formula shapes are validated before regeneration. Customized or
colliding report sheets block mutation. Year generation preserves/regenerates
this owned report for the target year when it exists in the approved template.

System sheets must not be referenced by normal user formulas unless explicitly designed.

## 7. Master contract

Observed defined names:

```text
ListKategori
  = OFFSET(Master!$A$5,0,0,COUNTA(Master!$A$5:$A$80),1)

ListPembayaran
  = OFFSET(Master!$G$5,0,0,COUNTA(Master!$G$5:$G$24),1)

ListTipe
  = Master!$E$5:$E$6
```

Observed conceptual layout:

```text
A: category
B: category kind
C: monthly budget
E: transaction type
G: payment method
```

Backend Master DTO should read workbook data rather than hardcode these values in Android.

## 8. VBA/macros

The supplied workbook contains `xl/vbaProject.bin`.

Any mutation/import/migration must preserve VBA/package parts unless a deliberate template migration explicitly changes them.

Workbook preservation tests should verify the VBA part remains present and, for ordinary transaction writes, byte-identical.

The macro button binding `TambahBarisBaru_ActiveSheet`, form controls, control properties, and VML drawings must also survive ordinary mutations unchanged.

No server-side execution of VBA is required.

## 9. Formula/cached-value rule

The supplied workbook contains calculated reports and formula caches. The backend must not assume cached values have been recalculated after a write.

API summaries are computed from transaction rows.

Desktop Excel is responsible for full formula recalculation when opened. The backend may set safe calculation properties requesting recalculation on load.

Every successful workbook mutation/import/year-generation save requests automatic
calculation with `fullCalcOnLoad=true` and `forceFullCalc=false`. This asks Excel
to rebuild results on opening without leaving the workbook in persistent forced
calculation mode (`xlPending`). The server does not calculate or trust those
caches. Idempotent no-op retries do not rewrite the workbook just to reset flags.

Do not rewrite formula results as hardcoded values.

## 10. Table append

Creating a transaction:

1. choose workbook by transaction year;
2. choose monthly sheet/table by month;
3. append a new A:F data row;
4. expand the table reference correctly;
5. preserve style/data validation behavior where expected;
6. create `__AppIndex` entry;
7. append `__AuditLog` CREATE;
8. save via atomic mutation.

Do not simply write “the next empty cells” without updating table metadata.

## 11. Update

Use `__AppIndex` to locate the canonical transaction.

Validate expected revision.

Update only A:F finance fields and the associated metadata/audit rows.

If date moves across months within the same year:

- remove row from old table;
- append to new month table;
- preserve transaction ID;
- increment revision once;
- audit as UPDATE with before/after.

If date moves across years:

- this is a cross-workbook operation and requires a carefully atomic/compensating implementation. It may be deferred initially by requiring delete+create or restricting edits across years until explicitly implemented.

## 12. Delete

- capture before state;
- remove the monthly table row;
- shrink table range correctly;
- keep an `__AppIndex` tombstone;
- append audit DELETE.

## 13. Import

Never trust an uploaded workbook based only on extension.

Validate:

- ZIP/OPC package readable;
- macro-enabled workbook type expected;
- required sheets;
- required tables and headers;
- Master structure/names sufficiently compatible;
- size/decompression safety limits;
- no path traversal from filename;
- template/schema compatibility.

Import occurs in staging and promotes atomically only after success.

## 14. Existing workbook migration

For a legacy compatible workbook without system sheets:

- add `__AppIndex`;
- add `__AuditLog`;
- assign transaction IDs to existing A:F rows;
- revision = 1;
- snapshot/hash current row;
- record migration metadata;
- do not generate a CREATE audit entry for every historical row unless deliberately desired; a single SYSTEM_MIGRATION event may be preferable.

## 15. Year generation

The supplied 2026 workbook has year-specific formulas, including monthly helper formulas containing `DATE(2026,...)`, plus year text in `Rekap`.

Generation for a target year must patch only known year-dependent text/formulas, not raw binary replacements across the entire XLSM.

Create a tested migration routine:

`GenerateYear(templateWorkbook, targetYear)`

and verify every monthly date helper points to the target year.

The bounded monthly calendar contract is:

- `AD1`: `DATE(year,month,1)`, with the month matching the explicit sheet mapping.
- `AG2:AG32`: `DATE(year,month,AFrow)` or `$AD$1+AFrow-1`, with the AF reference
  matching the helper's row. Mixed direct/relative series are supported.
- Normal formulas and shared groups wholly contained in AG2:AG32 are supported.
  Shared anchors must start their declared range; followers must belong to that
  group. Unrelated anchors, missing helpers, incorrect offsets/months/years and
  arbitrary expressions are rejected, not evaluated by the server.

Reader and generator use the same recognition rules, allowing insignificant
case/whitespace differences without rewriting them. Detection cross-checks AD1,
all daily helpers, ledger dates and the Rekap year. Generation patches only the
year token in known direct DATE formulas; relative expressions and shared-group
attributes remain unchanged. All 31 helper slots remain present, including dates
beyond a short month's last day; the existing analytical formulas decide how to
display those points. AF day numbers and business formulas are not rewritten.
All AD1/AG2:AG32 caches are cleared on generation, and Excel full recalculation is
requested. Import does not recalculate or rewrite these helper formulas/caches.

Generation/import explicitly save changed parts rather than auto-saving every
loaded XML part; reading shared strings must not reserialize their untouched
package part. Sanitized tests verify byte-preserved VBA, shared strings, controls,
pivots/charts, worksheet relationships and other unrelated parts. Template
promotion and desktop Excel/macro acceptance remain separate owner-reviewed work.

## 16. Direct server-file editing

Browser settings mutations (ADR 0010) are a narrow additional write boundary:
existing expense-category targets in Master C, recognized monthly source payout
days in L and literal estimates in M, and the known meal input cells M10/M11.
The meal formula itself, actual-income formulas, other projection formulas,
named ranges, macro/control/chart parts and monthly A:F remain unchanged.
Unsupported/formula-backed bindings are read-only. Nullable inputs remain empty;
zero remains an explicit numeric value. Each accepted batch appends one settings
audit, requests recalculation, creates a backup and atomically promotes the book.

Out of scope in v1:

- opening the active server XLSM over SMB/network share while API writes;
- arbitrary third-party process writing active workbook concurrently.

Supported manual workflow:

`export → edit locally in Excel → import`

This avoids an unbounded filesystem race/conflict problem while preserving Excel ownership.
