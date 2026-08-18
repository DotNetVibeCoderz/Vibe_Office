# VibeDesk

**[English](README.md) · [Bahasa Indonesia](README.id.md)**

Office suite yang di-host sendiri: dokumen, spreadsheet, presentasi, penyimpanan berkas, dan kalender,
lengkap dengan asisten AI yang bisa membaca berkas yang sedang Anda buka. Dibangun di atas .NET 10 dan
Blazor, berjalan sebagai aplikasi web, desktop Windows, atau mobile dari satu UI yang sama.

> Dibuat oleh **Gravicode Studios**, dipimpin oleh **Kang Fadhil**.

![Drive](docs/screenshots/04-drive-light.png)

---

## Lima aplikasi

| | |
|---|---|
| **Drive** | Pusatnya. Semua aplikasi lain menyimpan lewat sini, sehingga permission, sharing, versioning, dan pencatatan aktivitas terjadi di satu tempat saja. Pohon folder, sampah, berbintang, kuota. |
| **Docs** | Editor teks kaya dengan komentar dan saran perubahan yang bisa diterima langsung ke dokumen. |
| **Sheets** | Formula engine sungguhan — ~110 fungsi, referensi antar-sheet, deteksi siklus — plus grafik, pivot table, dan conditional formatting. |
| **Slides** | Enam tema, transisi per slide, catatan pembicara, presenter view, dan grafik hidup yang ditarik dari spreadsheet. |
| **Calendar** | Tampilan bulan, minggu, hari, dan agenda di atas acara berulang, pengingat, peserta, dan kalender bersama. |

Semuanya kolaboratif: komentar dan saran pada dokumen, berbagi lewat email atau tautan dengan peran
viewer / commenter / editor, serta riwayat versi lengkap dengan pemulihan sekali klik.

<table>
<tr>
<td width="50%"><img src="docs/screenshots/02-sheets-light.png" alt="Sheets"></td>
<td width="50%"><img src="docs/screenshots/06-slides-light.png" alt="Slides"></td>
</tr>
<tr>
<td><img src="docs/screenshots/05-docs-light.png" alt="Docs"></td>
<td><img src="docs/screenshots/07-calendar-light.png" alt="Calendar"></td>
</tr>
</table>

---

## Mr Clippy

Panel asisten yang hadir di kelima aplikasi, berpijak pada apa pun yang sedang Anda buka. Ia menyimpan
banyak percakapan, menerima lampiran gambar dan dokumen, serta menampilkan tool apa saja yang benar-benar
ia pakai — bukan meminta Anda percaya begitu saja pada teksnya.

Berjalan di atas **Semantic Kernel** dan mendukung empat penyedia — **OpenAI**, **Anthropic**,
**Google Gemini**, dan **Ollama** — yang bisa dipilih per percakapan. Dua belas kernel function
memungkinkannya mencari di web, membaca halaman, mengunduh berkas, berhitung, mengecek tanggal, dan
membaca berkas Drive Anda sendiri.

Dua keputusan desain yang perlu diketahui:

- `math.calculate` **memakai ulang** formula engine Sheets, bukan implementasi kedua. Jadi hitungan di
  chat berperilaku persis seperti di dalam sel.
- Tool Drive sama sekali tidak punya parameter user id. Setiap pembacaan lewat lapisan permission yang
  sama dengan UI, sehingga asisten hanya bisa melihat apa yang memang sudah bisa Anda buka.

Bisa berjalan sepenuhnya offline lewat Ollama; jalur itu tidak butuh API key sama sekali.

---

## Script dan otomasi

Apps Script, tanpa keharusan satu bahasa. Otomasikan seluruh suite dengan **JavaScript, Python, atau
C#** lewat satu API yang menjangkau Docs, Sheets, Slides, Calendar, dan Drive.

```js
const frame = api.sheets.read(fileId, 'Sales');
const top   = frame.where('amount', '>', 1000).groupBy('region', 'amount', 'sum');

api.sheets.create('Ringkasan wilayah', top);
api.notify('Ringkasan siap');
```

![Editor script](docs/screenshots/10-script-editor.png)

