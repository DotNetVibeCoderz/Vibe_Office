# Progress — OfficeNet

Checklist pengembangan. Diperbarui setiap kali sebuah bagian selesai dan **terverifikasi**, bukan
sekadar selesai ditulis. "Terverifikasi" berarti: build hijau, round-trip lewat pembaca kedua, dan
struktur paket lolos validator independen.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

## Legenda

- `[x]` selesai dan terverifikasi
- `[~]` sebagian
- `[ ]` belum dikerjakan

---

## Fondasi

- [x] `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets`
- [x] `OfficeNet.sln` (format klasik — `dotnet new sln` default ke `.slnx` di .NET 10)
- [x] Seluruh solusi build `Release` hijau, 0 warning
- [x] Nama paket: `PdfNet` sudah dipakai di NuGet → **semua** pakai prefix `Gravicode.OfficeNet.*`
- [x] `assets/icon.png` untuk paket NuGet
- [x] `README.md` (EN) dan `README.id.md`
- [x] `.gitignore`

## OfficeNet.Core — `Gravicode.OfficeNet.Core`

- [x] `OpcPackage` / `OpcPart` / `OpcPartName` / `OpcRelationship` — kontainer OPC
- [x] `ContentTypes`, `RelationshipTypes`
- [x] `Length` / `Units` — EMU sebagai satuan penyimpanan
- [x] `Ns` — namespace OOXML termasuk pemetaan strict → transitional
- [x] `XmlUtil` — urutan skema, toggle tri-state, `xml:space`
- [x] `OfficeColor` — RGB, tema, tint/shade, luminance WCAG
- [x] `ImageInfo` — sniffing PNG/JPEG/GIF/BMP/TIFF/WebP/EMF/WMF/SVG + DPI
- [x] `CoreProperties`, `ExtendedProperties`, `CustomProperties`
- [x] `OfficeDocument` / `IOfficeDocument` — API terpadu + dedup media via SHA-256
- [x] Build hijau, 0 warning

## PdfNet — `Gravicode.OfficeNet.PdfNet` (rewrite PyPDF2)

- [x] Model objek COS lengkap (null, bool, number, string, name, array, dict, stream, reference)
- [x] Filter: Flate (+ fallback framing), LZW, ASCIIHex, ASCII85, RunLength, predictor PNG/TIFF
- [x] Parser toleran + `PdfReader` dengan xref table, xref stream, object stream, hybrid `/XRefStm`
- [x] Pemulihan file rusak dengan pemindaian `N G obj`
- [x] `PdfWriter` — file lengkap, xref klasik 20 byte/entri
- [x] `PdfDocument` / `PdfPage` / `PdfPageCollection` — buka, gabung, split, rotate, reorder
- [x] Enkripsi: RC4-40, RC4-128, AES-128, AES-256 (R6) — **round-trip keempatnya terverifikasi**
- [x] Ekstraksi teks: content stream, CMap, ToUnicode, simple/composite font, spasi inferensial
- [x] Struktur halaman — baris, paragraf, heading, dan tabel disimpulkan dari posisi glyph
- [x] Penyematan font TrueType — subset, dinomori ulang, `/CIDToGIDMap`, `/ToUnicode` selalu ditulis
- [x] Ekstraksi gambar: DCT passthrough, PNG encoder tangan, indexed/CMYK → RGB
- [x] `PdfCanvas` — path, teks, wrapping, justifikasi, gambar, opacity
- [x] `StandardFonts` — metrik AFM 14 font baku
- [x] `PdfImageBuilder` — JPEG & PNG embed tanpa re-encode (predictor 15)
- [x] `AcroForm` — baca/isi field, generate appearance, flatten
- [x] Anotasi — note, link, highlight/underline/strikeout, stamp, square, watermark
- [x] Smoke test: buat → simpan → buka → ekstrak → split → merge → rotate → enkripsi
- [ ] Konversi PDF → Word / PDF → Excel
- [ ] Rasterisasi PDF → image (butuh SkiaSharp)

## WordNet — `Gravicode.OfficeNet.WordNet` (rewrite python-docx)

