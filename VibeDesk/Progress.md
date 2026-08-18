# VibeDesk — Progress

Tracking dokumen pengembangan. Sumber kebenaran fitur: `requirements.md`.

**Terakhir diperbarui:** 2026-08-18

---

## Status ringkas

| Layer | Status | Catatan |
| --- | --- | --- |
| Solution + build infra | ✅ Selesai | `global.json` (pin SDK 10.0.400), `Directory.Build.props`, central package management |
| `VibeDesk.Domain` | ✅ Build sukses | Entity lengkap: Drive, versi, permission, komentar, kalender, chat, platform |
| `VibeDesk.Application` | ✅ Build sukses | Abstraksi storage/cache/user, model konten Docs/Sheets/Slides, **formula engine penuh**, DTO Drive |
| `VibeDesk.Infrastructure` | ✅ Build sukses | Identity, DbContext, 4 provider DB, 4 storage, 2 cache, **semua service**, seeder, DI |
| `VibeDesk.Migrations.*` (4) | ✅ Migration jadi | Satu assembly per provider; SQLite sudah diverifikasi apply → 27 tabel |
| `VibeDesk.Ai` (Mr Clippy) | ✅ Build sukses | Semantic Kernel + 4 provider, 21 kernel function (11 baca + 10 tulis), sesi & lampiran tersimpan |
| `VibeDesk.Tests` | ✅ 254/254 lulus | Formula engine, alamat A1, plugin Clippy, cron, DataFrame, sandbox C#, parser CLI, dynamic array, konverter Office |
| `VibeDesk.Ui` (Razor lib) | ✅ Build sukses | Design system, shell, kelima aplikasi, panel Clippy, markdown |
| `VibeDesk.Api` | ✅ **JALAN** | 62 rute REST + OpenAPI/Scalar, hub SignalR, gRPC; diuji end-to-end |
| `VibeDesk.Client` | ✅ Build sukses | Kontrak Application di atas HTTP, sesi + sign-in bersama |
| `VibeDesk.Web` (Blazor Server) | ✅ **JALAN** | Auth + 2FA + **kelima aplikasi** (Drive, Docs, Sheets, Slides, Calendar) |
| `VibeDesk.Desktop` (Photino Hybrid) | ✅ **JALAN** | Jendela native lintas platform; Avalonia gagal, lihat di bawah |
| `VibeDesk.Mobile` (MAUI Blazor) | ✅ Build sukses | Target Android |
| `VibeDesk.Scripting` | ✅ **JALAN** | 3 runtime (Jint/IronPython/Roslyn), workspace API, scope, trigger, 23 template |
| `VibeDesk.Office` | ✅ **JALAN** | Impor/ekspor .docx/.xlsx/.pptx; ketiganya lolos OpenXmlValidator |
| `VibeDesk.Cli` (`vibedesk`) | ✅ **JALAN** | login/list/run/check/push/pull/enable/logs/templates; diuji end-to-end |
| Docs / README (EN+ID) | ✅ Selesai | README dwibahasa + 6 halaman `docs/` + screenshot |
| `Plan.md` | ✅ Selesai | Roadmap; Fase 7 mencatat pindahnya desktop ke Avalonia |

---

## Keputusan arsitektur yang sudah dikunci

1. **Drive sebagai hub.** Docs/Sheets/Slides bukan pohon terpisah — semuanya `DriveItem`
   dengan payload di `DriveItemContent`. Konsekuensinya sharing, trash, pencarian, dan version
   history ditulis **sekali** dan berlaku untuk semua aplikasi.
2. **Metadata dipisah dari payload (1:1).** Listing folder tidak pernah menarik isi dokumen.
3. **Payload = JSON, bukan tabel ternormalisasi.** Spreadsheet 10.000 sel = 1 baris DB, bukan
   10.000. Ini pembeda utama performa editor.
4. **Permission diwarisi lewat `DriveItem.Path`.** Materialised path (`/{guid}/{guid}/`) →
   resolusi hak akses = satu query `LIKE` ber-index, bukan rekursi.
5. **Provider di belakang interface.** `IStorageProvider`, `ICacheService` dipilih dari
   appsettings; kode fitur tidak pernah menyentuh SDK cloud langsung.
6. **Formula engine demand-driven + memoisasi.** Bukan graph dependensi pre-built; deteksi siklus
   via set "in progress" → `#CIRCULAR!` alih-alih stack overflow.

---

## Yang sudah bisa dipakai: Formula Engine

Selesai dan lolos build — komponen paling berisiko, jadi dikerjakan lebih dulu.

- **Addressing** (`CellAddress.cs`): A1 ⇄ index, marker absolut `$`, range, prefix sheet
  (`'Sheet 2'!A1:C9`).
- **Tokenizer + parser** (`FormulaParser.cs`): recursive descent, presedensi lengkap
  (comparison → `&` → `+-` → `*/` → `^` kanan-asosiatif → unary → postfix `%`),
  literal string ber-escape `""`, notasi ilmiah, argumen kosong (`IF(A1,,"no")`).
- **Value model** (`FormulaValue.cs`): koersi ala spreadsheet (blank=0, `"12%"`→0.12),
  ordering perbandingan number→text→boolean, 7 nilai error termasuk `#CIRCULAR!`.
- **~110 fungsi** (`FormulaFunctions.cs`): matematika/statistik, logika, `SUMIF(S)`/`COUNTIF(S)`/
  `AVERAGEIF(S)` dengan operator & wildcard, teks, tanggal (serial number Excel-compatible),
  lookup (`VLOOKUP`/`HLOOKUP`/`INDEX`/`MATCH`/`XLOOKUP`).
- **Fitur turunan** (`FormulaEngine.cs`): pivot table (group/filter/5 agregat),
  conditional formatting (termasuk color scale interpolasi), resolusi data chart.

---

## Yang sudah selesai di Application (kontrak)

- `Drive/DriveDtos.cs` — `DriveItemDto` (routing/ikon/hak akses turunan), `DriveQuery`,
  `PagedResult<T>`, `StorageUsageDto`, `PermissionDto`, `VersionDto`, `CommentDto`,
  `SaveContentResult` (membawa sinyal rebase untuk save kolaboratif).
