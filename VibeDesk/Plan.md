# Plan — VibeDesk

Roadmap. Status harian ada di [Progress.md](Progress.md); file ini yang menjelaskan **urutan dan
alasannya**.

---

## Prinsip yang mengurutkan pekerjaan

1. **Drive lebih dulu, selalu.** Keempat aplikasi lain menyimpan lewat Drive, jadi permission,
   sharing, versioning, dan activity log dibangun sekali di sana. Membangun Docs sebelum Drive berarti
   menulis ulang keempatnya nanti.
2. **Kontrak sebelum implementasi.** `VibeDesk.Application` hanya berisi interface dan DTO, sehingga UI
   bisa dibangun melawan kontrak sementara Infrastructure menyusul.
3. **Satu UI, tiga host.** Setiap komponen masuk `VibeDesk.Ui`. Begitu sebuah komponen ditulis untuk
   satu host saja, ia berhenti bisa dipakai host lain.
4. **Jalankan aplikasinya, jangan percaya build hijau.** Sebagian besar bug yang mahal di proyek ini
   (render mode, checkbox login, kontras dark mode, zona waktu seeder) lolos dari compiler dan hanya
   muncul saat aplikasinya benar-benar dibuka.

---

## Fase 1 — Fondasi ✅

Domain, Application, Infrastructure, empat provider database, empat storage, dua cache, Identity + 2FA,
seeder. Migration dipisah satu assembly per provider karena EF Core menemukan setiap tipe `Migration` di
assembly migration — satu proyek bersama membuat keempatnya bertabrakan.

## Fase 2 — Design system + shell ✅

Token warna "Batik Grid", `AppShell` berbasis CSS grid, ikon inline, tema terang/gelap yang diterapkan
sebelum first paint.

## Fase 3 — Kelima aplikasi ✅

Drive → Sheets → Docs → Slides → Calendar. Sheets didahulukan karena formula engine adalah bagian
paling padat logika dan paling mahal kalau salah arah.

## Fase 4 — Mr Clippy ✅

Semantic Kernel, empat provider, 12 kernel function. Connector Anthropic ditulis sendiri karena SK
tidak menyediakannya.

## Fase 5 — Tes + dokumentasi ✅

63 tes saat fase ini ditutup (formula engine, alamat A1, plugin Clippy); tumbuh jadi 161 setelah
Fase 9. README dwibahasa + halaman `docs/`.

---

## Fase 6 — `VibeDesk.Api` ✅

Ini **blocker** untuk desktop dan mobile: keduanya tidak boleh mereferensikan
`VibeDesk.Infrastructure`, karena Infrastructure membawa `FrameworkReference` ke ASP.NET Core (Identity
`AddDefaultTokenProviders` ada di shared framework itu). Menarik itu ke host klien berarti menyeret
framework web ke dalam aplikasi desktop.

Cakupan:

- Minimal API di atas service domain yang sudah ada — Drive, konten dokumen, komentar, versi,
  permission, kalender, notifikasi, Clippy
- OpenAPI + Scalar untuk permukaan REST
- Autentikasi: JWT bearer untuk sesi, API key (hash SHA-256) untuk integrasi
- **SignalR** — hub co-editing: perubahan konten, presence kursor, komentar
- **gRPC** — permukaan ketiga di atas service yang sama, untuk klien yang butuh streaming ketat

Satu domain service, tiga transport. Kalau logika bocor ke endpoint, ketiganya langsung berbeda perilaku.

**Hasil:** 62 rute REST, hub SignalR (satu grup per item; keanggotaan grup *adalah* batas otorisasi),
dan gRPC dengan `StreamChildren` yang mem-paging internal. Diuji end-to-end — login, konflik revisi
409, item tak terlihat 404 — bukan hanya build hijau. Rinciannya di [Progress.md](Progress.md).

Bonus yang tidak direncanakan tapi wajib: **`VibeDesk.Client`**, satu kelas per interface Application
di atas REST. Tanpa itu desktop dan mobile punya API tapi tidak punya cara memakainya dari UI bersama.

Dan satu celah arsitektur yang ketahuan di sini: kelima halaman aplikasi masih di `VibeDesk.Web`,
artinya desktop dan mobile tidak bisa merendernya. Sudah dipindah ke `VibeDesk.Ui`.