- [x] `WordDocument` — create, open, `FromTemplate`, save; deteksi paket salah format
- [x] `Paragraph` / `Run` — teks, break, tab, formatting tri-state
- [x] `RunFormat` / `ParagraphFormat` — urutan skema `w:rPr` dan `w:pPr` dipatuhi
- [x] `StyleCollection` — baca/tulis `styles.xml`, style bawaan (Heading 1-9, Title, Quote, Code…)
- [x] Tabel — baris, sel, merge horizontal & vertikal, border, shading, header berulang
- [x] Section — ukuran halaman (termasuk **F4/Folio**), margin, orientasi, kolom, nomor halaman
- [x] Header & footer — default/first/even, field PAGE & NUMPAGES
- [x] Numbering — bullet, numbered, outline multi-level
- [x] Gambar inline dengan ukuran natural dari DPI, dan gambar mengambang lewat `AddPicture(wrap:)`
- [x] Hyperlink eksternal & internal, bookmark, field, TOC
- [x] `ReplaceText`, `ReplaceTextAcrossRuns`, `MailMerge`
- [x] Ekspor PDF — layout mengalir, style chain, wrapping, justifikasi, tabel, header/footer
- [x] Smoke test + **validasi struktur .docx oleh pembaca independen (python)**
- [x] Impor PDF → Word — paragraf, heading berjenjang, dan tabel dari posisi glyph
- [x] Footnote / endnote — id 0 dan 1 dicadangkan; ikut digambar di ekspor PDF
- [x] Komentar (`comments.xml`) — termasuk mengomentari satu run saja
- [x] Shape / text box — `wps:wsp`, sebelas geometri preset, inline dan mengambang
- [x] Ekspor PDF objek mengambang — teks mengalir mengelilingi, shape digambar sebagai jalur

## ExcelNet — `Gravicode.OfficeNet.ExcelNet` (rewrite openpyxl + pandas)

- [x] `CellReference` / `CellRangeReference` — A1, base-26 bijektif
- [x] `CellValue` — number, text, bool, date (epoch 1899-12-30 + bug 1900), error
- [x] `Stylesheet` — font, fill, border, numFmt, cellXfs dengan dedup otomatis
- [x] `CellStyle` sebagai value type; **jebakan default record struct sudah ditutup**
- [x] `Worksheet` — sel sparse, merge, lebar kolom, tinggi baris, freeze, autofilter
- [x] Conditional formatting — cellIs, color scale, data bar
- [x] `SheetXml` — baca/tulis streaming lewat `XmlReader`/`XmlWriter`
- [x] `SharedStrings` — rich text digabung, sanitasi karakter kontrol
- [x] `FormulaEngine` — 60+ fungsi, presedensi Excel (`-2^2 = 4`), MOD/ROUND semantik Excel
- [x] CSV import/export dengan parser quoted field yang benar + preset `id-ID`
- [x] JSON import/export
- [x] SQL import/export lewat `DbConnection` (parameterised, transaksional)
- [x] Jembatan **GraviFrame** — `ToDataFrame` / `WriteDataFrame` dengan inferensi tipe mayoritas
- [x] Ekspor PDF — paginasi dua arah, header berulang, format angka
- [x] Smoke test + **validasi struktur .xlsx oleh pembaca independen (python)**
- [x] Chart (`c:chart` DrawingML) — lewat part drawing perantara
- [x] Pivot table — 4 part, `refreshOnLoad`; grid hasil sengaja tidak ditulis
- [x] Data validation (dropdown) — tulis dan baca; batas 255 karakter ditegakkan
- [x] Proteksi sheet dan workbook — tulis dan baca; flag dibalik tepat sekali
- [x] Streaming writer — sejuta baris, memori rata; hanya menulis dan hanya maju
- [x] Lookup — `VLOOKUP`, `HLOOKUP`, `INDEX`, `MATCH`, `XLOOKUP`; argumen range membawa bentuknya
- [x] Impor PDF → Excel — tabel dari kolom yang sejajar; angka ambigu dibiarkan teks

## PowerPointNet — `Gravicode.OfficeNet.PowerPointNet` (rewrite python-pptx)