- `Drive/IDriveService.cs` — `IDriveService`, `IDocumentContentService`, `IPermissionService`,
  `IVersionService`, `ICommentService`.
- `Calendars/CalendarContracts.cs` — `ICalendarService` + DTO; rekurensi di-expand di service
  (`GetOccurrencesAsync`) sehingga UI tidak perlu tahu soal RRULE.
- `Platform/PlatformContracts.cs` — `IUserDirectory`, `INotificationService`, `IActivityService`,
  `ISyncService`, `IApiKeyService`, `ICollaborationNotifier`.

## Yang sudah selesai di Infrastructure

- **Identity** — `AppUser : IdentityUser<Guid>` (2FA/TOTP/lockout dari ASP.NET Core Identity,
  tidak ditulis ulang), quota, locale, preferensi tema.
- **`AppDbContext`** — seluruh entity + index yang menopang query nyata (children-of-folder,
  my-drive, path prefix, event window). Divergensi provider ditangani di satu tempat:
  SQL Server pakai `rowversion` native, tiga lainnya token yang di-stamp di `SaveChanges`;
  SQLite pakai converter `DateTimeOffset` → UTC ticks agar ordering/range query tetap benar.
- **`DatabaseRegistration`** — pemilihan 4 provider dari config, fallback connection string
  berlapis sampai SQLite lokal sehingga clone baru langsung jalan.
- **Storage** — `FileSystemStorageProvider` (write-temp-then-move + guard path traversal),
  `EncryptingStorageProvider` (AES-256-GCM, magic prefix agar file lama tetap terbaca),
  `AzureBlobStorageProvider` (SAS bila tersedia), `S3StorageProvider`.
- **Cache** — `MemoryCacheService` (tag index + per-key lock anti cache-stampede),
  `RedisCacheService` (tag = Redis set, bukan `KEYS pattern` yang memblokir server).
- **`PermissionService`** — role efektif = max(ownership, grant langsung, grant ancestor via
  materialised path, link sharing) dalam satu query ber-index. `RequireAsync` tidak membedakan
  "tidak ada" vs "tidak boleh" agar keberadaan id tidak bocor.
- **`DriveService`** — listing/search, folder, dokumen, upload (rollback blob bila quota
  terlampaui), rename dengan penomoran duplikat, move dengan guard subtree + rewrite path satu
  `ExecuteUpdate`, deep copy, trash/restore subtree, delete permanen (bottom-up + purge blob),
  quota, breadcrumb.
- **Platform** — `UserDirectory` (batch lookup anti N+1), `NotificationService`,
  `ActivityService` (gagal log tidak menggagalkan operasi).

### Service Infrastructure (lanjutan)

- **`DocumentContentService`** — konkurensi berbasis revision counter, bukan last-write-wins:
  save dengan `baseRevision` basi ditolak beserta salinan server agar klien bisa rebase.
  `baseRevision = -1` = paksa tulis (restore/AI/import). Auto-snapshot ter-throttle 10 menit.
  Ekstraksi plain-text per tipe (HTML / nilai sel / teks elemen slide) untuk search + grounding AI.
- **`VersionService`** — restore men-snapshot state saat ini lebih dulu, jadi rollback pun bisa
  di-undo. Prune auto-snapshot di atas 50 (beserta blob-nya), snapshot berlabel user tidak dihapus.
- **`CommentService`** — Commenter bisa mengusulkan tapi tidak mengubah; menerima suggestion butuh
  Editor. Penerapan per tipe: replace occurrence pertama di HTML (bukan semua), tulis sel untuk
  Sheets (`=` → formula), ganti teks elemen untuk Slides. Mention via marker `@[guid]`.
- **`CalendarService`** — rekurensi di-expand saat baca; hanya *exception* (occurrence yang diubah
  atau dibatalkan) yang dimaterialisasi, sehingga "setiap Senin selamanya" tidak jadi baris tak
  terbatas. Weekly by-day menghitung interval per pekan, bukan per hari. Import Sheets → Calendar,
  free/busy (judul disembunyikan untuk event privat), sweeper reminder idempoten.
- **`ApiKeyService`** — hanya hash SHA-256 disimpan, plaintext ditampilkan sekali.
- **`SyncService`** — cursor high-water-mark, tidak pernah maju ke "now" agar tak ada delta hilang.
- **`SampleDataSeeder`** — 8 user demo, folder tree, 3 dokumen, 3 spreadsheet (model revenue
  berformula, timeline siap-import, data penjualan + pivot + color scale), 2 deck, sharing dengan
  ketiga role + link share, thread komentar + suggestion, 2 kalender + event berulang + attachment.
  Idempoten dan **menolak jalan di luar Development** (password demo diketahui publik).

## Catatan teknis yang perlu ditindaklanjuti

- `DriveService.ApplyVisibility` memakai `x.Path.Contains(p.DriveItemId.ToString())` untuk
  pewarisan akses. Benar secara fungsional tapi **tidak index-friendly**. Kalau jumlah item besar,
  ganti dengan tabel penutup (closure table) atau materialisasi daftar folder yang dishare ke
  user lalu prefix-match per folder.
- Infrastructure memakai `FrameworkReference Microsoft.AspNetCore.App` (dibutuhkan token provider
  Identity untuk TOTP). Konsekuensinya **Desktop/Mobile tidak boleh mereferensikan Infrastructure** —
  keduanya harus lewat `VibeDesk.Api`. Ini memang rencananya, tapi jangan sampai tertukar.
- Migration keempat provider di-generate dari model yang sama tapi **hanya SQLite yang diverifikasi
  apply**. SqlServer/MySql/PostgreSql perlu diuji terhadap server sungguhan.

## Web & editor — yang sudah jadi

### Arah desain "Batik Grid"
Keberanian desain hanya di chrome; canvas dokumen dibiarkan tenang — ini alat 8 jam/hari, dan
dokumen itu milik user. Palet dari dua pewarna batik Jawa: wedelan (teal `#0f5c57`) untuk aksi
primer, soga (emas `#b47b26`) untuk state aktif. Signature: **garis canting** — rule emas 2px
penanda item aktif, dipakai sama di nav, sel, tab sheet, notifikasi belum dibaca. Tidak vendor font
eksternal: alat kerja harus melukis di frame pertama, dan font-swap di spreadsheet me-reflow ribuan
sel. Personality dibawa sistem label mono uppercase.

