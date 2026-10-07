# UX flows

This document defines information architecture and behavior, not final visual styling. Exact Compose components can evolve after a usable prototype exists.

## 1. Navigation

Primary signed-in destinations use a fixed bottom bar: **Beranda**, **Riwayat**,
**Scan** (prominent center action), **Insight**, and **Setelan**. Insight is an
explicit TODO/coming-soon placeholder, not an implemented budgeting feature.

Required destinations:

```text
Launch/Auth Gate
     │
     ├── Login (when server session unavailable)
     └── App Unlock (when locally locked)
              │
              ▼
             Home
          ┌───┼───────────────┐
          │   │               │
     Manual Add            Smart Scan
          │                   │
          └──────► Draft/Editor
                         │
                       Save
                         │
                       Home

Home transaction tap → Transaction Detail → Edit / Delete

Bottom bar Setelan → Settings
Settings → Data / Workbook → list/create/import/export
```

Focused editor/detail/workbook/scan-review screens hide the bottom bar. System
Back and toolbar Back follow the same routes: new entry returns to its originating
Home/History, edit returns to detail, detail returns to its originating list,
workbook returns to Settings, and scan review discards the draft and returns to
the Scan entry page. Back on other primary destinations returns to Home.
On Home, first Back shows **Tekan sekali lagi untuk keluar**; another press within
two seconds finishes the Activity without signing out. Navigation or backgrounding
resets this window. Modal dialogs/sheets and the workbook busy guard take priority.

## 2. Launch/auth gate

### Existing valid server session + local session unlocked

Open Home immediately.

### Existing valid server session + app locally locked

Show BiometricPrompt. If device credential fallback is enabled, allow it.

### No usable refresh session

Show username/password login.

Do not present both login and biometric as interchangeable server credentials. Biometrics unlock a previously established local session.

## 3. Auto-lock

Default: 5 minutes.

Suggested choices:

- immediately;
- 1 minute;
- 5 minutes;
- 15 minutes;
- 30 minutes;
- never, only if the operator explicitly permits this option.

Behavior:

- switching apps briefly within the grace period does not prompt;
- after the grace period, returning to any sensitive Lajur UI requires unlock;
- background WorkManager sync continues without a biometric prompt.

## 4. Home

Default scope: current calendar month.

Suggested content:

```text
Lajur

September 2026                           [change month]

Ringkasan bulan ini
Pemasukan / Pengeluaran / Selisih / Jumlah transaksi

[ + Tambah ]

Sync: ✓ / Pending 3 / Error

Transaksi terbaru                        [Lihat semua]
─────────────────────────────────────────
Rp 85.000     Kebutuhan · Shopee Pay
Rp 25.000     Jajan · Cash
Rp 40.000     Transport · Bank

[Beranda] [Riwayat] [Scan] [Insight] [Setelan]
```

The implemented Home shows the selected month, full-month income/expense,
**Selisih bulan ini** (income minus expense, not an account balance), loaded
transaction count, sync/review reminders, and up to three non-deleted recent-date
transactions. **Lihat semua** opens History, where full-month search and review
filtering live. History search must not filter Home totals or preview.
Income cards use green, expense cards red in light and dark themes; explicit
type labels remain so meaning is not conveyed by color alone. Manual **+ Tambah**
is available on Home and History. The workbook remains the rich reporting surface.

When cached non-deleted rows in the selected month have empty payment (or another
missing/invalid required field), show **Perlu dilengkapi** with a transaction
count and **Lihat transaksi**. This count is independent of text search, not an
account-wide/server completeness claim. The action clears search and filters
the History list; **Lihat semua transaksi** removes the review filter. No blocking modal,
unpaid status, or change to financial totals. Filling payment later is an edit
to the original row. A blank method reads **Pembayaran belum diisi** on the list.

## 4a. Data / Workbook

The Settings entry lists server workbook years, sizes and last-modified time.
Create uses an explicit year and never replaces an existing workbook. Import
uses the system document picker, displays the selected file, requests its year,
and separates replacement opt-in from the final confirmation. Pending/failed
operations disable import; no hidden discard/merge is permitted. Export one
XLSM/all-years ZIP uses the system Save As picker and warns that pending local
changes are not included. Progress and errors stay visible on the Data screen.
After import, Home selects the imported year and reloads canonical data.

## 5. Manual Add