- **Izin per script.** Scope disimpan bersama script-nya, bukan dikirim saat pemanggilan, dan sebuah
  script tidak pernah bisa menjangkau apa pun yang tidak bisa dijangkau pemiliknya. Script khusus
  Sheets selamanya hanya menyentuh Sheets.
- **Trigger.** Saat sebuah event terjadi (`item.created`, `content.saved`, `event.created`, …) atau
  menurut jadwal cron lima kolom yang dihitung di zona waktu penulisnya. Eksekusi latar belakang
  berjalan sebagai pemilik script, bukan sebagai orang yang memicunya.
- **Catatan audit, bukan sekadar kolom status.** Tiap eksekusi merekam versi yang dijalankan,
  keluarannya, dan setiap panggilan API yang dilakukan — termasuk yang ditolak.
- **23 template** untuk Drive, Sheets, Docs, Slides, Calendar, dan API eksternal (kurs, harga emas
  dan komoditas, saham, terjemahan, pencarian, cuaca).
- **CLI** — `vibedesk run laporan.py --scope SheetsRead --input bulan=2026-08` — supaya script bisa
  dijalankan dari terminal atau dari langkah build.

Tiap bahasa disandbox dengan caranya sendiri, dan batasnya disebutkan terus terang: Python berjalan di
IronPython sehingga **NumPy dan pandas tidak bisa dimuat** (`DataFrame` menutup kebutuhan itu di
ketiga bahasa), dan script C# **tidak bisa dihentikan di tengah loop**. Baca
[docs/scripting.md](docs/scripting.md) sebelum membuka penulisan C# untuk orang yang belum Anda
percayai.

---

## Mulai cepat

```bash
git clone <repo ini>
cd VibeDesk
dotnet run --project src/VibeDesk.Web
```

Lalu buka <https://localhost:7181>. Pada jalan pertama aplikasi membuat database SQLite, menjalankan
migration, dan mengisi data contoh — delapan pengguna, pohon folder, dokumen, spreadsheet, deck,
sharing, komentar, dan kalender.

**Login demo** (hanya Development — seeder menolak berjalan di environment lain):

| Email | Peran |
|---|---|
| `fadhil@gravicode.com` | Admin |
| `sari@gravicode.com` | Member |
| `budi@gravicode.com` | Member |

Kata sandi untuk semua akun demo: `VibeDesk#2026`

![Masuk](docs/screenshots/01-login.png)

### Mengaktifkan asisten

Simpan kunci di user secrets, jangan di `appsettings.json`:

```bash
cd src/VibeDesk.Web
dotnet user-secrets set "Assistant:OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "Assistant:Tavily:ApiKey" "tvly-..."   # opsional: pencarian web
```