### Sheets (terverifikasi jalan)
Formula engine terhubung end-to-end: `Jan Revenue = 4000` = `320 × 12,5` (persis `=B2*C2`), baris
Total `SUM`/`AVERAGE` = `97.631,00`, conditional formatting menyalakan baris Growth > 15%.
`SheetGrid` memakai CSS-grid di atas div, bukan `<table>` — virtualisasi butuh elemen spacer, dan
spacer di dalam `tbody` adalah markup tak valid. Baris divirtualisasi: 200×26 = 5.200 sel, dan
merender semuanya lewat circuit SignalR membuat tiap ketikan men-diff seluruh grid.
`ChartView` = SVG inline tanpa library (7 tipe, tooltip per mark, legend, toggle table view).
Chart & pivot ditaruh **di bawah** grid, bukan mengapung di atasnya: kartu yang menutupi sel yang ia
rangkum menyembunyikan hal yang sedang diperiksa user.

**Palet chart:** palet brand gagal validasi berulang (teal tidak mencapai chroma floor pada
lightness slot kategorikal di sRGB). Seri data karena itu memakai palet referensi tervalidasi,
chrome tetap warna brand — keterbacaan data mengalahkan kontinuitas palet. Tiga slot light di bawah
3:1 terhadap surface → itulah sebabnya tiap chart selalu punya legend **dan** table view.

### Docs (terverifikasi jalan)
`RichTextEditor` — **Blazor tidak boleh pernah me-render ulang isi contenteditable.** Menetapkan
`innerHTML` mengempaskan caret ke awal, jadi render dari toast/sidebar/presence akan mencabut kursor
user di tengah kalimat. Markup-nya tanpa child dinamis: isi masuk sekali lewat JS pada render
pertama dan pada reload eksplisit (parameter `Revision`, dinaikkan **hanya** saat body harus diganti
seluruhnya — restore, rebase, suggestion diterapkan), keluar lewat JS saat disimpan.
`CommentSidebar` dipakai bersama ketiga editor; berkomentar butuh Commenter, menerapkan suggestion
butuh Editor. Anchor komentar menyimpan **kutipan teks**, bukan offset karakter — offset bergeser
tiap edit, kutipan tetap memberi tahu komentar itu tentang apa.

### Bug nyata yang hanya ketemu dari menjalankan & memotret aplikasinya
1. `AddRazorSupportForMvc=true` **mematikan kompilasi komponen** di RCL.
2. `@rendermode` di *layout* menjadikannya batas interaktif; `Body` (`RenderFragment`) tak bisa
   menyeberang → 500. Mode render harus diputuskan per-request di `<Routes>`.
3. `[FromForm] bool` untuk checkbox → **login 400** bagi yang tidak mencentang "Keep me signed in".
4. `SectionContent` tanpa using-nya membuat toolbar & judul **diam-diam tidak render**.
5. `Virtualize` tanpa `@using ...Web.Virtualization` dianggap elemen markup biasa; grid kosong.
6. **Kontras tema gelap**: style sel menyimpan warna latar tetap sementara teks memakai token tema →
   putih di atas putih. Diperbaiki dengan menghitung ink dari luminans relatif WCAG.

### Catatan tooling
Driver screenshot CDP (`shot.mjs` di scratchpad) sekarang memanggil `Network.setCacheDisabled`.
Profil browser bertahan antar run; tanpa ini stylesheet basi ter-screenshot sehingga layout tampak
rusak padahal sumbernya benar — sempat membuat saya mengejar bug layout yang tidak ada.

Screenshot: `docs/screenshots/` — `01-login`, `02-sheets-light`, `03-sheets-dark`, `04-drive-light`,
`05-docs-light`, `06-slides-light`, `07-calendar-light`.

---

### Slides (terverifikasi jalan)
Geometri elemen disimpan dalam **persen** dan ukuran teks dalam `cqw` (1% lebar slide), sehingga
thumbnail 140px adalah miniatur sejati dari stage 960px tanpa perhitungan ulang per zoom — satu
komponen `SlideView` melayani editor, rail thumbnail, dan presentasi layar penuh sekaligus.
Presenter view: slide berikutnya + catatan + timer. Chart bisa di-embed **tertaut** ke spreadsheet
sumbernya. Animasi dibatasi transform + opacity saja — dua properti yang bisa dianimasikan browser
tanpa layout ulang, dan itulah yang menjaga deck tetap mulus di proyektor.

### Calendar (terverifikasi jalan)
Empat tampilan (month/week/day/agenda) membaca dari satu daftar occurrence yang sudah di-expand,
jadi rekurensi tidak pernah sampai ke view. Terbukti benar: standup Senin–Jumat 09:30, Sprint retro
tiap 2 minggu (22 Agu → 5 Sep, tepat 14 hari). Waktu diedit dalam zona lokal browser dan dikonversi
ke UTC saat simpan; service hanya menyimpan dan membandingkan UTC.

**Bug data seed yang ditemukan lewat screenshot:** seeder menghitung jam kerja dari tanggal **UTC**
(`now.Date.AddHours(2)`), sehingga standup demo mendarat pukul 02:00 waktu setempat — kalendernya
benar, data contohnya yang tampak rusak. Sekarang waktu seed dibangun sebagai wall-clock di zona demo
lalu dikonversi ke UTC.

---

## Mr Clippy — `VibeDesk.Ai`

Dua backend di belakang satu `IClippyService`, keduanya berbagi `Kernel` yang sama sehingga tool yang
bisa dipanggil model benar-benar objek `KernelFunction` yang sama:

- **`SemanticKernelBackend`** — OpenAI, Google Gemini, Ollama. Connector-nya sendiri yang menjalankan
  loop tool (`FunctionChoiceBehavior.Auto()`); kelas ini hanya membentuk execution settings dan
  meneruskan chunk. Tool call disadap lewat `IFunctionInvocationFilter` karena pemanggilannya terjadi
  di dalam connector — tidak ada tempat lain untuk melihatnya.