## Fase 7 — Desktop lintas platform ✅ (dengan Photino, bukan Avalonia)

**Perubahan dari spec.** Spec menyebut WPF Blazor Hybrid; diputuskan pindah ke **Avalonia Blazor
Hybrid** supaya desktop jalan di Windows, macOS, dan Linux — bukan Windows saja.

Yang perlu diketahui sebelum mulai, hasil pemeriksaan NuGet (17 Agustus 2026):

| Paket | Versi | Unduhan |
|---|---|---|
| `BlazorWebView.Avalonia` | 11.0.0.1 | ~5.400 |
| `BlazorWebView.Avalonia.Cross` | 11.3.1 | ~930 |
| `Sky.Avalonia.BlazorWebView` | 1.0.0 | ~280 |

Tidak ada BlazorWebView first-party untuk Avalonia. Semuanya paket komunitas berskala kecil dan masih
dipatok ke **Avalonia 11.x**, sementara Avalonia sendiri sudah di **12.1.1**. Artinya jalur ini
membawa risiko nyata terhadap .NET 10 + Avalonia 12.

**Hasilnya: Avalonia tidak dipakai.** `BlazorWebView.Avalonia.Cross` 11.3.1 memang menargetkan
`net9.0` (bisa dikonsumsi proyek net10.0), tapi setelah isi paketnya diperiksa: **tidak ada satu pun
extension `Use*` di seluruh assembly-nya**. Paket inisialisasi platform pendampingnya tidak ada di
NuGet dengan nama apa pun yang bisa ditemukan. Paketnya tidak mandiri, dan menebak API yang tidak
terdokumentasi bukan rekayasa — itu tebakan yang kebetulan compile.

Fallback yang sudah tertulis di atas dipakai: **`Photino.Blazor` 4.0.13**. Mandiri
(`PhotinoBlazorAppBuilder.CreateDefault` → `RootComponents` → `Run`), terdokumentasi, dan mencapai
tujuan yang sebenarnya diminta — desktop Windows/macOS/Linux dari `VibeDesk.Ui` yang sama.

Host-nya tetap shell tipis seperti rencana: satu jendela, satu WebView, root ke `ClientRoot`,
autentikasi dan data lewat `VibeDesk.Api`. Diverifikasi hidup, bukan hanya build.

## Fase 8 — Mobile (MAUI Blazor) ✅ (Android)

Shell tipis yang sama — `ClientRoot` yang identik dengan desktop, jadi keduanya tidak bisa melenceng.
Target Android; iOS/MacCatalyst tinggal satu baris tapi butuh Mac untuk bundling.

Belum: mode offline. `ISyncService` sudah menyediakan delta pull/acknowledge, dan cache lokal SQLite
mengisi antara sinkronisasi — tapi itu pekerjaan tersendiri, bukan bagian dari shell.

## Fase 9 — Script & Automation Engine ✅

Otomasi ala Apps Script, tapi tiga bahasa: **JavaScript (Jint)**, **Python (IronPython)**, dan
**C# (Roslyn Scripting)** di atas satu workspace API yang menjangkau Docs, Sheets, Slides, Calendar,
dan Drive.

Urutan pengerjaannya mengikuti prinsip yang sama seperti fase lain: **hal yang mengatur kode dibuat
sebelum kodenya bisa jalan.** Scope, host allow-list, timeout, dan pencatatan panggilan API ada sejak
runtime pertama dijalankan — bukan ditambahkan setelah fitur terasa enak.

- `VibeDesk.Scripting` — `ScriptHost` (izin + audit), `WorkspaceApi` (satu permukaan untuk ketiga
  bahasa, seluruhnya sinkron), `DataFrame`, tiga runtime, `CSharpGuard`, `ScriptExecutor`
- `VibeDesk.Application/Scripting` — kontrak, `CronSchedule` (5 kolom, sadar DST), daftar event
- `VibeDesk.Infrastructure` — `ScriptService`: CRUD, versi, trigger, run, marketplace
- `VibeDesk.Api` — grup rute `/api/scripts`, `/api/script-runs`, `/api/script-templates`,
  `/api/script-market`