- [x] `Presentation` — create, open, `FromTemplate`, save; deteksi paket salah format
- [x] Slide — tambah, hapus, reorder, duplikat (termasuk remap relasi gambar), sembunyikan
- [x] Enam layout bawaan + slide master + tema (12 slot warna, fmtScheme lengkap)
- [x] `SlideMaster.SetThemeColor` / `SetThemeFonts` — restyle sekali untuk seluruh deck
- [x] Shape, placeholder (resolusi dari layout), autoshape preset, text box
- [x] `TextFrame` / `TextParagraph` / `TextRun` — level, bullet, numbering, anchor, autofit
- [x] `SlideTable` — grid, span, fill, header row; dikenali kembali saat file dibuka
- [x] `Picture` — sisip, ukuran natural dari DPI, crop
- [x] Notes slide
- [x] Transisi (9 preset) + animasi penuh — entrance/emphasis/exit/motion path, pemicu berurutan
      dan pemicu klik-bentuk (`interactiveSeq`)
- [x] Ekspor PDF — satu halaman per slide, tabel, gambar, nomor slide, notes, watermark
- [x] SmartArt — 5 part (data, layout, colors, quickStyle, drawing); 5 jenis diagram; ikut
      digambar di ekspor PDF
- [x] Smoke test + **validasi struktur .pptx oleh pembaca independen (python)**

### Fitur dari PptxGenJS (ditambahkan atas permintaan)

- [x] **HTML → PPTX** (`HtmlToSlides`) — heading jadi judul slide, konten jadi body, paginasi
      otomatis; formatting inline (tebal/miring/garis bawah/coret/warna/font/ukuran/tautan)
      dipertahankan per run
- [x] Parser HTML sendiri (`HtmlParser`) — tanpa dependensi pihak ketiga; implicit close,
      void element, raw text, entity numerik & bernama
- [x] Parser CSS inline (`CssStyle`) — warna (hex/rgb/nama), ukuran (px/pt/em/in/cm), berat,
      gaya, dekorasi, perataan; pewarisan lewat `Merge`
- [x] **`TableToSlides`** — satu tabel besar dipecah lintas slide dengan header berulang
- [x] Gambar dari `data:` URI dan berkas lokal; URL jarak jauh **hanya** lewat `ImageResolver`
      (konversi tidak pernah diam-diam melakukan request jaringan)
- [x] **Chart native** (`ChartData` / `SlideChart`) — column, bar, stacked, line, line+marker,
      area, pie, doughnut, scatter, radar; judul, legenda, label data, persentase, format angka,
      judul sumbu, gridline, warna per seri/slice; data di-cache di dalam part sehingga tidak
      perlu workbook ikut
- [x] **Media** (`SlideMedia`) — video & audio tersemat dengan *dua* relasi yang dibutuhkan
      (2007 + 2010), poster frame yang dibuat sendiri, autoplay lewat pohon timing
- [x] **Video online** — ditautkan, bukan disematkan
- [x] **Efek shape** (`ShapeEffects`) — gradien linear (2 warna atau lebih), transparansi,
      bayangan, glow, outline bertitik/putus, hyperlink pada shape
- [x] Opsi gambar — `AsEllipse`, `AsRoundedRectangle`, `Cover` (crop, bukan distorsi)
- [x] 96 tes PowerPointNet (dari 28), termasuk seluruh fitur di atas

- [x] **Chart digambar saat ekspor PDF** (`ChartRenderer`) — column/bar/stacked, line, area,
      pie/doughnut, scatter, radar; gridline, legenda, label data, skala "angka bulat".
      Sebelum ini slide chart diekspor menjadi halaman kosong.
- [x] `ChartXml.Read` / `SlideChart.GetData()` — chart bisa dibaca kembali menjadi `ChartData`
- [x] Ekspor image lewat `OfficeNet.Rendering`
- [ ] Ekspor video (butuh FFMPEG — paket terpisah lagi)

## OfficeNet — `Gravicode.OfficeNet` (meta package)

- [x] Facade `Office.Open(path)` yang mendeteksi format dari isi, bukan ekstensi
- [x] `Office.DetectFormat`, `Office.ExtractText`, `Office.ConvertToPdf`, `SupportedExtensions`
- [x] **Registry plugin** (`OfficeFormats` + `IOfficeFormatHandler`) — `Office.Open`,
      `ExtractText`, `SupportedExtensions`, dan `IsSupportedExtension` berkonsultasi ke registry.
      Format bawaan selalu menang, handler yang melempar saat mengendus dilewati, dan pendaftaran
      bersifat eksplisit (tanpa pemindaian assembly). Dibuktikan oleh 10 tes yang mendaftarkan
      format OPC baru dari ujung ke ujung.

