# Excel workbook contract

## 1. Principle

The macro-enabled Excel workbook is the canonical financial store.

The backend must mutate it conservatively and preserve all unrelated workbook features.

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

## 5. Do not add transaction system columns to A:F table

Earlier design discussion considered `TransactionId`, `Revision`, and timestamp columns adjacent to the transaction table.

**Rejected for v1 after inspecting the real workbook.**

Reason:

- monthly sheets have active content to the right of column F;
- for example the supplied September sheet uses H/I for summary/pivot-style content and K:O for cash projections;
- inserting table columns risks moving/breaking formulas, pivots, drawings, macros, or expected addresses.

Use hidden system sheets instead.

## 6. Hidden system sheets

Create if absent:

### `__AppIndex`

Application transaction identity/revision metadata. See `DATA_MODEL.md`.

Visibility should be **very hidden** if supported safely by the workbook package, otherwise hidden.

### `__AuditLog`

Append-only application financial change history.

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

No server-side execution of VBA is required.

## 9. Formula/cached-value rule

The supplied workbook contains calculated reports and formula caches. The backend must not assume cached values have been recalculated after a write.

API summaries are computed from transaction rows.

Desktop Excel is responsible for full formula recalculation when opened. The backend may set safe calculation properties requesting recalculation on load.

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

## 16. Direct server-file editing

Out of scope in v1:

- opening the active server XLSM over SMB/network share while API writes;
- arbitrary third-party process writing active workbook concurrently.

Supported manual workflow:

`export → edit locally in Excel → import`

This avoids an unbounded filesystem race/conflict problem while preserving Excel ownership.
