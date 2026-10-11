# Riset registrasi, pemulihan akun, dan login Google

Tanggal: 11 Oktober 2026. Status: usulan untuk ditinjau; belum mengubah aplikasi atau produksi.

## Kesimpulan dan rekomendasi

Ketiganya memungkinkan: daftar username/password, pemulihan tanpa SMTP, dan login Google pada web serta Android. Kesulitan keseluruhan **sedang**, dengan bagian paling penting berupa migrasi metadata autentikasi, pemulihan yang tidak mengambil alih akun orang lain, dan konsistensi sesi dua platform.

Rekomendasi awal: implementasikan registrasi lokal + kode pemulihan + perubahan password, kemudian tambahkan Google sebagai metode login alternatif. Ini menyelesaikan kebutuhan daftar dan lupa password tanpa menunggu Google Console. Google dapat didahulukan jika prioritas utama adalah pengalaman daftar paling singkat dan pengguna bersedia bergantung pada akun Google. Akun lokal tetap berguna bagi instalasi self-hosted dan perangkat yang tidak mendukung layanan Google.

SMTP bukan syarat untuk registrasi atau login Google. Untuk akun lokal, pemulihan tetap memerlukan bukti kepemilikan: kode yang sudah disimpan sebelumnya atau bantuan operator dengan verifikasi terpisah. Username saja tidak cukup.

## Kondisi Lajur saat ini

Temuan kode dan spesifikasi internal:

- `UserAccount` mempunyai UUID dan `PasswordHash` wajib, belum mempunyai email, identitas eksternal, atau kode pemulihan.
- Auth memakai SQLite untuk akun/perangkat/sesi. Keuangan tetap XLSM milik UUID pengguna, bukan email atau nama Google.
- `SqliteAuthService.CreateAsync` dan `ResetPasswordAsync` sudah melayani operator. Reset mencabut refresh session; cookie browser ditolak melalui stamp yang diturunkan dari hash password.
- `AuthDatabaseInitializer.InitializeAsync` memakai `EnsureCreatedAsync`, belum memiliki mekanisme peningkatan schema yang berversi.
- Browser menggunakan cookie dan antiforgery; Android menggunakan access/refresh token dengan satu perangkat aktif. Login browser harus tetap tidak mengeluarkan Android.
- Spec `docs/architecture/AUTH_SECURITY.md` saat ini menerima reset operator untuk v1. Registrasi publik/recovery/Google merupakan perluasan perilaku yang perlu dicatat dalam spec dan ADR saat disetujui.

