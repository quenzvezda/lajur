# Riset UX penyuntingan keuangan web Lajur

**Tanggal riset:** 2026-10-08 (Asia/Jakarta)  
**Status:** bahan desain; belum menjadi kontrak implementasi/ADR  
**Lingkup:** sumber publik primer untuk transaksi, Master, budget, uang makan, aksesibilitas, keamanan mutasi browser, dan ikon pembayaran. Tidak mengakses transaksi pribadi.

Dokumen ini memisahkan **fakta sumber**, **hasil pemeriksaan repository**, dan **usulan desain**. Pola aplikasi lain menjadi pembanding interaksi, bukan alasan mengubah Lajur menjadi aplikasi akuntansi atau mengalihkan kebenaran finansial dari XLSM. Bagian 11–15 menghubungkan riset dengan kontrak dan kode Lajur saat ini. Contoh angka/nama dalam usulan adalah sintetis.

## 1. Temuan yang paling berguna

| Kebutuhan | Bukti primer | Implikasi desain yang diusulkan |
|---|---|---|
| Input banyak transaksi dengan cepat | Actual mempunyai Tab/Shift+Tab antarkolom, Enter menerima pilihan, dan input berkelanjutan setelah penambahan. [Actual Tips & Tricks](https://actualbudget.org/docs/getting-started/tips-tricks/) | Pertahankan form tambah setelah save berhasil; tampilkan pilihan `Simpan & tambah lagi`; jangan mewajibkan membuka modal baru setiap baris. |
| Rasa spreadsheet yang tetap aman | APG membedakan navigasi grid dari editing di dalam sel. [W3C Grid](https://www.w3.org/WAI/ARIA/apg/patterns/grid/) | Tabel dapat terasa seperti Excel lewat keyboard/fokus, tanpa memberi editor semua sel/formula workbook. |
| Simpan transaksi lengkap | AG Grid mendukung validasi per editor dan antarfield pada satu baris. [AG Grid Validation](https://www.ag-grid.com/javascript-data-grid/cell-editing-validation/) | Sel boleh diedit langsung, tetapi tanggal/tipe/kategori/nominal dikomit sebagai satu transaksi. |
| Perubahan Master berpengaruh ke histori | YNAB mengarahkan kategori terpakai untuk disembunyikan atau merealokasikan aktivitas ketika dihapus. [YNAB Categories](https://support.ynab.com/en_us/adding-removing-and-customizing-categories-a-guide-HJFO5j909) | Pisahkan perubahan kosmetik, arsip, dan migrasi data; jangan menghapus kategori terpakai secara diam-diam. |
| Konfirmasi tidak menghambat input harian | WCAG 3.3.4 menerima reversible, checked, atau confirmed; penjelasannya tidak meminta konfirmasi tiap save biasa. [W3C Error Prevention](https://www.w3.org/WAI/WCAG22/Understanding/error-prevention-legal-financial-data.html) | Validasi inline untuk CRUD biasa; review dampak untuk delete massal/Master. |
| Retry dan konflik | HTTP conditional requests mencegah lost update; Stripe menunjukkan deduplikasi berdasarkan key operasi. [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.1), [Stripe Idempotency](https://docs.stripe.com/api/idempotent_requests) | Draft tetap tersedia saat gagal; retry memakai identitas operasi yang sama; perubahan versi harus dibahas pengguna. |
| Logo pembayaran | BI mendefinisikan QRIS sebagai standar lintas aplikasi/instrumen. ShopeePay punya pedoman logo tersendiri. [BI FAQ QRIS](https://www.bi.go.id/id/publikasi/peraturan/Documents/faq_padg_211819.pdf), [ShopeePay Guidelines](https://product.shopeepay.co.id/products/brand-guidelines) | QRIS bukan identitas rekening; `Shopee` tidak otomatis berarti `ShopeePay`; ikon mendampingi teks. |

## 2. Input transaksi: cepat tetapi mudah dipahami

### Fakta sumber

Actual menyediakan shortcut penambahan transaksi, navigasi antarkolom dengan Tab, penerimaan pilihan dropdown dengan Enter, serta opsi menambah transaksi sambil membiarkan form terbuka. Aplikasi ini juga mendukung duplicate transaksi dan command palette. Undo di versi web hanya berlaku dalam sesi tab; refresh menghilangkan histori undo. [Actual Tips & Tricks](https://actualbudget.org/docs/getting-started/tips-tricks/)

APG menjelaskan bahwa arrow keys untuk berpindah sel harus dialihkan ketika pengguna sedang mengedit teks atau memilih opsi. Enter/F2 dapat membuka editing; Escape dapat mengembalikan navigasi dan membatalkan edit. Grid juga mempunyai konsekuensi fokus/ARIA yang harus dikelola penulis aplikasi. [W3C Grid Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/grid/)

Combobox cocok baik untuk nilai terbatas maupun saran input bebas. APG membedakan autocomplete manual dari otomatis, dan mengingatkan agar shortcut aplikasi tidak merebut fungsi editing teks browser. Escape dapat menutup popup tanpa mengganti pilihan sebelumnya. [W3C Combobox Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/combobox/)

### Usulan desain

- Satu baris tambah yang selalu mudah dijangkau di atas tabel, dengan form vertikal alternatif untuk layar kecil.
- Urutan keyboard mengikuti kolom workbook; kategorinya combobox pencarian. Keterangan bisa menyarankan teks terdahulu tanpa mengubah kategori/nominal otomatis.
- `Tab` berpindah field; `Enter` saat dropdown terbuka menerima opsi; `Ctrl+Enter` menyimpan transaksi lengkap. Tombol terlihat tetap tersedia bagi pengguna tanpa keyboard.
- Setelah berhasil: tanggal dan tipe dapat dipertahankan; nominal dan keterangan dikosongkan. Opsi mempertahankan kategori/pembayaran harus terlihat, karena mengingat semuanya tanpa penjelasan berisiko salah konteks.
- Default tanggal berada dalam bulan yang sedang dilihat. Ketika pengguna melihat bulan lama, tampilkan bulan tujuan dengan jelas; jangan menyimpan tanggal hari ini tanpa petunjuk.
- Ubah tanggal ke bulan lain dalam tahun yang sama: tampilkan penjelasan perpindahan sheet dan tautan ke bulan tujuan setelah berhasil.
- Tombol `Duplikat` membuat draft baru untuk transaksi berulang. Draft harus punya identitas baru ketika pengguna memutuskan menyimpan.
- Nominal disimpan sebagai integer IDR; format ribuan hanya tampilan. Jika singkatan seperti `25rb` ditambahkan, hasil normalisasinya harus terlihat sebelum save. Jangan menggunakan `eval` atau menganggap kolom sebagai formula Excel.
- Pembayaran kosong harus menjadi opsi eksplisit, misalnya `Belum diisi`; tidak otomatis diganti Cash.

### Tradeoff

Shortcut satu huruf seperti `T` atau `D` dapat efisien, tetapi rawan aktif ketika sedang mengetik. Mulai dengan tombol plus, Tab, Escape, dan kombinasi save yang terikat pada form; shortcut tambahan perlu scope fokus dan bantuan keyboard. Meniru semua perilaku Excel sekaligus menaikkan kompleksitas aksesibilitas dan debugging.

## 3. Edit langsung di tabel dan batas commit

### Fakta sumber

AG Grid menyediakan mode validasi `revert` atau `block`. Dalam full-row editing, validasi dapat memeriksa hubungan antarfield; nilai row belum diperbarui sebelum commit. Ini membuktikan pola draft per baris dapat dibangun tanpa mengubah data setiap kali pengguna keluar dari satu sel. [AG Grid Cell Editing Validation](https://www.ag-grid.com/javascript-data-grid/cell-editing-validation/)

Batch editing AG Grid menahan beberapa edit untuk dikomit/dibuang bersama. Dokumentasinya menandai fitur ini sebagai Enterprise dan membatasinya pada client-side row model. [AG Grid Batch Editing](https://www.ag-grid.com/javascript-data-grid/cell-editing-batch/)

### Usulan desain

- Klik/Edit/F2 membuka draft satu transaksi. Sel yang berubah diberi indikator plus teks `Belum disimpan`.
- Sediakan `Simpan`/`Batal` pada baris; jangan menyimpan field parsial pada blur.
- Satu field yang salah tidak menghapus semua input; pesan ditempel pada field dengan penjelasan tindakan koreksi.
- Status dibedakan: `Draft`, `Menyimpan`, `Tersimpan`, `Gagal`, `Konflik`. Ringkasan finansial menunjukkan data kanonis; preview dampak draft diberi label tersendiri.
- Sorting/filtering tidak mengubah identitas target. Row number hanya posisi tampilan, bukan alamat transaksi untuk update/delete.
- Saat filter akan menyembunyikan transaksi yang baru diubah, beri feedback dan tautan `Lihat transaksi`; jangan membuat pengguna mengira save hilang.
- Update massal tahap berikutnya hanya untuk field yang dapat diubah secara konsisten, dengan daftar target dan hitungan terlihat. Operasi lintas sheet/tahun memerlukan batas yang jelas.

Library grid hanya mengatur state UI; commit di dalam library tidak membuktikan workbook sudah tersimpan. Atomic write, validasi kanonis, dan revision tetap kewajiban server.

## 4. Paste dari Excel/CSV: masukkan ke staging dahulu

### Fakta sumber

Actual CSV import meminta pemetaan kolom, format tanggal, delimiter, serta memperlihatkan interpretasi tanggal sebelum impor. Deduplikasinya memanfaatkan identitas impor bila tersedia, kemudian kemiripan tanggal/nominal/payee. [Actual Importing Transactions](https://actualbudget.org/docs/transactions/importing/)

AG Grid mempunyai clipboard processing; kemampuan clipboard lanjutan dan range selection tercantum sebagai Enterprise. Community tersedia tanpa lisensi berbayar dan berlisensi MIT; Enterprise memerlukan lisensi produksi. [AG Grid Clipboard](https://www.ag-grid.com/javascript-data-grid/clipboard/), [Community vs. Enterprise](https://www.ag-grid.com/javascript-data-grid/community-vs-enterprise/)

OWASP mendokumentasikan formula injection ketika input tidak tepercaya diekspor ke CSV lalu dibaca spreadsheet sebagai formula. Quoting CSV saja tidak menjamin formula tidak dieksekusi. [OWASP CSV Injection](https://community.owasp.org/attacks/CSV_Injection)

### Usulan desain

1. Paste tabel tab-separated ke area `Tempel dari Excel`.
2. Preview pemetaan ke enam kolom transaksi, bulan tujuan, nilai nominal/tanggal yang sudah diparse, dan total baris.
3. Tandai invalid date, kategori tidak dikenal, nominal tidak valid, kemungkinan duplikat, dan baris di luar bulan pilihan.
4. Pengguna memperbaiki staging; tidak ada tulisan workbook sebelum menekan `Simpan transaksi`.
5. Server memvalidasi ulang semuanya. Pilih atomisitas batch secara eksplisit; partial success harus dilaporkan per baris dan retry tidak boleh membuat duplikat.

Duplikat yang isinya mirip belum tentu kesalahan: dua transaksi bernominal sama dalam satu hari bisa sah. Tampilkan peringatan, jangan otomatis menggabungkan/menghapus. Pola deduplikasi impor Actual tidak disarankan disalin tanpa menguji semantik Lajur.

Teks hasil paste harus ditulis sebagai teks, bukan formula. Risiko ekspor CSV berbeda dari XLSM: penulisan string XLSM wajib menggunakan tipe string yang aman, sedangkan ekspor CSV butuh mitigasi tambahan sesuai perilaku spreadsheet target.

## 5. Master: arsip berbeda dari hapus/migrasi

### Fakta sumber

Actual mempunyai hide/unhide kategori; kategori yang disembunyikan tetap memengaruhi budget. Menghapus kategori dengan saldo positif atau transaksi terdahulu memerlukan pemilihan kategori tujuan. [Actual Categories](https://actualbudget.org/docs/budgeting/categories/)

YNAB menyarankan hide bila kategori memiliki aktivitas. Delete kategori terpakai meminta reassignment transaksi dan nilai budget/available; ini dapat mengubah hasil historis. [YNAB Adding, Removing, and Customizing Categories](https://support.ynab.com/en_us/adding-removing-and-customizing-categories-a-guide-HJFO5j909)

### Usulan desain

| Operasi | Pengalaman yang diusulkan |
|---|---|
| Tambah | Form singkat; validasi duplikat nama, tipe, dan ruang template; preview apakah tersedia pada transaksi dan laporan. |
| Ubah budget/kosmetik | Penjelasan scope perubahan dan before/after. |
| Arsip | Hilang dari pilihan transaksi baru; histori dan perhitungan tetap terbaca. Draft/sinkronisasi tertunda tidak boleh rusak diam-diam. |
| Rename kategori terpakai | Analisis referensi dan formula terlebih dahulu; lock awal lebih aman daripada cascade tak teruji. |
| Ubah tipe kategori terpakai | Perlakukan sebagai perubahan semantik finansial, bukan label sederhana. |
| Delete kosong | Periksa juga formula, defined name, laporan, serta budget; jumlah transaksi nol belum membuktikan tidak direferensikan. |
| Merge/delete terpakai | Wizard migrasi dengan target, jumlah transaksi per bulan/tahun, dampak budget/laporan, backup, dan hasil audit. |

Pola produk lain menggunakan model relasional internal; workbook dapat memiliki referensi alamat sel dan string literal di formula. Karena itu hubungan Master Lajur tidak cukup diperlakukan seperti FK transaksi saja. Arsip sendiri juga memerlukan representasi status yang tetap kompatibel dengan workbook dan klien; belum ada keputusan storage di riset ini.

## 6. Budget dan uang makan

### Fakta sumber

Actual memungkinkan editing budget yang dilanjutkan ke kategori berikutnya dengan Enter; navigasi bulan terpisah dari transaksi. Polanya menunjukkan input angka berurutan dapat tetap efisien tanpa modal per kategori. [Actual Tips & Tricks](https://actualbudget.org/docs/getting-started/tips-tricks/)

Excel `NETWORKDAYS.INTL` menghitung jumlah hari kerja utuh berdasarkan rentang, weekend, dan daftar hari libur. Ini adalah fungsi kalender umum, bukan aturan pencairan uang makan perusahaan. [Microsoft NETWORKDAYS.INTL](https://support.microsoft.com/en-us/excel/functions/networkdays-intl-function)

### Usulan desain

- Budget: tabel kategori, target, realisasi, sisa, dan status yang bisa dipahami tanpa warna. Target dapat diedit secara berurutan dengan Enter; total realisasi tidak menjadi field yang dapat diubah.
- Tampilkan scope dengan kata jelas: misalnya `Target Master untuk tahun ini` bila satu angka dipakai semua bulan, atau `Budget Oktober` hanya bila penyimpanan memang per bulan. UI tidak boleh menjanjikan scope yang lebih sempit dari workbook.
- Sebelum commit target, tampilkan nilai lama/baru dan lingkup bulan/tahun terdampak. Target masa lalu boleh berubah hanya jika semantik produk memang demikian.
- Uang makan: kebutuhan yang telah dikonfirmasi adalah **jumlah hari kerja + tarif per hari, per bulan**. Dua input itu dan preview hasil perkalian sudah cukup; kalender/absensi otomatis tidak dibutuhkan.
- Preview menggunakan IDR integer dan diberi label `Estimasi`. Mengubah proyeksi tidak berarti uang telah diterima dan tidak otomatis menambah transaksi pemasukan; realisasi tetap transaksi yang disimpan pengguna.
- Range jumlah hari, izin tarif nol, dan batas perubahan bulan lama harus ditetapkan dari aturan workbook/produk. Tidak mengambil hari libur dari layanan cloud secara otomatis.

Sumber Microsoft di atas hanya membantu membedakan kalender dari jumlah hari input manual. Tidak ada rekomendasi mengganti formula uang makan workbook dengan `NETWORKDAYS.INTL`.

## 7. Aksesibilitas dan mencegah salah klik

### Fakta sumber

WCAG 3.3.4 menyediakan tiga jalur perlindungan: submit dapat dibalik, input diperiksa dan dapat dikoreksi, atau pengguna dapat meninjau/mengonfirmasi sebelum final. Penjelasannya tidak mengharuskan konfirmasi setiap save record biasa. [W3C Error Prevention](https://www.w3.org/WAI/WCAG22/Understanding/error-prevention-legal-financial-data.html)

GOV.UK Design System memasangkan ringkasan error dengan pesan dekat field; ringkasan menautkan field salah dan menerima fokus keyboard. Ini contoh desain form pemerintah, bukan kewajiban mengadopsi semua styling GOV.UK. [GOV.UK Error Summary](https://design-system.service.gov.uk/components/error-summary/)

WCAG 2.5.8 menetapkan minimum target 24×24 CSS px dengan beberapa pengecualian, termasuk spacing. Ukuran visual ikon dan area klik bisa berbeda. [W3C Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum)

### Usulan desain

- CRUD harian menggunakan validasi yang bisa dikoreksi; delete menampilkan transaksi/total yang tepat. Hindari dialog `Yakin?` tanpa konteks.
- Destructive action tidak ditempatkan terlalu rapat dengan Save. Default fokus pada cancel untuk dialog berdampak besar; dialog harus punya judul, penjelasan, dan pengelolaan fokus. [W3C Alert Dialog Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/alertdialog/)
- Tabel mempunyai header dan navigasi fokus konsisten. Jika belum mampu melaksanakan grid ARIA lengkap, tabel semantik dengan tombol Edit + form tetap merupakan pilihan realistis.
- Layar kecil memakai form/drawer yang nyaman dengan target sentuh lebih besar; jangan mengecilkan tabel enam kolom hingga semuanya sulit disentuh.
- Undo yang ditampilkan harus benar-benar didukung server/revision/tombstone. Membatalkan draft berbeda dari membalik delete yang sudah tersimpan; jangan menjanjikan undo permanen dengan state browser semata.

## 8. Keamanan mutasi browser, retry, dan konflik

### Fakta sumber

Cookie otomatis dikirim browser, sehingga endpoint perubahan data perlu mitigasi CSRF. OWASP membahas synchronizer token/custom header, origin checking, dan Fetch Metadata; SameSite biasanya lapisan tambahan, bukan pengganti semua pertahanan. [OWASP CSRF Prevention](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html)

ASP.NET Core menyediakan `IAntiforgery`; request JavaScript dapat mengirim token melalui header yang dikonfigurasi. Penerapan harus mengikuti versi runtime dan jalur endpoint yang benar. [Microsoft Antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

RFC 9110 menjelaskan If-Match memakai strong validator untuk mencegah lost update; kondisi yang tidak cocok tidak boleh menerapkan perubahan. PUT/DELETE bersifat idempotent secara semantik HTTP; POST tidak otomatis demikian. [RFC 9110 §§9.2.2,13.1.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.1)

Stripe menyimpan hasil berdasarkan idempotency key dan memeriksa parameter agar retry key yang sama tidak berubah menjadi operasi berbeda. Ini contoh API produk, bukan spesifikasi wajib atau usulan integrasi pembayaran Stripe. [Stripe Idempotent Requests](https://docs.stripe.com/api/idempotent_requests)

### Usulan desain

- Semua POST/PATCH/DELETE browser memakai sesi cookie yang sah, authorization pemilik workbook, dan antiforgery. Tidak ada perubahan finansial lewat GET.
- Browser tidak menyamar sebagai device Android. Audit perlu membedakan web, Android, dan migrasi.
- Retry create memakai UUID/identitas operasi yang sama dan payload yang sama. Tombol disabled membantu UX tetapi bukan jaminan deduplikasi.
- Konflik revision menampilkan nilai terakhir server dan draft pengguna; beri pilihan memuat ulang atau menerapkan kembali sesudah review. Jangan menggunakan last-write-wins diam-diam.
- Lock workbook dan revision melindungi dua tab browser serta Android yang tersinkronisasi pada saat bersamaan. Token revision/ETag tidak menggantikan serialisasi write file.
- Saat koneksi hilang, draft tetap ada dan status belum tersimpan jelas. Memperluas offline web membutuhkan desain tersendiri; jangan menyatakan offline web lengkap hanya karena Android mendukung outbox.

## 9. Ikon metode pembayaran: dekorasi yang benar secara semantik

### Fakta sumber

BI menjelaskan bahwa aplikasi berbagai penerbit dapat membaca QRIS merchant yang sama; satu QRIS menerima beragam sumber dana/instrumen. Jadi QRIS adalah standar/kanal pembayaran, bukan bank atau dompet yang menyimpan dana pengguna. [BI FAQ QRIS](https://www.bi.go.id/id/publikasi/peraturan/Documents/faq_padg_211819.pdf), [BI QRIS](https://www.bi.go.id/en/fungsi-utama/sistem-pembayaran/ritel/kanal-layanan/QRIS/default.aspx)

ShopeePay menetapkan clear space serta ukuran minimum; pedomannya menyarankan menghindari standalone wallet icon pengganti logo lengkap kecuali canvas sudah memiliki branding Shopee yang memadai. Acceptance mark digunakan untuk menyatakan ShopeePay tersedia sebagai opsi pembayaran. [ShopeePay Brand Guidelines](https://product.shopeepay.co.id/products/brand-guidelines)

Lucide berlisensi ISC, dengan sebagian ikon turunan Feather memakai MIT; notice perlu dipertahankan. Pedoman aksesibilitasnya menyarankan label teks tetap terlihat, ikon dekoratif disembunyikan dari screen reader, dan tombol ikon mempunyai nama aksesibel. [Lucide License](https://lucide.dev/license), [Lucide Accessibility](https://lucide.dev/how-to/accessibility)

### Usulan desain

| Nilai pembayaran | Ikon/presentasi |
|---|---|
| Cash/Tunai | Ikon uang generik + teks asli. |
| Bank generik | Ikon bank; logo bank tertentu hanya jika nama/identitasnya benar. |
| QRIS | Badge/ikon QR + label QRIS; jangan otomatis mengartikan sumber dana sebagai ShopeePay/BCA/dll. |
| Shopee | Pertahankan nama; pastikan apakah artinya marketplace, ShopeePay, atau SPayLater sebelum memilih logo. |
| ShopeePay | Logo resmi jika penggunaan/ukuran/clear space sesuai; fallback wallet generik jika tidak. |
| Tidak dikenal/kosong | Ikon netral atau tanpa ikon; label `Belum diisi` untuk nilai kosong. |

Mapping ikon hanya metadata presentasi dan tidak boleh menulis ulang string Master atau ledger. Simpan aset lokal agar tabel tidak bergantung pada CDN atau mengirim pola pembayaran pengguna ke host aset pihak ketiga. Pedoman brand bukan otomatis lisensi merek untuk semua penggunaan. Riset ini belum menemukan izin BI yang spesifik untuk logo QRIS dekoratif 16–20 px di ledger; ikon QR generik + teks adalah fallback sampai penggunaan resmi dipastikan.

## 10. Pertanyaan dan pekerjaan yang masih terbuka

1. **Budget:** apakah target Master tahunan tetap berlaku untuk semua bulan, atau pengguna menginginkan nilai berbeda per bulan? Model histori baru memerlukan keputusan/ADR, bukan perubahan label UI saja.
2. **Master:** bagaimana representasi stable ID/arsip tetap kompatibel dengan workbook, Android, dropdown Excel, formula literal, ranking, Rekap, dan defined names?
3. **Kapasitas:** penambahan kategori/metode pembayaran harus diuji sampai batas template; apakah ekspansi terkontrol diperlukan atau slot kosong cukup?
4. **Bulk/paste:** apakah batch commit all-or-nothing tersedia? Jika partial, bagaimana row result, retry, dan draft recovery direpresentasikan?
5. **Undo:** apakah restore tombstone/version tersedia atau undo dibatasi pada draft? Jangan menampilkan klaim yang belum memiliki dukungan server.
6. **Uang makan:** berapa batas jumlah hari dan tarif; apakah bulan lama boleh disunting? Input hari+tarif per bulan telah dipilih, sehingga kalender bukan blocker.
7. **Ikon merek:** makna tepat nilai Master perlu dipastikan tanpa menyalin data pribadi ke dokumen; hak/pedoman penggunaan brand masing-masing pemilik juga perlu diperiksa bila logo resmi ditambahkan.
8. **Library:** apakah kebutuhan awal cukup dengan Razor/local JavaScript? AG Grid adalah referensi pola, belum pilihan dependency. Library harus dinilai terhadap keyboard, mobile, footprint, dan lisensi fitur yang benar-benar diperlukan.

## Bibliografi primer — diakses 2026-10-08

- [Actual — Tips & Tricks](https://actualbudget.org/docs/getting-started/tips-tricks/): keyboard, continuation, duplicate, undo sesi.
- [Actual — Categories](https://actualbudget.org/docs/budgeting/categories/): hide/unhide dan merge.
- [Actual — Bulk Actions](https://actualbudget.org/docs/transactions/bulk-editing/): filter, seleksi, dan edit massal; produk juga mencatat batas untuk split transactions.
- [Actual — Importing Transactions](https://actualbudget.org/docs/transactions/importing/): pemetaan CSV, interpretasi tanggal, dan duplikat.
- [YNAB — Categories](https://support.ynab.com/en_us/adding-removing-and-customizing-categories-a-guide-HJFO5j909): hide versus delete/reassignment.
- [W3C APG — Grid](https://www.w3.org/WAI/ARIA/apg/patterns/grid/): keyboard dan fokus editing.
- [W3C APG — Combobox](https://www.w3.org/WAI/ARIA/apg/patterns/combobox/): autocomplete dan pemilihan.
- [W3C APG — Alert Dialog](https://www.w3.org/WAI/ARIA/apg/patterns/alertdialog/): dialog konfirmasi bermakna.
- [W3C WCAG 2.2 — Error Prevention](https://www.w3.org/WAI/WCAG22/Understanding/error-prevention-legal-financial-data.html): reversible/checked/confirmed.
- [W3C WCAG 2.2 — Target Size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum): target minimal dan pengecualian.
- [GOV.UK — Error Summary](https://design-system.service.gov.uk/components/error-summary/): error yang dapat dinavigasi.
- [AG Grid — Cell Editing Validation](https://www.ag-grid.com/javascript-data-grid/cell-editing-validation/): validasi field/baris.
- [AG Grid — Batch Editing](https://www.ag-grid.com/javascript-data-grid/cell-editing-batch/): staging commit/cancel; Enterprise.
- [AG Grid — Clipboard](https://www.ag-grid.com/javascript-data-grid/clipboard/): processing paste.
- [AG Grid — Community vs. Enterprise](https://www.ag-grid.com/javascript-data-grid/community-vs-enterprise/): fitur dan lisensi.
- [Microsoft — NETWORKDAYS.INTL](https://support.microsoft.com/en-us/excel/functions/networkdays-intl-function): perbedaan kalender dan count manual.
- [Microsoft — ASP.NET Core Antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0): token mutasi browser.
- [OWASP — CSRF Prevention](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html): ancaman sesi cookie.
- [OWASP — CSV Injection](https://community.owasp.org/attacks/CSV_Injection): batas aman pertukaran spreadsheet.
- [IETF — RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html): idempotency HTTP dan conditional update.
- [Stripe — Idempotent Requests](https://docs.stripe.com/api/idempotent_requests): contoh deduplikasi retry.
- [BI — FAQ QRIS](https://www.bi.go.id/id/publikasi/peraturan/Documents/faq_padg_211819.pdf): interoperabilitas instrumen/sumber dana; halaman PDF terkadang timeout, ringkasan resmi terindeks tersedia saat riset.
- [BI — QRIS](https://www.bi.go.id/en/fungsi-utama/sistem-pembayaran/ritel/kanal-layanan/QRIS/default.aspx): penjelasan kanal QRIS; sebagian isi situs tidak terambil penuh oleh pembaca web.
- [ShopeePay — Brand Guidelines](https://product.shopeepay.co.id/products/brand-guidelines): logo dan acceptance mark.
- [Lucide — License](https://lucide.dev/license): lisensi ikon generik.
- [Lucide — Accessibility](https://lucide.dev/how-to/accessibility): ikon bersama teks dan nama aksesibel.

**Keterbatasan bukti:** temuan ini berasal dari dokumentasi resmi, bukan uji komparatif kecepatan input pada pengguna Lajur. Ukuran target WCAG adalah kriteria aksesibilitas; belum membuktikan UI tertentu paling cepat. Dokumentasi library/product berubah; cek kembali API dan lisensi saat memilih dependency. Tidak ada akses aset logo atau unduhan media dalam riset ini.

## 11. Temuan repository yang menentukan desain

Pemeriksaan dilakukan terhadap kode dan dokumentasi pada 2026-10-08, setelah commit branding `5e932b1`. Tidak membaca atau menyalin isi transaksi/Master pribadi; acuan struktur berasal dari kontrak dan audit yang telah disanitasi.

| Kondisi saat ini | Dampak untuk implementasi |
|---|---|
| Web adalah viewer baca-saja dengan Razor Pages, JavaScript/CSS lokal dan sesi cookie browser tersendiri. [ADR 0008](../adr/0008-read-only-web-viewer-and-browser-session.md), [WebViewerSetup](../../services/api/src/Lajur.Api/Web/WebViewerSetup.cs). | Mutasi browser memerlukan ADR baru, antiforgery, serta audit Web. Jangan memperluas API Android agar menerima cookie secara global atau melakukan login device Android dari browser. |
| CRUD API/workbook sudah mempunyai UUID transaksi, revision, create idempotent, konflik update/delete, lock per user/tahun, temporary copy + validasi + atomic replace. [Finance contracts](../../services/api/src/Lajur.Application/Finance/FinanceContracts.cs), [workbook store](../../services/api/src/Lajur.Infrastructure/Workbooks/OpenXmlWorkbookStore.cs). | Gunakan ulang batas penulisan tersebut; jangan membuat writer web kedua. Bulk harus memakai satu operasi workbook, bukan rewrite XLSM untuk setiap sel. |
| `ReportRow` belum membawa UUID/revision. Viewer menandai baris dengan `month:line`, berdasarkan posisi hasil baca. [Report contracts](../../services/api/src/Lajur.Application/Reports/FinanceReportContracts.cs), [viewer](../../services/api/src/Lajur.Api/wwwroot/js/viewer.js). | Tambahkan identitas kanonis dari `__AppIndex` ke read model sebelum mengaktifkan edit/delete. Sorting/filtering tidak boleh mengubah target. Baris tanpa indeks memerlukan jalur rekonsiliasi yang eksplisit; GET tidak boleh memigrasikan diam-diam. |
| Finance service saat ini menerima `deviceId` dan mencatat sumber Android. [WorkbookFinanceService](../../services/api/src/Lajur.Infrastructure/Finance/WorkbookFinanceService.cs), [workbook models](../../services/api/src/Lajur.Infrastructure/Workbooks/WorkbookModels.cs). | Generalisasikan konteks penulis secara kecil: user, sumber, identitas actor yang relevan, correlation/operation ID. Jangan mengarang Android device ID untuk browser. |
| A:F berisi tanggal, tipe, pembayaran, kategori, integer IDR, keterangan. Pembayaran boleh kosong; kategori harus cocok dengan tipe. [Excel contract](../architecture/EXCEL_CONTRACT.md), [editor](../../services/api/src/Lajur.Infrastructure/Workbooks/OpenXmlWorkbookEditor.cs). | Enam field tetap. Pembayaran kosong ditampilkan sebagai belum diisi dan tidak menghambat save. Jangan menambah kolom akun, settlement, atau formula input. |
| Master memuat kategori/tipe/budget di A:C, tipe di E, pembayaran di G. Reader membatasi kategori baris 5–81, tipe 5–6, pembayaran 5–24; audit terbaru menjelaskan sebagian batas template sampai baris 86. [Reader](../../services/api/src/Lajur.Infrastructure/Workbooks/OpenXmlWorkbookReader.cs), [audit](WORKBOOK_SOURCE_AUDIT_2026_10_03.md). | Tambah Master harus menyelaraskan batas reader, named ranges, dropdown, serta cakupan laporan. Isi di luar batas tidak boleh tampak berhasil tetapi tidak terbaca. Jangan menghapus seluruh row worksheet karena kolom E/G berada pada row yang sama. |
| Nama kategori dipakai sebagai string literal dalam formula dan sebagian panel memakai referensi alamat Master. Rekap/ranking mempunyai cakupan yang ditentukan template. [Observed structure](WORKBOOK_OBSERVED_STRUCTURE.md), [audit](WORKBOOK_SOURCE_AUDIT_2026_10_03.md). | Nol transaksi belum berarti aman dihapus. Rename/merge memerlukan pemeriksaan formula/laporan; hanya rewrite formula yang dikenali dan disetujui. Penambahan kategori belum selesai jika laporan Excel mengabaikannya. |
| Budget kategori adalah satu target **bulanan** di Master untuk sebuah workbook **tahunan**. [Reader](../../services/api/src/Lajur.Infrastructure/Workbooks/OpenXmlWorkbookReader.cs), [report service](../../services/api/src/Lajur.Infrastructure/Finance/WorkbookFinanceReportService.cs). | Perubahan target mengubah pembanding seluruh bulan tahun tersebut. Budget berbeda per bulan adalah model baru dan perlu ADR; nominal transaksi aktual tetap tidak berubah. |
| Proyeksi mengenali formula uang makan perkalian dua sel literal, misalnya `=$M$10*$M$11`. Reader mengenali pola tertentu, bukan mesin formula umum. [Projection reader](../../services/api/src/Lajur.Infrastructure/Workbooks/OpenXmlWorkbookReader.Projection.cs). | Edit hanya literal jumlah hari dan tarif yang telah dipetakan; pertahankan formula estimasi/aktual/sisa. Formula tidak dikenali membuat panel tidak dapat diedit, dengan alasan jelas. |
| Android menyimpan Master lokal dan mempertahankan transaksi yang belum tersinkron saat refresh. [FinanceRepository](../../apps/android/app/src/main/java/com/lajur/app/data/FinanceRepository.kt), [sync protocol](../architecture/SYNC_PROTOCOL.md). | Web tidak boleh menghilangkan draft/outbox Android. Perubahan Master memerlukan version/lifecycle serta pesan penyelesaian untuk draft yang menggunakan nilai lama. Server tidak bisa mengetahui semua draft pada device offline. |

Open XML menyimpan formula di elemen `f` dan hasil kalkulasi terakhir di `v`; cache bukan bukti formula telah dihitung ulang setelah input berubah. [Microsoft — Working with formulas](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/working-with-formulas). Web harus menghitung preview uang makan dari input yang dikenali dan memperbarui laporan dari baris kanonis. Export tetap meminta recalculation/refresh melalui mekanisme workbook yang ada; jangan menjanjikan kalkulasi semua formula atau PivotTable langsung oleh Open XML.

## 12. Konsep UI yang direkomendasikan

**Tabel bulanan untuk bekerja, panel detail untuk konteks.** Pertahankan rasa workbook melalui tab bulan, enam kolom, pencarian/filter, angka rata kanan, header jelas, dan footer total. Kecepatan datang dari sedikit langkah dan keyboard, bukan kebebasan menulis semua cell Excel.

### Halaman bulan

1. Chrome: tahun + tab Rekap/bulan/Semua/Master sesuai navigasi yang ada.
2. Ringkasan bulan singkat; grafik/proyeksi panjang bisa dibuka saat dibutuhkan agar input terlihat cepat.
3. Baris tambah dengan enam field dan `Simpan & lanjut`. Tombol `Duplikat` mengisi draft dari baris pilihan untuk transaksi berulang.
4. Tabel transaksi dengan aksi Edit/Hapus yang dapat dicapai tanpa hover. Sel boleh membuka editing; unit save tetap seluruh transaksi.
5. Footer membedakan **total hasil filter** dan **total bulan**. Rekap/pivot adalah hasil perhitungan; klik angka membuka daftar sumbernya, bukan mengedit total.

**Contoh input sintetis:** pilih Oktober → tanggal 8 → Pengeluaran → Cash → Konsumsi → ketik `15000` → keterangan `Makan siang` → Ctrl+Enter. UI menampilkan `Rp15.000`, menyimpan sekali, lalu memfokuskan nominal draft berikutnya. Tanggal/tipe dipertahankan; mempertahankan kategori/pembayaran adalah pilihan terlihat. Jika konteks belum lengkap, fokus menuju field wajib pertama.

| Interaksi | Aturan awal |
|---|---|
| Tab / Shift+Tab | Field berikutnya/sebelumnya. |
| Enter saat pilihan terbuka | Pilih item combobox; tidak langsung save. |
| Ctrl+Enter pada editor | Simpan & lanjut, dengan tombol sebagai alternatif. |
| Escape | Tutup pilihan terlebih dahulu; batal draft dengan perlindungan bila ada perubahan. |
| Edit / double-click / F2 | Buka editor transaksi terpilih; F2 hanya berlaku pada grid yang benar-benar mendukung fokus. |
| Duplikat | Draft baru; UUID baru hanya untuk transaksi baru. |

Kategori dicari dengan teks dan disaring menurut tipe. Mengubah tipe meminta pemilihan ulang kategori yang tidak kompatibel. Nilai pembayaran kosong tetap sah. Tanggal hari ini hanya default ketika tab bulan memang cocok; bulan lama memakai tanggal dalam bulan itu yang terlihat/ditentukan pengguna. Ubah tanggal lintas bulan dalam tahun yang sama memberi preview perpindahan; lintas tahun ditolak pada tahap pertama sesuai batas writer.

UI menunjukkan `Belum disimpan → Menyimpan → Tersimpan`, atau error dekat field/konflik. Save pada blur tidak dipakai. Input dipertahankan saat request gagal. Jika filter menyembunyikan hasil save, tampilkan `Tersimpan, di luar filter saat ini` dan aksi melihat baris. Jangan mereset sort/filter/scroll tanpa alasan.

Untuk mobile, editor enam field ditampilkan vertikal, bukan enam input sempit dalam satu row. Tabel tetap untuk membaca dan memilih; form yang sama mengatur validasi. Draft web awal berada dalam memori sesi; penyimpanan finansial permanen di browser bersama bukan default. Sebelum pergantian tahun/sheet/logout, draft berubah perlu diselesaikan atau dibuang dengan jelas.

### Hapus dan pemulihan

Delete satu transaksi memperlihatkan tanggal, kategori, nominal, keterangan, dan bulan tujuan. Delete massal memperlihatkan jumlah dan daftar **baris yang benar-benar dipilih**; tidak menyertakan baris tersembunyi secara diam-diam. Hindari konfirmasi setiap save biasa. Tombol `Batal` hanya membatalkan draft; `Urungkan hapus` baru dapat dijanjikan setelah restore server memiliki audit, identitas, dan revision yang benar.

### Paste sebagai peningkatan berikutnya

Paste TSV dari enam kolom Excel → preview → koreksi → satu commit batch. Default usulan: semua baris valid atau tidak ada yang ditulis. Tampilkan format tanggal/IDR hasil interpretasi, kategori tidak dikenal, baris lintas bulan, dan duplikat potensial. Identitas batch/baris dipertahankan saat retry. Tidak membuat kategori otomatis dari typo hasil paste. Batas jumlah baris ditentukan setelah pengukuran rewrite workbook; bukan paste tanpa batas.

## 13. Master, budget, dan uang makan sebagai input terstruktur

### Master

Panel Master dibagi menjadi Kategori dan Metode pembayaran. Tipe transaksi tetap dua nilai yang didukung laporan. Scope default adalah buku/tahun terpilih, bukan rename/delete seluruh tahun tanpa pemberitahuan.

| Operasi | Rekomendasi implementasi awal |
|---|---|
| Tambah kategori/pembayaran | Form singkat; nama, tipe kategori, budget opsional, ikon opsional. Cek duplikat dan kapasitas/cakupan template; kembali ke draft transaksi tanpa kehilangan isinya. |
| Edit ikon atau tampilan | Aman jika metadata presentasi tidak mengganti nilai kanonis. |
| Arsip / aktifkan kembali | Opsi utama untuk Master yang sudah tidak digunakan; histori tetap terbaca. Desain status harus tetap konsisten dengan pilihan Excel dan Android. |
| Rename / ubah tipe | Bila sudah terpakai atau punya dependensi, jangan menyediakan direct edit bebas. Tampilkan dampak dan jalur migrasi; type change terpakai sebaiknya buat kategori baru. |
| Hapus permanen | Hanya setelah transaksi, budget, formula, defined names, dropdown, dan laporan terbukti tidak bergantung. Jangan cascade delete transaksi. |
| Gabungkan / rename terpakai | Tahap lanjutan: preview target, jumlah transaksi/bulan, formula yang dapat dipatch, kebijakan penggabungan budget, backup, cek ulang revision di bawah lock, lalu audit. Batalkan bila ditemukan formula yang tidak didukung. |

Usulan teknis: lifecycle/icon mapping disimpan sebagai metadata workbook yang eksplisit, dengan versi Master, tanpa mirror ledger/Master finansial ke SQLite. Keputusan lokasi/schema perlu ADR. Stable ID Master membantu metadata, tetapi tidak otomatis memperbaiki referensi literal/alamat Excel. Tahap awal boleh membatasi rename/delete terpakai sambil menyediakan tambah dan arsip yang benar. Pending Android tidak dihapus, dipetakan ke kategori lain, atau dianggap berhasil diam-diam; pengguna memilih resolusi ketika sync mendeteksi Master berubah.

### Budget

Tabel: kategori pengeluaran, target bulanan, realisasi bulan yang dilihat, selisih, serta indikator perubahan draft. Angka aktual hanya baca. Edit beberapa target berturut-turut lalu `Simpan perubahan budget` sebagai satu commit. Preview menampilkan target lama/baru dan total target.

Asumsi usulan awal, karena pilihan budget berbeda per bulan belum dikonfirmasi: **pertahankan satu target bulanan per kategori dalam buku tahunan**. Label contoh `Budget bulanan — buku 2026`; penjelasan perubahan berlaku untuk pembanding Jan–Des 2026. Target kosong (`belum diatur`) berbeda dari nol (`Rp0`); validasi target nonnegatif integer IDR. Tidak menambahkan rollover, envelope, rekening, atau alokasi pendapatan.

### Uang makan — pilihan pengguna sudah dikonfirmasi

Panel bulan mempunyai dua input: `Jumlah hari kerja` dan `Tarif per hari`. Contoh sintetis `22 × Rp35.000 = estimasi Rp770.000`. Preview menunjukkan nilai lama/baru; tarif boleh berbeda antarbulan. Opsi salin bulan sebelumnya harus memperlihatkan nilai sebelum disimpan.

Write hanya ke sel literal yang dipetakan dari formula yang dikenal. Jangan overwrite formula atau mengubah hari berdasarkan kalender otomatis. Hari dan tarif adalah integer nonnegatif dengan pengecekan batas/overflow; ketidaklaziman jumlah hari ditampilkan sebagai peringatan yang didasarkan aturan produk, bukan asumsi hari libur perusahaan. Nilai kosong dan nol perlu dibedakan. Label hasil formula dengan cache teks lama tidak dijadikan nilai aktual; web merender label dari input terstruktur.

Mengubah hari/tarif hanya mengubah **estimasi**, bukan mencairkan uang otomatis. Realisasi uang masuk tetap transaksi Pemasukan yang ditinjau dan disimpan pengguna. Jika kelak ada aksi `Catat pencairan`, aksi itu membuat draft dengan preview dan identitas baru, bukan efek samping save input proyeksi.

## 14. Batas teknis dan penerimaan

- Browser menggunakan cookie yang ada melalui handler/endpoint khusus browser dengan antiforgery dan authorization workbook pemilik. Service aplikasi/writer digunakan bersama Android. Tambahkan sumber audit Web; tidak ada token Android di localStorage atau device palsu.
- Semua row mutations memakai UUID/revision, bukan posisi visual. Pada konflik, tampilkan nilai server dan perubahan pengguna; tidak melakukan overwrite atau merge finansial otomatis. Memuat server tidak boleh menghilangkan salinan draft yang belum diputuskan.
- Create retry menggunakan UUID/content sama. Untuk response update yang terputus, jangan langsung menganggap revision conflict berarti perubahan tidak tersimpan; rekonsiliasi state/operation ID agar user tahu hasil. Operation ID Master/proyeksi/batch perlu semantik retry tersendiri.
- Budget, Master, dan input proyeksi membutuhkan revision/version sesuai domainnya, bukan sekadar revision transaksi. Payload tidak menerima alamat sel/formula bebas dari browser.
- Cache laporan tahunan saat ini berbasis metadata file. Mutasi harus menginvalidasi/menyegarkan snapshot di bawah batas lock yang sama, agar tampilan tidak bergantung pada timestamp/resolusi file. Ringkasan kanonis tidak menambahkan draft yang belum berhasil.
- IDR harus tetap integer tepat. JavaScript `Number` tidak dapat mewakili semua nilai `Int64`; tetapkan batas aman atau representasi string desimal dengan parsing integer yang konsisten. UI tidak boleh membulatkan uang diam-diam.
- Tidak memperlebar A:F. Penambahan/arsip Master yang memengaruhi named ranges/dropdown/laporan adalah migrasi template terkontrol, bukan patch umum seluruh formula. Preservasi VBA, style, tabel, controls, charts, defined names, serta panel kanan tetap wajib.

Penerimaan minimum untuk implementasi:

1. Create/edit/delete web memengaruhi row yang benar setelah sort/filter, memakai identitas kanonis, dan tidak menyentuh akun lain.
2. Retry create setelah response terputus tidak menggandakan transaksi; edit web versus Android menghasilkan konflik yang dapat diselesaikan tanpa kehilangan draft.
3. Cookie browser tidak membuka jalur JWT Android; mutasi tanpa antiforgery ditolak; reset/logout berlaku sebagaimana sesi browser saat ini.
4. XLSM preservation tests untuk setiap mutation baru; negative test formula/proyeksi tidak dikenal dan kapasitas Master mencegah save palsu/korup.
5. Perubahan budget/hari/tarif langsung terlihat di web tanpa cache formula lama; transaksi aktual tidak berubah dan VBA byte-identical.
6. Kategori baru terlihat dalam pilihan dan cakupan laporan workbook yang dijanjikan; delete/rename tidak bypass dependensi; budget kosong/0 dan overflow diuji.
7. Refresh Android mempertahankan outbox; Master usang memiliki pesan/resolusi, bukan kehilangan data.
8. Smoke test manual keyboard dan layar kecil, serta buka/export/recalc/RefreshAll pada Excel untuk perubahan template. Tidak menjadikan suite UI E2E yang flaky sebagai syarat CI v1.

## 15. Urutan pekerjaan yang disarankan

| Tahap | Hasil yang dapat ditinjau |
|---|---|
| Fondasi + CRUD bulanan | ADR mutasi web, actor Web, read model UUID/revision, browser handlers aman, tambah/edit/delete/duplikat, keyboard, form mobile, konflik. Ini prioritas pertama. |
| Input workbook terstruktur | Edit target budget tahunan serta jumlah hari/tarif uang makan per bulan, version/retry dan refresh laporan; hanya literal yang dikenali. |
| Master | Tambah kategori/pembayaran sampai coverage template benar; lifecycle/arsip + kompatibilitas Android; rename/delete terpakai tetap melalui guard/migrasi. |
| Percepatan + kosmetik | Paste batch dengan preview/atomicity, edit massal terbatas, restore bila dipilih, ikon lokal dengan mapping terverifikasi. Ikon generik dapat masuk lebih awal karena tidak mengubah data finansial. |

Riset ini tidak memilih AG Grid/React atau paket lain sebagai dependency. Razor + JavaScript lokal yang ada cukup sebagai titik awal CRUD baris; evaluasi ulang library jika navigasi/range/paste kompleks benar-benar dibutuhkan. Fitur clipboard/batch Enterprise bukan prasyarat membuat UX tersebut sendiri.

**Status akhir riset:** pilihan uang makan sudah dikonfirmasi; budget memakai asumsi kompatibel dengan workbook saat ini sampai pengguna mengubah scope. Fitur runtime belum diimplementasikan dan ADR mutasi web belum dibuat/diadopsi. Dokumen ini menjadi bahan pembahasan dan rencana vertical slice berikutnya.