- `VibeDesk.Ui` — halaman `/scripts` (tiga tab) dan `/scripts/{id}` (editor + panel izin/trigger/run)
- `VibeDesk.Cli` — `vibedesk`, satu executable, dependensi hanya pada DTO Application

Keputusan yang dikunci di fase ini:

- **Izin melekat pada script, bukan pada pemanggilan.** Editor, trigger, API, dan CLI memakai grant
  yang sama; tidak ada jalur yang bisa melebarkannya saat jalan.
- **Trigger dipasang lewat dekorator DI** pada `IActivityService` dan `ICollaborationNotifier`. Tidak
  satu pun service fitur diubah untuk mendukung script.
- **Eksekusi latar belakang berjalan sebagai pemilik script**, bukan sebagai pemicunya.
- **`DataFrame` menggantikan pandas**, karena IronPython tidak bisa memuat C-extension — dan
  tersedia di ketiga bahasa, bukan hanya sebagai tambalan untuk Python.
- **Script baru selalu lahir disabled**, termasuk hasil template dan hasil install dari yang
  dibagikan.

Batas yang diterima, dan ditulis di [docs/scripting.md](docs/scripting.md) alih-alih disamarkan:
script C# tidak bisa dihentikan di tengah loop (thread-nya ditinggalkan setelah timeout + 2 detik),
NumPy/pandas tidak bisa dimuat, dan analisis Roslyn adalah pertahanan berlapis — bukan batas VM.

---

## Fase 10 — Kemampuan yang ditanyakan setelah rilis ✅

Dua celah yang muncul dari pertanyaan langsung, bukan dari spec:

**Tool tulis untuk Mr Clippy.** Asisten sebelumnya hanya bisa membaca — 11 kernel function, semuanya
read-only. Sekarang ada `authoring` dengan 10 fungsi: buat/ubah dokumen, spreadsheet, presentasi, dan
kelola folder Drive. Batas yang dipilih dan dikunci: **boleh buat dan ubah, tidak pernah hapus.** Tidak
ada tool trash atau delete sama sekali — bukan dibatasi, memang tidak disediakan — jadi tidak ada
panggilan yang bisa disalahpahami model sampai menghilangkan pekerjaan. Ada saklarnya di
`Assistant:AllowWorkspaceWrites`.

**Impor/ekspor Office.** `.docx`, `.xlsx`, `.pptx` dua arah lewat `VibeDesk.Office` di atas
DocumentFormat.OpenXml. Unggahan Office jadi item yang benar-benar bisa diedit; tiap item bisa
diunduh kembali sebagai berkas Office. Konversinya lossy dan batasnya ditulis apa adanya di
`docs/apps.md`, dan format biner lama (`.doc`/`.xls`/`.ppt`) ditolak dengan sengaja.

Keduanya diuji melawan LLM sungguhan dan berkas OOXML sungguhan, bukan hanya lewat unit test — dan
keduanya membongkar bug yang sudah ada sebelumnya: konteks asisten yang wajib diisi, dan seluruh
unduhan di host web yang ternyata 500.

---

## Fase 11 — Pengetatan ⬜ **berikutnya**

- Closure table menggantikan filter visibilitas berbasis path — filter sekarang tidak ramah index dan
  itu sudah dicatat, bukan ditemukan nanti
- Rate limiting di API
- Redis + PostgreSQL sebagai jalur produksi yang diuji, bukan hanya opsi konfigurasi
- Isolasi proses untuk script C#, yang sekaligus menghapus dua batas di atas

---

## Yang sengaja tidak dikerjakan

- **Sinkronisasi Gmail/Outlook.** Ada di spec, tapi butuh OAuth app terdaftar per tenant; ditunda
  sampai ada deployment nyata yang memintanya.
- **Operational transform penuh untuk co-editing.** Concurrency sekarang berbasis revision counter
  dengan rebase di klien — benar, dan gagal secara kelihatan. OT/CRDT baru sepadan kalau memang ada
  pengguna yang mengetik di paragraf yang sama pada saat bersamaan.
- **Menyusun ulang tool call dari token stream Anthropic.** Menambal JSON parsial lintas event berarti
  tool call salah-parse jadi aksi yang diam-diam keliru. Loop non-streaming lebih baik sampai SDK
  menyediakan akumulator resmi.