## OfficeNet.Rendering — `Gravicode.OfficeNet.Rendering`

Paket terpisah **dengan sengaja**: satu-satunya komponen yang butuh dependensi native (SkiaSharp),
sehingga kelima paket lainnya tetap murni terkelola.

- [x] `DocumentRenderer` — `.docx`/`.xlsx`/`.pptx`/`.pdf` → PNG/JPEG/WebP
- [x] Interpreter content stream sendiri (`PageContent`) — path diisi & digaris dengan warna yang
      benar, aturan even-odd/winding, penempatan gambar lewat CTM, warna teks per run
- [x] `RenderThumbnail`, `RenderToFiles`, batas `MaxPixels` (menurunkan DPI, bukan memotong)
- [x] Rotasi halaman diterapkan seperti yang dilakukan penampil
- [ ] Gradien, pattern, soft mask, clipping — di luar cakupan renderer pratinjau

## Pengujian — **389 tes, semua lulus**

- [x] `tests/OfficeNet.TestKit` — `OpcValidator` (pembaca independen), `TempFile`, `TestImages`
- [x] `tests/OfficeNet.Core.Tests` — 41 tes
- [x] `tests/PdfNet.Tests` — 45 tes
- [x] `tests/WordNet.Tests` — 34 tes (termasuk `DocxValidator`: urutan skema, sectPr, w:tc, r:id)
- [x] `tests/ExcelNet.Tests` — 111 tes (termasuk `XlsxValidator`: urutan anak, fill 0/1, sharedStrings)
- [x] `tests/PowerPointNet.Tests` — 109 tes (termasuk `PptxValidator`: sldId ≥ 256, spTree, tema,
      dan round-trip chart untuk kesepuluh tipe)
- [x] `tests/OfficeNet.Rendering.Tests` — 11 tes; memeriksa **piksel** pada posisi yang diketahui,
      bukan sekadar "menghasilkan PNG"
- [x] `tests/OfficeNet.Docs.Tests` — 36 tes: **setiap contoh kode di `docs/` dan setiap sel kode di
      `notebooks/` dikompilasi dan dijalankan**
- [x] `WordNet.Tests.ScalingTests` — menjaga *bentuk biaya*, bukan kecepatan: membandingkan waktu N
      terhadap 4N sehingga kembalinya jalur O(N²) membuat tes gagal
- [x] Validator OPC sudah jadi kode uji, bukan lagi skrip python ad-hoc
- [x] `WordTests.Validate` kini memeriksa urutan anak `w:tbl`/`w:tr`/`w:tc`, bukan hanya `w:rPr`
      dan `w:pPr`

### Bug yang ditemukan dan diperbaiki oleh tes

- `PdfStream` mewarisi `PdfDictionary`, sehingga cabang checkbox di `AcroForm` menelan setiap
  text field — flatten kehilangan seluruh teks. Terjadi di dua tempat (`StampWidget`, `OnStates`).
- `IFERROR` tidak pernah menerima error: eksepsi dari argumen membatalkan seluruh formula. Sekarang
  error menjadi *nilai*, dengan daftar fungsi yang toleran terhadap error.
- `StringBuilder.AppendLine` memakai `Environment.NewLine`, sehingga teks hasil ekstraksi berbeda
  antara Windows dan Linux. Diganti LF eksplisit di keempat library.
- Placeholder `subTitle` tidak dikenali oleh `SetBody`, sehingga `AddTitleSlide` gagal.
- Tabel PowerPoint tidak dikenali kembali saat file dibuka (semua `graphicFrame` jadi `Shape`).
- **`w:trPr` ditulis *setelah* seluruh `w:tc`.** `GetOrCreate` diberi daftar urutan yang hanya
  berisi `w:trPr` sendiri, sehingga `SetOrdered` tidak menemukan penerus dan menambahkannya di
  akhir. `CT_Row` adalah `tblPrEx?, trPr?, tc…` — Word melaporkannya sebagai berkas yang perlu
  diperbaiki. Ditemukan dengan membaca XML hasil keluaran, bukan oleh tes; validator sekarang
  memeriksanya.