- **`AnthropicBackend`** — Semantic Kernel tidak punya connector Anthropic, jadi ini ditulis langsung
  di atas SDK resmi `Anthropic` dan menjalankan loop tool-nya sendiri.

Keputusan yang perlu diingat:

- **Loop Anthropic sengaja non-streaming.** Menyusun ulang tool call dari token stream berarti
  menambal JSON parsial lintas event `input_json_delta`, dan tool call yang salah parse adalah aksi
  yang diam-diam keliru — bukan yang gagal secara kelihatan. Panel tetap render progresif: jawaban
  final dipotong kecil-kecil saat dikembalikan.
- **`Temperature` tidak dikirim ke Anthropic.** Parameter sampling dihapus dari semua model setelah
  Claude Opus 4.6 dan sekarang menghasilkan 400. Nilai `Assistant:Temperature` tetap dipakai oleh
  OpenAI/Google/Ollama.
- **Kernel dibangun per request, tidak di-cache.** Plugin menutup (closure) atas service milik user
  yang sedang login dan dokumen yang sedang dibuka; men-cache kernel berarti men-cache akses satu user.
- **Ollama dianggap tersedia hanya dari endpoint** (memang tidak butuh API key), jadi `IsConfigured`
  bernilai true out-of-the-box. Konsekuensinya kegagalan koneksi harus terbaca jelas — `Describe()`
  menerjemahkan `SocketException`/`HttpRequestException` jadi pesan yang bisa ditindaklanjuti.

12 kernel function dalam 4 plugin:

| Plugin | Function |
|---|---|
| `time` | `current_datetime`, `date_add`, `days_between` |
| `math` | `calculate` |
| `web` | `web_search` (Tavily), `read_web_page` (HtmlAgilityPack), `read_file_from_url` |
| `workspace` | `search_drive`, `read_document`, `get_open_document`, `list_calendar_events` |

`math.calculate` **memakai ulang `FormulaEngine`**, bukan implementasi kedua — jadi `ROUND`,
`SUMPRODUCT`, dan fungsi tanggal berperilaku persis seperti di dalam sel, dan hanya ada satu tempat
bagi bug untuk hidup.

`WebPlugin` adalah permukaan SSRF aplikasi ini: URL-nya berasal dari model, yang berarti berasal dari
teks apa pun yang dibaca model — termasuk dokumen yang di-share orang asing. Karena itu setiap fetch
dijaga oleh resolusi DNS, bukan hanya pengecekan hostname; alamat loopback, RFC1918, dan 169.254/16
(endpoint metadata cloud) ditolak.

`WorkspacePlugin` read-only dan tidak punya parameter user id sama sekali — semua akses lewat
`IDriveService`/`IDocumentContentService` yang sudah menyelesaikan permission untuk user yang login,
sehingga tidak ada parameter yang bisa diisi model untuk menjangkau berkas orang lain.

---

## Tests

`dotnet run --project tests/VibeDesk.Tests` — 254 tes, semuanya lulus: formula engine, alamat A1,
plugin Clippy, cron lima kolom, `DataFrame`, guard sandbox C#, parser opsi CLI, dan dynamic array.

`dotnet test` **tidak dipakai**: xunit.v3 berjalan di atas Microsoft.Testing.Platform dan .NET 10 SDK
menghapus jalur VSTest untuknya. `dotnet.config` sudah menunjuk runner yang benar, tapi menjalankan
proyek tesnya langsung (ia memang `Exe`) adalah cara yang paling andal.

Satu tes sempat gagal karena asumsi saya sendiri: saya menduga `IF(TRUE,,"no")` menampilkan `0` seperti
Excel, padahal engine mengembalikan blank yang tampil kosong. Perilaku engine lebih baik, dan yang
benar-benar penting — blank tetap ter-koersi jadi 0 dalam aritmetika — sekarang ikut diuji.

---

## API, klien, dan host (Fase 6–8)

### `VibeDesk.Api`

62 rute REST + OpenAPI/Scalar, hub SignalR, dan gRPC — ketiganya di atas service domain yang sama.
Tidak ada logika di endpoint: begitu ada, ketiga transport langsung berbeda perilaku dan hanya satu
yang benar-benar teruji.

Diuji end-to-end terhadap instance yang berjalan, bukan hanya build hijau:

| Uji | Hasil |
|---|---|
| Tanpa token | 401 |
| Login `fadhil@gravicode.com` | JWT 604 karakter |
| `/api/auth/me` | user + peran Admin benar |
| `/api/drive` | 3 folder root ter-seed |
| `/api/calendars` | 2 kalender ter-seed |
| Simpan konten dengan revision basi | **409** + salinan server untuk rebase |
| Item tak terlihat | **404**, bukan 403 |
| Kata sandi salah | 401 |

Satu cacat kontrak ditemukan saat pengujian dan diperbaiki: enum ter-serialisasi sebagai **angka**
(`"type": 0`). Kontrak seperti itu memaksa setiap klien meng-hard-code urutan enum C#, dan menukar
urutannya diam-diam merusak semuanya. Sekarang `JsonStringEnumConverter` dipasang di host.

**Seeder tidak jalan tanpa `launchSettings.json`** — environment default-nya Production, jadi
`IsDevelopment()` false dan tidak ada satu pun user demo. Sudah ditambahkan.

### `VibeDesk.Client`

Satu kelas per interface Application, semuanya di atas REST, plus `ApiSession` pemegang token.
Kegagalan HTTP diterjemahkan balik jadi `NotFoundException`/`ForbiddenException`/`ValidationException`,
sehingga halaman yang ditulis untuk service server berperilaku identik saat bicara ke API.

Dua member sengaja tidak diimplementasikan: `NotifyAsync` melempar (kalau diekspos, siapa pun yang
login bisa mengirim notifikasi ke siapa saja), dan `DispatchDueRemindersAsync` mengembalikan 0
(sweeper-nya milik server; kalau setiap perangkat menjalankannya, semuanya berebut mengirim reminder
yang sama).

### Halaman dipindah ke `VibeDesk.Ui`