Interaction optimization:

1. Amount should be easy to reach and use numeric input.
2. Default date = today.
3. Default type = `Pengeluaran`.
4. Payment method is optional and can be filled later on the same transaction.
5. Category is explicit.
6. Description is optional.

Current editor: amount appears first, accepts whole-IDR digits, and displays
thousands separators without changing the stored integer. Date opens a calendar.
Short type/payment menus match the field width. Category opens a searchable,
scrollable bottom sheet showing only Master categories for the selected type;
changing type clears the category. Missing required fields prevent save and
are summarized near the save action. Remembering the last payment remains a
future preference, not a currently implemented default.

Payment is labeled **Pembayaran (opsional)** with **Opsional. Bisa diisi nanti.**
and a **Kosongkan pembayaran** choice. Only date, type, category and positive
integer amount are required. An explicitly chosen method still must be a Master
value. The same optionality applies to scan review; missing payment is neutral,
not a red required-field warning. Save continues to require human review.

Save behavior:

- validate locally;
- write local transaction + outbox in one Room transaction;
- return immediate success to the user;
- synchronization is independent.

## 6. Smart Scan

Entry points:

- bottom bar center Scan → image/camera entry page;
- image picker;
- camera;
- incoming Android image share intent.

If the app is locked, incoming share intent must pass through the same app-lock gate before sensitive content/draft is shown.

### Processing state

The user should see a single meaningful state such as:

`Analyzing transaction…`

Selecting an image runs only local OCR/evidence rules. It does not initialize a
model. A validated draft appears as soon as OCR finishes. The optional
**Tambahkan saran AI** action starts local semantic suggestions explicitly.
The user can cancel AI or continue into the editor without waiting. Entering
the editor invalidates late AI results so they cannot overwrite user corrections.

### Draft UI

Show the draft first. Raw OCR, dimensions, timings, and debug report saving live
under **Lihat teks struk & detail**, collapsed by default.

Supported values show their source: **Terbaca dari struk** or **Saran AI · periksa**.
Lower-confidence evidence is marked **Hasil baca perlu diperiksa**; these labels
are review hints, not calibrated accuracy scores. Evidence and alternatives
expand per field without a narrow right-hand label column.

Low/ambiguous values should be visually marked, e.g.:

```text
Jumlah
Rp 128.500     ⚠ Periksa
```

Tapping the field can reveal alternatives:

```text
Possible values:
● Rp 128.500
○ Rp 125.000
[Enter manually]
```

Do not routinely display fake-precision percentages such as “72% confidence”.

### Missing fields

A required field can remain empty:

```text
Kategori *
[ Pilih kategori… ]
```

Save is disabled until required fields are valid.

**Periksa & lengkapi transaksi** opens the ordinary editor with validated partial
values. A missing scan date or type remains empty; manual-entry defaults do not
silently stand in for extraction. Missing category must be selected explicitly.

### Important

Smart Scan never directly persists a canonical transaction without review.

## 7. Transaction detail

Show:

- date;
- type;
- payment;
- category;
- amount;
- description;
- sync state if still local/pending.

Actions:

- Edit;
- Delete.

Audit history does not need a dedicated v1 screen, though the API/data model may expose it later.

## 8. Edit

If transaction is still pending locally:

- modify local record;
- collapse/replace pending operation when safe.

If synchronized:

- create/update an outbox UPDATE command carrying expected server revision.

If API returns revision conflict:

- do not overwrite;
- fetch latest canonical transaction;
- show a concise conflict message;
- require user to review the latest state before retrying.

## 9. Delete

Delete requires confirmation.

If unsynchronized create has never reached server, delete can remove/cancel the pending create locally.

If synchronized:

- enqueue DELETE with expected revision;
- remove/hide from normal local list optimistically only if the UI clearly represents pending state;
- on permanent server failure, restore canonical view and surface the error.

No full recycle bin in v1.

## 10. Settings

Groups:

- Security
- Sync
- Smart Scan
- Transaction defaults
- Appearance
- Data

See `docs/architecture/SETTINGS.md`.

## 11. Error philosophy

Prefer actionable states:

- `Pending sync — no internet`
- `Sync failed — tap to retry`
- `Workbook import rejected — current data was not changed`
- `This transaction changed on the server — reload before editing`

Avoid raw HTTP/status/stack-trace messages in normal UI.