- `w:gridCol` ditulis tanpa `w:w`. Legal, tetapi tidak menyampaikan apa pun — dan pengekspor PDF
  library ini sendiri membaca grid untuk menata kolom.
- Chart diekspor ke PDF sebagai halaman kosong: `PptToPdf` menangani `Picture` dan `SlideTable`
  tetapi tidak `SlideChart`.
- `ChartXml.Read` sempat membaca `c:pt` dalam urutan dokumen; titik boleh jarang, sehingga satu
  lubang menggeser semua nilai sesudahnya. Sekarang dibaca menurut `idx`, dan yang kosong menjadi
  `NaN`.
- **16 nama API yang salah di draf pertama `docs/`** — ditemukan oleh `OfficeNet.Docs.Tests`
  begitu contoh-contohnya dijadikan kode yang dikompilasi.

## Aplikasi contoh — **kelimanya selesai**

- [x] `samples/BatchConverter.Console` — konversi massal ke PDF; berkas rusak dicatat lalu
      dilanjutkan, kode keluar 0/1/2 supaya scheduler bisa bertindak. Diuji pada berkas nyata.
- [x] `samples/EtlPipeline.Console` — CSV → workbook + laporan Word + deck, masing-masing dengan
      PDF-nya. Empat library dalam satu pipeline; keluarannya diperiksa secara visual.
- [x] `samples/OfficeNet.Dashboard` — Blazor Server: unggah, pratinjau, dan **anatomi paket** (part,
      tipe konten, ukuran, dan graf relationship). `?demo=1` memuat tiga dokumen contoh.
- [x] `samples/OfficeNet.Gallery` — Avalonia: 10 demo, masing-masing berjalan di sebelah **kode
      yang benar-benar dijalankan** (dibaca dari sumber tersemat saat runtime, jadi tidak bisa
      menyimpang). Plus asisten Semantic Kernel dengan sesi ganda dan contoh prompt yang bisa diklik.
- [x] `samples/OfficeNet.Editor` — Avalonia: buka `.docx`/`.xlsx`, sunting teks, lihat halaman yang
      dirender sungguhan, ekspor PDF.

### Asisten Gallery — terverifikasi dengan model sungguhan

Provider: OpenAI, **Azure OpenAI**, Anthropic, Google Gemini, DeepSeek. Kunci hanya dari environment
variable, tidak pernah dari berkas di repo.

Kernel function: `web_search` (Tavily), `fetch_page`, `current_datetime`, `days_between`,
`calculate`, `percentage_change`.

Diuji langsung terhadap Azure OpenAI `gpt-5-mini` dan DeepSeek `deepseek-v4-flash`; fungsi
matematika, tanggal, dan pencarian web semuanya benar-benar terpanggil.

Dua hal yang ditemukan saat pengujian nyata:

- **Anthropic tidak punya konektor SK resmi.** `AnthropicChatCompletionService` bicara langsung ke
  Messages API: system prompt adalah field tingkat atas (bukan pesan), `max_tokens` wajib, dan
  autentikasinya `x-api-key` + `anthropic-version`. Tool calling sengaja tidak diimplementasikan
  di sana alih-alih diiklankan lalu diam-diam diabaikan.
- **Model bernalar menolak `temperature` selain bawaannya.** `gpt-5-mini` menjawab permintaan
  dengan `Temperature = 0.2` sebagai `HTTP 400 unsupported_value`, jadi sampelnya tidak menetapkan
  temperature sama sekali.

## Benchmark, notebook, dokumentasi

- [x] `docs/` (EN) — indeks, Core, WordNet, ExcelNet, PowerPointNet, PdfNet, Rendering, OfficeNet
- [x] `docs/id/` — cermin Bahasa Indonesia untuk kedelapan halaman
- [x] `docs/screenshots/` — 11 gambar, **dihasilkan** oleh `tools/ScreenshotGen` dari keluaran
      library, bukan ditangkap dari aplikasi; tidak mungkin menyimpang dari kodenya