Kelima halaman aplikasi tadinya ada di `VibeDesk.Web` — artinya desktop dan mobile tidak bisa
merendernya sama sekali, padahal premisnya "satu UI, tiga host". Sekarang di `VibeDesk.Ui`.

Jebakannya: routing Blazor Web App diselesaikan oleh **endpoint system**, bukan hanya `<Router>`.
`AdditionalAssemblies` saja tidak cukup — `.AddAdditionalAssemblies(...)` di `MapRazorComponents<App>()`
juga wajib. Mendaftarkan hanya salah satunya menghasilkan **404 tanpa gejala lain**; kelima rute sempat
mati sampai keduanya dipasang.

### Desktop: Avalonia gagal, Photino dipakai

Keputusan pindah dari WPF ke Avalonia dipegang, tapi **paketnya tidak bisa dipakai**. Setelah
memeriksa isi `BlazorWebView.Avalonia.Cross` 11.3.1: tidak ada satu pun extension `Use*` di seluruh
assembly-nya — paket inisialisasi platform pendampingnya tidak ada di NuGet dengan nama apa pun yang
bisa ditemukan. Paket itu tidak mandiri, dan menebak API-nya bukan rekayasa.

Fallback yang sudah tertulis di Plan dipakai: **`Photino.Blazor` 4.0.13**. Mandiri, terdokumentasi,
dan mencapai tujuan yang sama — Windows/macOS/Linux dari `VibeDesk.Ui` yang sama.

Diverifikasi benar-benar hidup, bukan hanya build: proses `VibeDesk` dengan judul jendela "VibeDesk",
`Responding=True`, dan render batch-nya berisi markup `SignInView` — UI bersama ter-render di luar
browser.

### Mobile

MAUI Blazor, target **Android** saja. iOS/MacCatalyst tinggal satu baris (workload-nya terpasang) tapi
butuh Mac untuk bundling. Windows sengaja tidak ditarget: `VibeDesk.Desktop` sudah mencakup Windows,
jadi MAUI Windows head hanya jadi aplikasi desktop kedua yang harus dijaga sinkron.

`10.0.2.2`, bukan `localhost`, adalah alamat mesin host dari emulator Android — sudah jadi default.

---

## Script & Automation Engine — `VibeDesk.Scripting`

Fase 9. Tiga bahasa di atas satu API, dengan izin, trigger, audit, template, dan CLI.

### Pilihan runtime, dan konsekuensinya

| Bahasa | Runtime | Kenapa | Yang harus diterima |
| --- | --- | --- | --- |
| JavaScript | **Jint 4.16** | Interpreter murni .NET. Tidak melihat CLR kecuali objek yang diserahkan. | — |
| Python | **IronPython 3.4.2** | Python murni .NET, jadi bisa dikurung tanpa proses terpisah. | **C-extension tidak bisa dimuat**: tidak ada NumPy, tidak ada pandas |
| C# | **Roslyn Scripting 5.9** | Kompilasi ke IL sungguhan, dengan seluruh framework. | Harus **ditolak saat analisis**, dan tidak bisa dihentikan di tengah loop |

Permintaan spec "import library populer (NumPy, ML.NET)" tidak bisa dipenuhi apa adanya untuk NumPy:
IronPython tidak menjalankan kode native. Daripada meninggalkan penulis Python dengan kemampuan yang
lebih sedikit, `DataFrame` dibuat di host — `Where`, `SortBy`, `GroupBy`, `Pivot`, `Distinct`,
`Select`, `AddComputed`, `ToCsv` — dan tersedia identik di **ketiga** bahasa. Filternya memakai string
operator, bukan callback, supaya bentuknya sama persis di JS, Python, dan C#.

### Trigger tanpa menyentuh satu pun service fitur

Event hook dipasang dengan **mendekorasi** `IActivityService` dan `ICollaborationNotifier` di DI, bukan
dengan menambah baris di Drive/Docs/Calendar. `IActivityService` sudah mencatat `item.created`,
`item.shared`, `event.created`, dan seterusnya; `ICollaborationNotifier` dipanggil pada setiap simpan
yang diterima. Jadi seluruh fitur tetap tidak tahu bahwa script itu ada, dan tidak ada satu pun
service fitur yang berubah.

Eksekusi latar belakang berjalan sebagai **pemilik script**, bukan sebagai pemicunya
(`ScriptImpersonation` + `ImpersonatingCurrentUser` mendekorasi `ICurrentUser` apa pun yang dipasang
host). Kalau Sari mengubah berkas yang diawasi script Budi, script itu jalan dengan akses Budi.

### Dua lubang nyata yang hanya ketemu karena dijalankan

**1. Sandbox C# bocor: `Environment.Exit(1)` mematikan proses API.**

Guard-nya menolak `System.IO.File` yang ditulis lengkap, tapi meloloskan `Environment` yang telanjang.
Sebabnya: kompilasi milik guard tidak memakai `using` yang sama dengan kompilasi eksekusi, jadi
`Environment` tidak resolve, `GetSymbolInfo` mengembalikan null, dan pemeriksaannya **diam-diam
dilewati**. Yang berhasil ditangkap justru menyamarkan yang bocor.

Diperbaiki dua lapis: `usings: ScriptUsings` pada kompilasi guard, **dan** lapisan `DeniedSimpleNames`
yang tidak butuh simbol sama sekali (dengan `IsDeclaredLocally`, supaya variabel penulis yang kebetulan
bernama `path` atau `file` tidak ikut ditolak). Analisis simbol itu presisi, tapi ia bisu ketika nama
gagal resolve — dan "gagal resolve" tidak sama dengan "aman".

**2. Timeout tidak menghentikan apa pun.**

`await` pada task eksekusi menunggu sampai selesai, terlepas dari token — pembatalan itu kooperatif.
`while True:` di Python menggantung sampai klien menyerah; versi C#-nya membunuh host. Sekarang
eksekusinya **diadu** dengan `Task.Delay(timeout + 2s)`; kalau jamnya menang, run dilaporkan
`TimedOut` dan **thread-nya ditinggalkan**. Itu batas .NET yang jujur, bukan yang disembunyikan.