Mengubah model EF saja tidak menaikkan schema SQLite produksi. `EnsureCreated` tidak menginisialisasi schema jika tabel sudah ada dan bukan pasangan yang cocok untuk alur migrations. Perlu strategi upgrade eksplisit dari database existing; jangan menghapus auth database untuk membuat schema baru. [Microsoft EF Core: EnsureCreated](https://learn.microsoft.com/en-us/ef/core/managing-schemas/ensure-created)

## Pilihan pemulihan tanpa SMTP

| Pilihan | Pengalaman pengguna | Kesulitan relatif | Keterbatasan |
| --- | --- | --- | --- |
| Kode pemulihan sekali pakai | Simpan kode ketika daftar; masukkan username + kode ketika lupa password | Sedang | Jika semua kode hilang dan tidak ada metode lain, perlu operator |
| Login Google yang sudah ditautkan | Masuk dengan Google, lalu atur ulang password lokal setelah autentikasi ulang | Sedang–tinggi | Tidak membantu akun lokal yang belum pernah menautkan Google |
| Reset operator | Operator verifikasi pemilik di luar aplikasi, lalu reset melalui CLI | Rendah untuk pengguna sedikit | Tidak cocok sebagai pemulihan massal; jangan jadikan API anonim |
| Email melalui API penyedia email | Klik tautan dari email, tanpa menjalankan SMTP sendiri | Sedang + layanan eksternal | Tetap membutuhkan penyedia, domain pengirim, dan operasional email |
| Pertanyaan keamanan | Jawab pertanyaan pribadi | Tidak direkomendasikan | Jawaban dapat ditebak atau dicari |

Usulan UX kode: setelah daftar, tampilkan beberapa kode acak dalam dialog khusus, dengan unduh/copy dan penjelasan singkat untuk menyimpan di password manager. Tampilkan plaintext sekali; simpan hanya hash server. Jangan menyimpan satu-satunya salinan di APK/perangkat yang mungkin hilang. Regenerasi harus memerlukan autentikasi ulang dan membatalkan kode lama.

Endpoint recovery perlu jawaban gagal generik, pembatasan percobaan, kode acak berkekuatan cukup, konsumsi atomik sekali pakai, dan pencabutan seluruh sesi setelah sukses. Kode yang disimpan pengguna sebelumnya dapat digunakan tanpa pengiriman email, tetapi backend tetap memvalidasinya secara online. Pertanyaan keamanan tidak boleh menjadi satu-satunya bukti pemulihan. [OWASP Forgot Password](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)

**Ubah password saat masih login berbeda dari lupa password.** Yang pertama dapat meminta password saat ini atau reautentikasi Google yang sudah ditautkan. Yang kedua membutuhkan kode pemulihan atau bukti eksternal yang sudah ditetapkan sebelumnya. Jangan menyalin kemampuan CLI reset berdasarkan username menjadi endpoint publik.

## Google pada web, Android, dan backend

Arsitektur yang diusulkan tidak membutuhkan Firebase Auth: web/Android mengambil Google ID token, API memvalidasinya, kemudian API menerbitkan sesi Lajur yang sudah ada. Google hanya menjadi penyedia identitas; buku keuangan tetap berada di VPS.

### Android

Gunakan Credential Manager dengan `googleid`, bukan memulai integrasi baru dengan API Google Sign-In Android lama. Sediakan tombol eksplisit; tangani pengguna membatalkan, tidak ada kredensial, dan kegagalan jaringan. ID token dikirim ke API lewat HTTPS. `setServerClientId` menggunakan **Web client ID**, bukan Android client ID. Nonce perlu dicocokkan di backend; keluar aplikasi juga membersihkan credential state provider. [Android: implementasi Sign in with Google](https://developer.android.com/identity/sign-in/credential-manager-siwg-implementation)

Google Console membutuhkan client Android yang cocok dengan application/package ID dan SHA-1 sertifikat APK. Untuk APK yang diunduh dari GitHub, pakai fingerprint sertifikat **release APK tersebut**, bukan debug key. Tambahkan client debug terpisah untuk pengujian. Jika nantinya memakai Play App Signing, konfigurasi sertifikat APK yang benar-benar didistribusikan Play juga perlu dicocokkan. Web client dalam project yang sama menyediakan audience backend. [Google Android codelab: konfigurasi client](https://codelabs.developers.google.com/sign-in-with-google-android)

Pemilihan versi library stabil dan dukungan minimum Android harus diverifikasi saat implementasi; contoh dokumentasi/codelab bukan alasan otomatis menaikkan minimum SDK Lajur. Google login pertama/reautentikasi memerlukan koneksi. Setelah login, entri offline, app lock, outbox, dan refresh Lajur tetap berjalan sesuai arsitektur existing.

### Web

Gunakan Google Identity Services (GIS). Daftarkan authorized JavaScript origin `https://lajur.solusi-program.site` tanpa path. Redirect URI hanya diperlukan bila memilih mode redirect; mode callback JavaScript tidak membutuhkan redirect endpoint Google. GIS dimuat dari script resmi Google; CSP existing Lajur perlu penyesuaian terbatas untuk script/frame/connect/style yang dibutuhkan. [Google GIS setup](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid)

Sesi browser tetap cookie Lajur. Login/link Google harus mempunyai perlindungan login-CSRF/nonce yang sesuai mode integrasi, bukan hanya menerima token dari halaman mana pun. Untuk POST GIS langsung, validasi double-submit `g_csrf_token`. Verifikasi backend wajib meliputi signature, issuer, audience allowlist, expiry; jangan mempercayai email/profile dari request client. Identitas eksternal disimpan berdasarkan `(provider, sub)`, karena email dapat berubah. Penautan akun lama dilakukan setelah pengguna membuktikan akses akun existing, bukan auto-link karena email terlihat sama. [Google: verifikasi ID token](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token)

### .NET

Library resmi `Google.Apis.Auth` mempunyai `GoogleJsonWebSignature.ValidateAsync` dengan `ValidationSettings`; tetapkan audience eksplisit. Library mengelola cache sertifikat Google. Tambahkan validasi nonce/challenge milik Lajur, batas ukuran request, rate limiting, serta pengujian token tidak sah. Jangan logging ID token. API kemudian memakai UUID Lajur yang sudah dipetakan untuk menerbitkan cookie atau token Lajur. [Referensi Google .NET](https://docs.cloud.google.com/dotnet/docs/reference/Google.Apis/latest/Google.Apis.Auth.GoogleJsonWebSignature)

## Google Console, scope, dan verifikasi

Minimal scope identitas saja: `openid`, `email`, `profile`; tidak perlu izin Drive/Gmail atau akses workbook Google. Scope identitas dasar termasuk non-sensitive. Ini memisahkan login dari pemberian izin API lain. [Google GIS setup](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid)

Jangan menyatakan semua aplikasi mode Testing selalu dibatasi tester/7 hari: Google mendokumentasikan pengecualian untuk Sign in with Google atau subset scope identitas dasar. Bila meminta scope tambahan, pengecualian tersebut tidak berlaku. Kebijakan organisasi Google Workspace juga bisa menghalangi login. [Google: audience dan publishing status](https://support.google.com/cloud/answer/15549945)

Verifikasi brand berbeda dari pemeriksaan scope sensitive/restricted. Aplikasi External produksi yang ingin menampilkan nama/logo mungkin memerlukan verifikasi brand, domain ownership melalui Search Console, homepage publik, dan privacy policy. Domain yang didaftarkan ialah `solusi-program.site`; homepage/privacy dapat berada pada subdomain Lajur. Dokumentasi menyebut review otomatis bisa singkat, manual biasanya 2–3 hari kerja; itu bukan janji waktu untuk project ini. Tidak memakai scope sensitif mengurangi proses review, tetapi tidak menjamin bebas seluruh verifikasi. [Google: brand verification](https://developers.google.com/identity/protocols/oauth2/production-readiness/brand-verification)

Client ID dan fingerprint sertifikat adalah konfigurasi publik. Tidak perlu mengirim private signing key ke Google; fingerprint dapat diambil dari APK publik. Untuk rancangan verifikasi ID token ini, client secret tidak perlu ditaruh di Android atau browser. Penyiapan Google Console dan persetujuan pengguna memerlukan keterlibatan pemilik akun.

## Rancangan data dan migrasi yang perlu diputuskan

Usulan desain, bukan fakta tentang perilaku existing:

1. Pertahankan UUID akun existing dan seluruh path workbook. Tambahkan metadata provider identity dengan unique `(provider, subject)` dan unique username sesuai kebijakan existing.
2. Pisahkan security stamp random dari hash password. Akun Google-only boleh tidak mempunyai password; jangan memberikan password dummy yang dapat dipakai login.
3. Tambahkan metadata hash recovery code, waktu konsumsi, serta challenge nonce/expiry. Tidak ada ledger transaksi di SQLite.
4. Upgrade auth schema berversi dan test terhadap salinan sintetis schema lama sebelum deploy. Pertimbangkan bahwa image rollback tidak otomatis membalik migrasi database; tetapkan kompatibilitas atau rollback terkontrol.
5. Penautan Google untuk `quenz`: login existing → autentikasi ulang password → pilih Google → konfirmasi penautan. Login Google pertama yang belum ditautkan membuat akun terpisah; jangan memindahkan workbook otomatis.
6. Pembatalan tautan tidak boleh menghapus satu-satunya cara masuk tanpa terlebih dahulu menambahkan metode alternatif. Perubahan kredensial/recovery mengubah stamp dan mencabut sesi yang sesuai.
7. Public registration configurable: `closed`, `invite`, atau `open`. Untuk VPS pribadi, mulai dengan invite; sediakan limit akun/percobaan agar registrasi tidak bebas menghabiskan storage. Pada open mode tetap perlu rate limit dan pembatasan provisioning workbook.
8. Email yang diketik pada registrasi lokal tidak diberi status verified tanpa pembuktian. Jangan tampilkan tombol email reset yang belum benar-benar bisa mengirim dan memvalidasi tautan.
9. Audit template onboarding sebelum registrasi pengguna lain diaktifkan. Template produksi disebut clean V4 karena ledger transaksi dibersihkan, sementara Master/config tetap dipertahankan. Itu belum membuktikan seluruh budget, gaji/proyeksi, label Master, dan metadata telah menjadi default netral. Jangan membagikan konfigurasi pribadi pemilik kepada akun baru. Riset ini tidak membaca nilai template privat; sanitasi/verifikasi default merupakan syarat penerimaan implementasi.

## Ukuran pekerjaan dan urutan

Estimasi berikut adalah perkiraan engineering kasar untuk implementasi, pengujian, UI web + Android, dan dokumentasi; bukan SLA. Satu hari efektif kira-kira 6–8 jam fokus. Rentang bisa bertambah setelah desain disepakati atau temuan migrasi. Google Console/verifikasi eksternal tidak termasuk.

| Irisan | Kesulitan | Estimasi kasar | Alasan |
| --- | --- | --- | --- |
| Register lokal dengan invite/config, onboarding, limit dan UI dua platform | Sedang | 1–2 hari | Create akun sudah ada, endpoint publik/provisioning dan UI belum ada |
| Ubah password + recovery code + migrasi/versioning dan invalidasi sesi | Sedang | 2–3 hari | Keamanan, konsumsi kode atomik, schema existing dan dua jenis sesi |
| Google backend + web + Android + penautan akun existing | Sedang–tinggi | 2–4 hari tambahan | Tiga integrasi, nullable password/stamp, konfigurasi release dan testing nyata |
| Reset email dengan provider API setelah penyedia ditentukan | Sedang | 1–2 hari tambahan | Verifikasi email, token delivery/expiry, konfigurasi domain dan failure handling |

Urutan rekomendasi:

1. Sepakati registrasi invite/open dan UX recovery; buat ADR/schema upgrade serta test migrasi.
2. Implementasikan register lokal, ubah password, dan recovery code sebagai satu alur lengkap. Akun existing dapat menghasilkan kode setelah reautentikasi.
3. Tambahkan Google; persiapkan homepage/privacy dan Console sembari kode dikerjakan. Uji APK release bertanda tangan yang diunduh dari GitHub dan browser produksi.
4. Email reset tetap opsional jika nanti ada kebutuhan pengguna lebih luas. Backup offsite tetap ditunda sesuai permintaan pengguna; snapshot pra-deploy untuk perubahan schema tetap bagian operasional migrasi.

Penerimaan minimum: username duplikat race-safe; akun baru terisolasi; kode salah/terpakai/bersamaan ditolak; reset mencabut sesi browser dan Android; akun disabled ditolak pada seluruh metode; token Google wrong-audience/issuer/expiry/nonce ditolak; penautan tidak dapat mengambil alih akun; UUID/workbook akun lama tetap; cancel Google tidak menghapus sesi/outbox; migrasi existing dan kegagalan upgrade teruji.