- [x] `notebooks/*.ipynb` (.NET Interactive) — Quickstart + satu per library, sel markdown bilingual
- [x] `tools/gen_notebooks.py` + `tools/gen_notebook_tests.py` — notebook dihasilkan, dan setiap
      selnya dikompilasi oleh test suite
- [x] `Plan.md` (roadmap)
- [x] `benchmarks/OfficeNet.Benchmarks` (BenchmarkDotNet) — dijalankan, hasil dilaporkan di
      [benchmarks/README.md](benchmarks/README.md)
- [x] `../.github/workflows/officenet-ci.yml` — build + tes di Windows, Linux, macOS; memverifikasi
      berkas hasil generate masih mutakhir; memeriksa tautan dokumentasi. Ada di akar repo karena
      GitHub hanya membaca workflow dari sana; difilter `paths: OfficeNet/**`.
- [x] `../.github/workflows/officenet-release.yml` — pack + publish ke NuGet, dipicu tag
      `officenet-v*`, dengan environment gate karena publikasi tidak bisa dibatalkan
- [x] `.gitignore`
- [x] `tools/check_links.py` — 174 tautan relatif diverifikasi

### Bug performa yang ditemukan oleh benchmark

Tes tidak bisa melihat keduanya: keluarannya benar, hanya lambat.

- **`WordDocument.InsertBlock` O(jumlah blok) per sisipan.** `AddBeforeSelf` menyusuri daftar anak
  dari depan karena LINQ to XML memakai singly linked list. 10.000 paragraf: **1.576 ms → 99 ms**.
- **Indexer tabel mengalokasikan pembungkus untuk tiap baris dan sel pada setiap akses.** Tabel 500
  baris diisi sel demi sel: **92 ms → 9,3 ms**, alokasi **55 MB → 7 MB**.
  Yang *tidak* hilang: `table[r, c]` tetap O(baris) karena LINQ to XML memakai linked list, jadi
  mengisi tabel lewat indexer tetap kuadratik dalam waktu (2000 baris: 236 ms). Jalur linearnya
  adalah menelusuri `table.Rows` sekali (36 ms) — sekarang didokumentasikan di indexer-nya dan di
  panduan WordNet, bukan disembunyikan.

## Rilis — **v1.0.0 terbit di NuGet**

Tujuh paket beserta symbol package, dipublikasikan 8 September 2026:

| Paket | Isi |
|---|---|
| `Gravicode.OfficeNet` | Meta package + facade `Office` + registry plugin |
| `Gravicode.OfficeNet.Core` | Kontainer OPC, satuan, warna, metadata, model chart |
| `Gravicode.OfficeNet.WordNet` | `.docx` |
| `Gravicode.OfficeNet.ExcelNet` | `.xlsx` |
| `Gravicode.OfficeNet.PowerPointNet` | `.pptx` |
| `Gravicode.OfficeNet.PdfNet` | PDF |
| `Gravicode.OfficeNet.Rendering` | Rasterisasi (SkiaSharp) |

- [x] Nama paket diperiksa dulu di nuget.org — ketujuhnya belum dipakai
- [x] Diunggah menurut urutan dependensi, sehingga tidak pernah ada paket yang merujuk
      dependensi yang belum terbit
- [x] **Diverifikasi dengan mengonsumsinya dari proyek baru** lewat nuget.org: Word, Excel dengan
      chart dan pivot, `SUM` yang benar-benar dihitung, konversi PDF, dan render PNG

## CI — gagal, lalu diperbaiki

Workflow memang terpicu di ketiga commit pertama, dan **gagal di ketiganya**. Klaim "seharusnya
jalan" ternyata setengah benar: ia jalan, tetapi tidak hijau.

Penyebabnya satu, dan asalnya dari sini:

- `Avalonia.Diagnostics` direferensikan pada versi **12.1.1, yang tidak pernah ada** — paket itu
  berhenti di 11.3.20. Referensinya bersyarat `Configuration == Debug`, sedangkan setiap build
  lokal memakai `-c Release`, sehingga tidak pernah dievaluasi di sini. CI menjalankan
  `dotnet restore` **tanpa** konfigurasi, yang berarti Debug, jadi paket itu diminta dan restore
  gagal sebelum satu baris pun dikompilasi. Paketnya juga tidak pernah dipakai:
  `AttachDevTools()` tidak ada di mana-mana.