Trace hook Python juga tidak pernah dipanggil karena dua hal sekaligus: engine-nya harus dibuat dengan
opsi `Tracing`/`Frames`, dan hook-nya harus **mengembalikan dirinya sendiri** — hook yang mengembalikan
null menyuruh IronPython berhenti menelusuri frame itu, jadi pemeriksaannya berhenti setelah baris
pertama.

### Bug yang jauh lebih besar dari fitur ini: `NoTracking` global

Ditemukan saat menguji `vibedesk enable` — perintahnya sukses, servernya membalas `isEnabled: true`,
tapi pembacaan berikutnya tetap `false`. Diperiksa langsung ke SQLite: barisnya memang tidak berubah.

Penyebabnya bukan di scripting. `DatabaseRegistration` memasang
`UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)` sebagai default global, padahal **setiap
service** menulis `AsNoTracking()` sendiri di jalur baca (60+ pemanggilan di 9 service) dan jalur
tulisnya memuat entity, mengubahnya, lalu `SaveChangesAsync`. Terhadap entity yang detached, itu
no-op yang senyap.

Artinya **tidak ada satu pun update di seluruh aplikasi yang tersimpan** — rename di Drive, simpan
Docs, ubah pengaturan. Responsnya mengembalikan objek yang sudah diubah di memori, jadi UI-nya terlihat
benar sampai halaman dimuat ulang. Dibuktikan dengan `PATCH /api/drive/{id}/name`: respons "Archive
TEST", pembacaan ulang "Archive".

Default tracking dikembalikan. Diverifikasi ulang setelah perbaikan: rename bertahan, `enable`
bertahan, script tersimpan lalu benar-benar jalan.

### UI, CLI, dan template

Halaman `/scripts` punya tiga tab: milik sendiri, galeri template, dan yang dibagikan. Editor
`/scripts/{id}` menaruh kode di kiri dan segala yang mengaturnya di kanan — scope, allowed host,
timeout, trigger, riwayat run — karena hal-hal itulah yang menentukan apakah kodenya boleh melakukan
yang ia tulis, dan itu tidak pantas disembunyikan di balik dialog pengaturan.

Syntax highlighting-nya ditulis sendiri (~100 baris regex di `vibedesk.js`) untuk ketiga bahasa.
Tidak ada bundler di proyek ini dan menarik CDN akan membuat editor mati saat offline — padahal
offline adalah salah satu janji suite ini. Komentar dan string dicocokkan lebih dulu supaya keyword di
dalam string tetap terwarnai sebagai string.

CLI `vibedesk` diuji end-to-end melawan API yang benar-benar jalan: login, `push`, `enable`, `run`
(file lokal dan script tersimpan, JS dan Python), `check` (menolak `Environment.Exit`), `pull`,
`logs`. Dua perilakunya sengaja: **`push` tidak pernah meng-enable** script, dan **`push` tanpa
`--scope` tidak mengubah izin yang sudah ada** — mendorong perbaikan ketik tidak boleh diam-diam
mencabut akses yang dibutuhkan script.

23 template di enam kelompok, termasuk integrasi API eksternal yang diminta spec: kurs, harga emas dan
komoditas, saham, terjemahan, pencarian, dan cuaca. Semua template lahir dalam keadaan **disabled**,
dengan scope yang dibutuhkannya sudah tercentang.

### Trigger yang mendaftar tapi tidak pernah benar-benar jalan

Ditemukan hanya karena mencoba menjalankan sample script bawaan lewat trigger sungguhan, bukan lewat
tombol Run.

**Sebab pertama: dekorator DI terbuang diam-diam.** `AddVibeDeskScripting` mendekorasi
`IActivityService`, `ICollaborationNotifier`, dan `ICurrentUser`. Tapi dekorasi itu *mengganti*
registrasi, dan kedua host mendaftar ulang dua di antaranya **sesudahnya**:
`AddScoped<ICurrentUser, ApiCurrentUser>()` di `AddVibeDeskApiSecurity`, dan
`AddScoped<ICollaborationNotifier, SignalRCollaborationNotifier>()` di host API. Registrasi terakhir
menang, jadi dekoratornya hilang tanpa error.

Akibatnya: event `content.saved` tidak pernah terpicu sama sekali, dan event Drive terpicu lalu gagal
di `RequireId()` karena eksekusi latar tidak punya HTTP context dan impersonation-nya sudah ikut
terbuang. Dari luar terlihat seperti "trigger-nya tidak jalan"; log-nya menunjukkan hal lain.

Dipisah jadi `AddVibeDeskScriptingTriggers()` yang dipanggil **paling akhir** di kedua host, tepat
sebelum `builder.Build()`. Urutannya sekarang eksplisit dan ada komentarnya di tempat kejadian.

**Sebab kedua: trigger tidak membawa konteks.** `ScriptRunRequest` mengirim `input: null`, jadi script
yang dipicu `item.created` tidak tahu berkas mana yang dibuat — praktis tidak bisa berbuat apa-apa.
Sekarang `input` berisi `event`, `itemId`, dan `detail` untuk event; `event`, `cron`, dan `firedAt`
untuk jadwal.

Dibuktikan hidup, bukan hanya dibangun: membuat dokumen lewat `POST /api/drive/documents` memicu
script "Tag new uploads", yang berjalan di latar sebagai pemiliknya dan benar-benar mengubah nama
berkasnya jadi `2026-08 Event Proof`. Riwayat run mencatatnya sebagai `Event`, bukan `Api`.

### Sample data untuk Scripts

