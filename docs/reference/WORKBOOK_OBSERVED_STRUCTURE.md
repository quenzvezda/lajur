# Observed structure of `Keuangan_Pribadi_2026.xlsm`

This document records facts observed from the workbook supplied during requirements/design. It is a development reference, not a license to hardcode every cell forever.

## 1. Workbook type

- Macro-enabled `.xlsm`.
- Contains `xl/vbaProject.bin`.
- Workbook calculation metadata indicates calculation is not something the backend should treat as already complete after edits.

## 2. Visible sheets

In order:

1. Rekap
2. Jan
3. Feb
4. Mar
5. April
6. May
7. June
8. July
9. Aug
10. Sep
11. Okt
12. Nov
13. Des
14. Master

## 3. Monthly Excel tables

Observed table names/ranges at inspection time:

| Table | Observed ref |
|---|---|
| JanTable | A5:F79 |
| FebTable | A5:F105 |
| MarTable | A5:F78 |
| AprilTable | A5:F90 |
| MayTable | A5:F108 |
| JuneTable | A5:F121 |
| JulyTable | A5:F95 |
| AugTable | A5:F84 |
| SepTable | A5:F62 |
| OktTable | A5:F6 |
| NovTable | A5:F6 |
| DesTable | A5:F6 |

All table headers:

```text
Tanggal
Tipe
Pembayaran
Kategori
Jumlah (Rp)
Keterangan
```

Ranges will naturally change as transactions are appended/deleted; the names and header contract are what matter.

## 4. Master

Observed visible labels include:

- `Kategori`
- `Jenis`
- `Budget / Bulan`
- `Tipe`
- `Metode Pembayaran`

Observed transaction types:

- Pengeluaran
- Pemasukan

Observed payment methods at inspection time:

- Cash
- Bank
- Jago
- Dana
- Gopay
- Shopee Pay
- Shopee Pay Later
- Visa

Observed categories include:

- Makanan-Jadi
- Makanan-Mentah
- Jajan
- Buah
- Sayur
- Bumbu
- Masak-Mamah
- Mamah
- Kebutuhan
- Transport
- Kucing
- Perawatan
- Fashion
- Obat
- Kuota
- Elektronik
- Furniture
- Coding
- Sport
- Entertainment
- Pindahan
- Investasi
- Ketipu
- Gaji
- THR
- Uang Makan
- Freelance
- Titipan Teman
- Bonus
- Lainnya

The app should read these from Master; this list is documentation, not a hardcoded Android enum.

## 5. Defined names

Observed:

```text
ListKategori
= OFFSET(Master!$A$5,0,0,COUNTA(Master!$A$5:$A$80),1)

ListPembayaran
= OFFSET(Master!$G$5,0,0,COUNTA(Master!$G$5:$G$24),1)

ListTipe
= Master!$E$5:$E$6
```

## 6. Rekap

`Rekap` uses structured references such as:

```text
SUMIFS(SepTable[Jumlah (Rp)], SepTable[Tipe], "Pengeluaran")
COUNTA(SepTable[Tanggal])
```

It also aggregates spending by category/payment and other annual summaries.

The API must preserve these formulas but need not use their cached results.

## 7. Monthly right-side content

Monthly sheets contain more than the A:F transaction table.

Example observed on `Sep`:

- H/I area contains transaction/category summary content.
- K:O contains “PROYEKSI KAS BULAN INI” and related formulas.
- AD/AF/AG/AH and other farther-right helper cells/formulas exist.

Therefore inserting transaction metadata columns after F is unsafe without a dedicated workbook-layout migration.

## 8. Year-specific formulas

Monthly helper formulas include hard-coded year expressions such as:

```text
DATE(2026,1,1)
DATE(2026,1,AF2)
...
DATE(2026,12,1)
DATE(2026,12,AF2)
```

New-year generation must update these formula nodes for the target year.

`Rekap` also contains year-specific visible title text such as `REKAP TAHUNAN 2026`.

## 9. Formula recalculation

Because formulas and cached values exist, server code must not assume that changing table rows has recalculated every displayed summary.

Android/API summaries should be computed directly from canonical transaction rows.

Desktop Excel can recalculate workbook reports on open.

## 10. Preservation rule

Any implementation touching XLSM must prove it does not accidentally remove:

- VBA project;
- table definitions;
- defined names;
- formulas;
- right-side content;
- styles/validation;
- other unknown package parts.