Dua perbaikan:

- [x] Referensi dan versinya dibuang, dengan catatan di `Directory.Packages.props` supaya tidak
      ditambahkan kembali begitu saja
- [x] `Configuration: Release` diset di level workflow dan diteruskan ke `restore`, sehingga
      restore dan build tidak bisa lagi mengevaluasi konfigurasi yang berbeda

Pelajarannya: **membangun hanya dengan `-c Release` di lokal menyembunyikan seluruh graf dependensi
Debug.** `dotnet restore` tanpa argumen adalah cara termurah menemukannya.

### Kegagalan kedua: `-warnaserror`

Restore lolos, lalu Build gagal — dan penyebabnya bukan lintas platform sama sekali:

- `TextBox.Watermark` sudah usang di Avalonia 12 (namanya kini `PlaceholderText`). Itu sebuah
  *warning*, dan CI membangun dengan `-warnaserror`, sementara verifikasi lokal selama ini hanya
  `dotnet build -c Release` tanpa flag itu. Jadi peringatannya selalu ada, hanya tidak pernah
  fatal di mesin ini.
- Windows gagal lebih awal, di Restore, karena `-p:Configuration=$Configuration` adalah ekspansi
  shell POSIX; shell bawaan `windows-latest` adalah PowerShell, sehingga nilainya kosong. Diganti
  dengan ekspresi GitHub `${{ env.Configuration }}` yang berlaku di semua shell.

- [x] `Watermark` → `PlaceholderText`
- [x] Restore memakai ekspresi GitHub, bukan ekspansi shell
- [x] Build di CI kini memancarkan diagnostiknya sebagai anotasi `::error::`, karena log mentah
      butuh autentikasi sementara anotasi bisa dibaca publik

**Pelajarannya: verifikasi lokal harus memakai flag yang sama dengan CI.** `dotnet build -c Release`
dan `dotnet build -c Release -warnaserror` adalah dua perintah berbeda, dan hanya satu di antaranya
yang dijalankan CI.

### Kegagalan ketiga: substitusi variabel di PowerShell

Linux dan macOS hijau, Windows gagal dengan `MSB4126: solution configuration "|Any CPU" is
invalid` — konfigurasinya sampai dalam keadaan kosong.

Restore sudah memakai ekspresi GitHub dan lolos; Build memakai env var tingkat langkah dan tidak.
Env var memang terlihat oleh shell, tetapi mensubstitusinya butuh sintaks shell, dan
`$CONFIGURATION` tidak berarti apa-apa bagi PowerShell — shell bawaan `windows-latest`. Ekspresi
GitHub disubstitusi sebelum shell mana pun melihatnya, jadi ia berperilaku sama di ketiga runner.

- [x] Restore, Build, dan Test semuanya memakai `${{ env.Configuration }}`

## CI — **hijau di ketiga sistem operasi**

Run `3456147`: `ubuntu-latest`, `macos-latest`, `windows-latest`, `generated files are current`,
dan `documentation links` — kelimanya lulus.

Tiga kali gagal sebelum hijau, dan ketiganya adalah hal yang tidak mungkin terlihat dari mesin
pengembangan:

| Gagal | Sebab | Kenapa lolos di lokal |
|---|---|---|
| Restore | `Avalonia.Diagnostics 12.1.1` tidak pernah ada | Referensinya hanya untuk Debug; lokal selalu `-c Release` |
| Build | `TextBox.Watermark` usang di Avalonia 12 | Peringatan; lokal tidak memakai `-warnaserror` |
| Build (Windows) | `$CONFIGURATION` kosong di PowerShell | Runner lain memakai bash |

**Semuanya satu pola yang sama: verifikasi lokal tidak menjalankan perintah yang sama dengan CI.**
Perintah yang setara dengan CI adalah:

```powershell
dotnet restore OfficeNet.sln -p:Configuration=Release
dotnet build OfficeNet.sln -c Release --no-restore -warnaserror
dotnet test OfficeNet.sln -c Release --no-build
```

## Yang masih tersisa

Tidak ada butir rilis yang terbuka. Sisanya adalah pekerjaan v1.1 dan seterusnya di
[Plan.md](Plan.md).