Tiga script bawaan — satu per bahasa — supaya tab Scripts tidak terbuka dalam keadaan kosong:
laporan storage (Python), penanda unggahan baru (JavaScript), dan pemeriksa model pendapatan (C#).
Ketiganya lahir **disabled** berikut trigger-nya, dan ketiganya sudah dijalankan sungguhan. Yang C#
menemukan satu baris kosong di `Q3 Revenue Model` bawaan — sample yang menemukan sesuatu yang nyata
lebih berguna daripada sample yang selalu bersih.

Sekaligus menutup dua celah yang baru kelihatan saat sample-nya ditulis:

- **`api.Frame(columns, rows)`** — sebelumnya script bisa *membaca* `DataFrame` dari sheet tapi tidak
  bisa membuat satu dari hasil hitungannya sendiri, sehingga `Sheets.Create(name, frame)` tidak bisa
  dipakai untuk apa pun yang dihitung script.
- **Panel Inputs di editor** — CLI bisa mengirim `--input`, API bisa, tapi editornya tidak. Artinya
  script yang butuh `fileId` sama sekali tidak bisa diuji dari tempat ia ditulis. Sekarang bisa, dan
  screenshot dokumentasinya memakai jalur itu: hasilnya menemukan satu baris kosong yang memang ada di
  data seed.

Satu bug render kecil ikut ketahuan dari screenshot: `<span>v@script.VersionNumber</span>` dibaca Razor
sebagai alamat email, jadi kartu script menampilkan `V@SCRIPT.VERSIONNUMBER` apa adanya. Perlu
`v@(script.VersionNumber)`.

### Audit 30 fungsi spreadsheet, dan apa yang ternyata hilang

Diaudit secara empiris — tiap formula benar-benar dievaluasi lewat engine sungguhan di atas sheet
sungguhan, bukan dicocokkan namanya di kode. Hasil awal: **25 dari 30 didukung**.

Yang menarik, dua "kegagalan" pertama ternyata bukan soal fungsinya:

- **Kelima fungsi lookup lulus semua** (VLOOKUP, HLOOKUP, XLOOKUP, INDEX, MATCH) begitu diuji dengan
  range sel asli. `#VALUE!` yang muncul di percobaan pertama disebabkan **literal array `{...}`** yang
  tidak dikenali parser — bukan fungsinya yang tidak ada.
- Lebih buruk lagi, `COUNT({1;2;3})` **diam-diam menjawab 0**, bukan error. Jawaban salah yang terlihat
  benar adalah kegagalan paling mahal di spreadsheet.

Yang benar-benar hilang: **FILTER, UNIQUE, SORT, SEQUENCE, LET** — seluruh keluarga dynamic array.

### Menambahkannya ternyata butuh tiga perbaikan, bukan satu

Menulis kelima fungsinya mudah. Tapi begitu ditulis tesnya, ketahuan ketiganya tidak berguna tanpa ini:

1. **`INDEX` hanya menerima range**, jadi `INDEX(SORT(A1:A6),1)` menjawab `#REF!` — tidak ada cara
   membaca satu elemen dari hasilnya.
2. **Operator tidak menyebar ke array.** `A1:A3>99` hanya membandingkan sel pertama, jadi mask FILTER
   panjangnya 1 lawan 3 dan ditolak. Tanpa ini FILTER praktis tidak bisa dipakai dengan bentuk yang
   orang tulis secara alami.
3. **Agregat menolak teks di dalam array.** `SUM(SORT(A1:A6))` menjawab `#VALUE!` untuk kolom yang
   memuat nama, padahal `SUM(A1:A6)` mengabaikannya. Sebabnya: pengecekan "lewati teks" memakai tipe
   node (`arg is ReferenceNode`), bukan sifat nilainya. Sekarang `EvaluateToList` melaporkan apakah
   argumennya sebuah koleksi.

Efek samping perbaikan (2) melampaui dynamic array: `SUM(A1:A3*2)` sekarang menjumlahkan seluruh baris
yang digandakan (26), bukan sel pertama dikali dua (10). Itu perilaku yang benar dan tidak ada satu pun
tes lama yang mengunci perilaku sebelumnya.

Literal array `{1,2;3,4}` ikut ditambahkan ke lexer dan parser, mendatar row-major karena
`FormulaValue` array memang tidak menyimpan bentuk.

**Hasil akhir: 30 dari 30**, diverifikasi ulang lewat jalur yang sama. Dua batas ditulis apa adanya di
`docs/apps.md`: hasilnya **tidak spill** ke sel tetangga (sel yang berisi `=SEQUENCE(4)` menampilkan
`1`, persis seperti sel berisi `=A1:A4`), dan array-nya datar sehingga `SORT(range, 2)` ditolak
alih-alih diam-diam mengurutkan kolom pertama.

### Uji asisten dengan LLM sungguhan

Diuji dengan **DeepSeek** (`deepseek-v4-flash`) lewat jalur OpenAI-compatible, plus **Tavily** untuk
pencarian web. Kuncinya lewat environment variable, bukan `appsettings.json`.

Satu bug ketahuan langsung: `ClippyContext` wajib diisi, jadi pesan tanpa `context` — yang sah lewat
REST, gRPC, atau CLI — melempar `NullReferenceException` dan berbalas "An unexpected error occurred."
UI selalu mengirim context, jadi ini hanya terlihat dari jalur API. Sekarang context opsional dan
di-default ke `Workspace`.

Setelah itu semuanya jalan, dan bukan cuma "ada balasannya":

| Yang diuji | Tool yang dipanggil | Hasil |
| --- | --- | --- |
| Persona | — | Menyebut Mr Clippy, Gravicode Studios, Kang Fadhil |
| Drive | `search_drive` | Menyebutkan berkas nyata milik pengguna |
| Aritmetika | `calculate` ×2 | 1234 × 5678 = 7.006.652; √8281 = 91 |
| Tanggal | `current_datetime` | Benar, di zona Asia/Jakarta |
| Web | `web_search` | Ringkasan .NET Aspire berikut tautan sumbernya |
| Baca berkas | `search_drive` → `read_document` → `calculate` | Total Revenue $97.631 — **cocok persis** dengan baris Total di sheet-nya |

Rantai tiga tool terakhir itu yang paling meyakinkan: asisten menemukan sendiri berkasnya, membacanya,
lalu menghitung — dan angkanya bisa dicocokkan dengan isi sheet.

### Impor/ekspor Office — `VibeDesk.Office`

Fase 10. `.docx`, `.xlsx`, dan `.pptx`, dua arah, di atas **DocumentFormat.OpenXml 3.5.1** — SDK resmi
Microsoft, MIT, satu dependensi untuk ketiga format sekaligus alih-alih tiga pustaka satu-format dengan
tiga set keanehan sendiri.

Proyeknya hanya mereferensi `VibeDesk.Application`: konverter memetakan OOXML ke model konten dan tidak
punya urusan dengan EF Core, storage, atau web stack. Kontraknya (`IOfficeConverter`) tinggal di
Application, jadi jalur unggah Drive dan endpoint ekspor bergantung pada abstraksi, bukan pada SDK-nya.

**Impor** menempel di `DriveService.UploadAsync`: berkas Office jadi item yang benar-benar bisa diedit,
bukan lampiran buram. Kalau konversinya gagal — paket rusak, terkunci sandi — berkasnya tetap tersimpan
sebagai lampiran. Menolak unggahannya sama sekali jelas lebih buruk daripada menyimpannya apa adanya.

**Ekspor** dibangun ulang dari model konten setiap kali diminta; tidak ada yang disimpan. Ada di
`/api/drive/{id}/export` dan `/drive/export/{id}`, plus entri "Download as .docx/.xlsx/.pptx" di menu
Drive.

### Round-trip lolos, tapi Office menolak file-nya

Tes round-trip pertama lulus semua — dan itu justru menyesatkan: kedua sisi konverter sepakat satu sama
lain, yang akan tetap terjadi walaupun keduanya salah dengan cara yang sama.

Ditambahkan `OfficeValidityTests` yang menjalankan `OpenXmlValidator` bawaan SDK — aturan yang sama yang
dipakai Word, Excel, dan PowerPoint saat memutuskan membuka berkas atau menawarkan "repair". **Dua dari
tiga format langsung gagal:**

- **Word** — `<w:tbl>` wajib punya `<w:tblGrid>` sebelum barisnya; urutan anak `<w:tblBorders>` terkunci
  skema (top, left, bottom, right, insideH, insideV) dan urutan saya salah; di `<w:pPr>`, `keepNext`
  harus mendahului `spacing`, dan `spacing` mendahului `ind`.
- **PowerPoint** — `<a:majorFont>`/`<a:minorFont>` wajib menyebut `<a:ea>` dan `<a:cs>`, meski kosong.

Excel lolos sejak awal. Ketiganya valid sekarang, dan tes validator itu yang menjaganya tetap begitu.

### Yang terbawa dan yang hilang

Konversi ini **lossy dua arah**, dan itu memang disengaja: model VibeDesk mendeskripsikan dokumen
sebagai HTML, peta sel jarang, dan daftar elemen slide — tidak satu pun berbentuk OOXML.

| | Terbawa | Hilang |
| --- | --- | --- |
| Word | Heading, paragraf, bold/italic/underline/strike, daftar berpoin dan bernomor, tabel, tautan, line break | Gambar, footnote, header/footer, section break, font dan warna, tracked changes |
| Excel | Seluruh sheet, nilai, tipe, formula, format angka kustom | Font, isian, border, grafik, pivot, conditional formatting, validasi data, sel gabungan, gambar |
| PowerPoint | Urutan slide, teks tiap shape, catatan pembicara | Gambar, grafik, tabel, SmartArt, tema, animasi, transisi, posisi persis |

Format biner lama (`.doc`, `.xls`, `.ppt`) **ditolak dengan sengaja**. Itu bukan OOXML sama sekali, SDK-nya
tidak bisa membacanya, dan meloloskannya sebagai "dokumen" hanya akan menghasilkan berkas penuh mojibake
alih-alih jawaban jujur "ini tetap jadi lampiran".

Diverifikasi end-to-end, bukan hanya lewat unit test: `.docx` berisi heading, run tebal, dan tabel
diunggah lewat API dan kembali sebagai `Document` dengan HTML
`<h1>…</h1><p>… <b>32 persen</b> …</p><h2>…</h2><table>…</table>` utuh. Ekspor `Q3 Revenue Model`
menghasilkan `.xlsx` berisi 77 sel dengan angka **dan** formulanya (`B2*C2`, `D2-E2`) — bukan hanya
nilai hasil hitungnya.

### Bug lama yang baru ketahuan: semua unduhan di web host 500

Ekspor lewat UI mengembalikan 500. Dikira bug rute baru — ternyata rute **unduh berkas yang sudah ada
sejak lama pun 500**, dengan error yang sama persis:

> Do not call GetAuthenticationStateAsync outside of the DI scope for a Razor component.

`HttpCurrentUser` memanggil `AuthenticationStateProvider` **di konstruktornya**. Provider itu hanya sah
di dalam scope DI sebuah komponen Razor dan melempar di luar itu — jadi setiap minimal-API endpoint di
host web gagal begitu menyentuh service apa pun yang me-resolve `ICurrentUser`. Karena `DriveService`
memakainya secara internal, memindahkan injeksi keluar dari endpoint tidak akan menolong; perbaikannya
harus di `HttpCurrentUser` sendiri.

Sekarang resolusinya ditunda sampai pemakaian pertama, dan kalau provider menolak, jatuh ke
`IHttpContextAccessor`. Alasan asli memilih provider tetap berlaku — circuit Blazor hidup lebih lama
daripada request yang membukanya, jadi HTTP context bisa null atau basi di dalam komponen — tapi di luar
circuit tidak ada masalah itu, dan principal milik request justru yang paling tepat.

Diverifikasi: ekspor sekarang `200` dengan
`content-disposition: attachment; filename=laporan.docx`, dan yang 500 hilang.

---

## Langkah berikutnya

Semua yang ada di spec sudah terkirim. Yang tersisa adalah pengetatan, bukan fitur:

1. **Closure table** menggantikan filter visibilitas berbasis path — filter sekarang tidak ramah index
   dan itu sudah dicatat sejak awal, bukan ditemukan nanti.
2. **Rate limiting** di API.
3. **Redis + PostgreSQL sebagai jalur produksi yang diuji**, bukan sekadar opsi konfigurasi.
4. **iOS/MacCatalyst** untuk mobile, saat ada Mac untuk mem-bundle-nya.
5. **Isolasi proses untuk script C#** — analisis Roslyn itu pertahanan berlapis, bukan batas VM, dan
   thread yang ditinggalkan setelah timeout adalah harga yang dibayar karena .NET tidak bisa
   membatalkan thread. Keduanya hilang kalau script dijalankan di proses terpisah.