Atau jalankan [Ollama](https://ollama.com) secara lokal, dan tidak perlu kunci sama sekali.

### Host lainnya

Aplikasi desktop dan mobile adalah klien dari API, jadi jalankan API-nya lebih dulu:

```bash
dotnet run --project src/VibeDesk.Api        # https://localhost:7299 — dokumentasinya di /scalar/v1
dotnet run --project src/VibeDesk.Desktop    # jendela native, Windows/macOS/Linux
dotnet build src/VibeDesk.Mobile -f net10.0-android
```

Arahkan klien ke server lain lewat `VIBEDESK_Api__BaseAddress`. Di emulator Android, mesin host ada di
`10.0.2.2`, bukan `localhost` — host mobile-nya sudah memakai itu sebagai default.

---

## Kebutuhan

.NET 10 SDK (`10.0.400`, dikunci di `global.json`). Tidak ada lagi — database, cache, dan penyimpanan
berkas semuanya default ke implementasi lokal tanpa konfigurasi.

---

## Mengganti backend

Setiap backend adalah perubahan konfigurasi, bukan perubahan kode.

| | Default (dev) | Juga didukung |
|---|---|---|
| **Database** | SQLite | SQL Server, MySQL, PostgreSQL |
| **Cache** | Memori | Redis |
| **Penyimpanan berkas** | Filesystem lokal | Azure Blob, Amazon S3, MinIO |
| **AI** | — | OpenAI, Anthropic, Google Gemini, Ollama |

```jsonc
{
  "Database": { "Provider": "PostgreSql" },
  "Cache":    { "Provider": "Redis" },
  "Storage":  { "Provider": "S3", "EncryptAtRest": true }
}
```

`EncryptAtRest` membungkus backend penyimpanan mana pun yang Anda pilih dengan AES-256-GCM, sehingga
enkripsi tersedia di keempatnya — bukan menjadi properti salah satu saja.

Migration ditempatkan satu assembly per provider database: EF Core menemukan setiap tipe `Migration` di
assembly migration, jadi satu proyek bersama akan membuat keempat provider bertabrakan. Keempatnya
direferensikan oleh host, itulah sebabnya berganti provider tidak perlu build ulang.

---

## Arsitektur

```
VibeDesk.Domain          entity dan enum, tanpa dependensi
VibeDesk.Application     kontrak, DTO, model dokumen, formula engine
VibeDesk.Infrastructure  EF Core, Identity, storage, cache, implementasi service
VibeDesk.Ai              Mr Clippy — Semantic Kernel, provider, kernel function
VibeDesk.Scripting       runtime script, workspace API, trigger, sandbox
VibeDesk.Ui              seluruh UI termasuk halamannya, sebagai Razor class library
VibeDesk.Client          kontrak yang sama, dipenuhi lewat HTTP alih-alih EF Core
VibeDesk.Web             host Blazor Server
VibeDesk.Api             REST + SignalR + gRPC
VibeDesk.Desktop         Photino Blazor Hybrid (Windows/macOS/Linux)
VibeDesk.Mobile          MAUI Blazor (Android)
VibeDesk.Cli             vibedesk — menjalankan dan mengelola script dari terminal
```

UI dijadikan class library agar host web, desktop, dan mobile merender komponen yang sama. Desktop dan
mobile sengaja **tidak** mereferensikan `VibeDesk.Infrastructure` — keduanya bicara ke API, sehingga
dependensi framework ASP.NET Core tidak ikut masuk ke host klien.

Detail lengkap: [docs/architecture.md](docs/architecture.md).

---

## Dokumentasi

| | |
|---|---|
| [Arsitektur](docs/architecture.md) | Lapisan, model penyimpanan, permission, concurrency |
| [Konfigurasi](docs/configuration.md) | Semua setting, dan cara mengganti tiap backend |
| [Aplikasinya](docs/apps.md) | Drive, Docs, Sheets, Slides, Calendar secara rinci |
| [API](docs/api.md) | REST, SignalR, dan gRPC, plus aturan yang perlu diketahui klien |
| [Mr Clippy](docs/assistant.md) | Provider, kernel function, batas keamanan |
| [Script](docs/scripting.md) | Bahasa, workspace API, scope, trigger, dan CLI |
| [Pengembangan](docs/development.md) | Perintah, migration, tes, konvensi |

---

## Tes

```bash
dotnet run --project tests/VibeDesk.Tests
```

`dotnet test` bukan pintu masuknya di sini: xunit.v3 berjalan di atas Microsoft.Testing.Platform, dan
.NET 10 SDK menghapus jalur VSTest untuknya. Proyek tesnya adalah executable — menjalankannya langsung
adalah rute yang andal.

---

## Keamanan

Permission berbasis peran (viewer / commenter / editor / owner) yang diselesaikan dari kepemilikan,
grant langsung, grant folder yang diwarisi, dan link sharing — dalam satu query ber-index. Autentikasi
dua faktor TOTP. Enkripsi at-rest AES-256-GCM opsional. API key hanya disimpan sebagai hash SHA-256.

Meminta item yang tidak boleh Anda lihat mengembalikan *not found*, bukan *forbidden*, sehingga
keberadaan sebuah id tidak pernah bocor. SVG dan HTML yang diunggah tidak pernah disajikan inline,
karena SVG inline dari unggahan adalah XSS tersimpan di origin kita sendiri.

---

## Lisensi

Lihat repositori untuk lisensinya. Dibuat oleh **Gravicode Studios**, dipimpin oleh **Kang Fadhil**.
