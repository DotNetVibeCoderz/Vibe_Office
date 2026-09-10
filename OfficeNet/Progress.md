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
- [x] Tanda tangan digital — verifikasi dan penandatanganan (`adbe.pkcs7.detached`, SHA-256)
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

## Rilis

### v1.1.0 — terbit di NuGet

Ketujuh paket beserta symbol package, dipublikasikan 9 September 2026. Isi rilisnya adalah seluruh
bagian v1.1 [Plan.md](Plan.md), yang kini selesai:

| Pustaka | Yang bertambah di 1.1.0 |
|---|---|
| WordNet | Footnote & endnote, komentar, text box & shape, objek mengambang di ekspor PDF, impor PDF |
| ExcelNet | Data validation, proteksi sheet & workbook, streaming writer, `HLOOKUP`/`INDEX`/`MATCH`/`XLOOKUP`, impor tabel PDF |
| PowerPointNet | SmartArt (5 jenis), animasi penuh (entrance/emphasis/exit/motion path, pemicu klik-bentuk) |
| PdfNet | Penyematan font TrueType, tanda tangan digital, `PageStructure` |
| Rendering | `RenderSlide`, rentang halaman, `RenderToFiles` untuk dokumen hidup |

- [x] Dependensi antar-paket diperiksa dulu: ketujuhnya menunjuk 1.1.0, bukan 1.0.0 yang lama
- [x] Diunggah menurut urutan dependensi, sehingga tidak pernah ada paket yang merujuk
      dependensi yang belum terbit
- [x] `Gravicode.OfficeNet.PdfNet` kini membawa satu dependensi baru,
      `System.Security.Cryptography.Pkcs` — dipakai untuk CMS tanda tangan digital
- [x] **Diverifikasi dengan mengonsumsinya dari proyek baru** lewat nuget.org: sebelas pemeriksaan
      atas fitur yang baru di 1.1.0 — text box mengambang, footnote & komentar, impor PDF→Word,
      dropdown & proteksi, streaming 50.000 baris, `XLOOKUP` dan `INDEX`/`MATCH`, SmartArt lima
      part, animasi dengan pemicu bentuk, penyematan font dengan teks yang bisa dibaca kembali,
      tanda tangan yang utuh dan yang rusak terdeteksi, serta render satu slide

### v1.0.0 — terbit di NuGet

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

## v1.2 — dua jalur boros ditemukan, dua hipotesis ditolak

Aturannya dipegang: mengukur dulu, baru mengoptimalkan.

**`PdfDocument.Split` menyalin seluruh dokumen ke setiap bagian.** Kamus halaman membawa `/Parent`,
dan `/Kids` milik parent itu menyebut setiap halaman di sumbernya. Keluarannya benar — setiap bagian
memuat halaman yang tepat — tapi juga memuat semua halaman lain sebagai objek yatim. 100 halaman:
**39.759 us → 1.129 us**, alokasi **29,9 MB → 1,3 MB**.

Yang paling berharga dari butir ini bukan perbaikannya, melainkan jebakannya: **halaman baru
mendapat `/Parent` ketika pohon halaman ditulis**, jadi dokumen yang dibangun di memori dan tidak
pernah disimpan tidak punya tautan parent untuk diikuti, dan bugnya tidak kelihatan. Tes regresi
versi pertama lulus melawan kode yang rusak. Sekarang ia bolak-balik lewat byte lebih dulu.

**Ekspor Word→PDF menggambar satu kata sekaligus.** Pemenggalan baris bekerja per kata, tapi
menggambarnya per kata membuat setiap kata membawa operator font, warna, dan posisinya sendiri —
terukur 713 byte per kata, lawan 147 byte kalau dua belas kata keluar sekali panggil. 10.000
paragraf: **449 ms → 253 ms**, alokasi **241 MB → 159 MB**, dan berkasnya **45% lebih kecil**.

Penggabungannya eksak, dan itu dibuktikan bukan didalilkan: dokumen yang sama diekspor oleh kedua
versi pada keempat perataan dengan tebal, miring, garis bawah, coret, warna, sorot, dan superskrip
berganti di tengah baris — awal, akhir, dan isi setiap baris kembali identik.

**Satu pengukuran berbohong.** Sekali jalan, benchmark melaporkan kode baru mengalokasikan 578 MB
melawan 241 MB kode lama — kebalikan dari kenyataannya. Probe langsung menjawab 158 MB dan benchmark
yang diulang menjawab 159 MB. Yang menyelamatkan bukan kecurigaan, melainkan kebiasaan menanyakan
hal yang sama kepada dua alat.

**Setiap baris mengalokasikan daftar yang tumbuh dari empat.** Setelah penggambaran diperbaiki,
fase-fase layout diinstrumentasi satu per satu. Dua yang teratas — membangun line filler (3,5 KB per
paragraf) dan mengambil baris berikutnya (1,4 KB per baris) — ternyata kesalahan yang sama: `List<T>`
mulai dari empat entri lalu berlipat ganda, jadi daftar untuk selusin kata mengalokasikan empat array
berisi dua puluh delapan slot untuk menyimpan dua belas. **10.000 paragraf: 159 MB → 123 MB.**

Yang perlu dicatat dari butir ini adalah batasnya. Daftar baris aman dipakai ulang karena hidupnya
pendek; fillernya tidak, karena ia hidup melintasi `EnsureSpace`, dan memulai halaman berarti
menggambar catatan kaki serta float tertunda yang melakukan layout sendiri. Penjaganya pun harus
dipertajam: versi pertama hanya memastikan teksnya ada, dan buffer basi lolos karena ia mengulang
kata alih-alih menghilangkannya.

**Meresolusi gaya memindai ulang seluruh bagian styles.** Instrumentasi ulang memindahkan butir
terbesar ke tempat yang semula hanya masuk "sisanya": `Resolve` menghabiskan 1,1 KB per panggilan,
dan dipanggil sekali per paragraf plus sekali per run. Menyusuri rantai `basedOn` mengalokasikan set,
iterator, dan buffer pembalikan, lalu setiap mata rantai memanggil indexer gaya yang memindai linear
seluruh gaya. Rantai yang sama dibangun puluhan ribu kali; sekarang disusuri sekali lalu disimpan.
**123 MB → 111 MB.**

**Isi satu halaman disalin empat kali dalam perjalanan keluar.** Kanvas menyusun operatornya di
`StringBuilder` lalu merangkainya jadi string, membungkusnya jadi string kedua, baru mengodekannya —
tiga salinan sebelum array byte yang benar-benar disimpan. Sekarang chunk buildernya disempitkan
langsung ke tujuan. Halaman berisi empat puluh lima baris: **52,8 KB → 37,7 KB**. Penghematannya
sebanding dengan isi halaman: Word 111 MB → 104 MB, tapi 200 slide hanya 20,6 MB → 20,1 MB.

**Melayout satu baris menyalin teks yang sedang dilayout.** Filler-nya menyimpan setiap kata sebagai
string terpotong ditambah salinan format yang sudah diresolusi — 2,8 KB per paragraf. Sekarang
sepotong baris hanya menyebut rentang di segmennya, dan teksnya baru diambil saat digambar, ketika
potongan bertetangga sudah digabung. **104 MB → 82 MB**, keluaran identik byte demi byte.

Dua hal yang hanya muncul karena diuji, bukan karena dibaca. Cabang pemangkas spasi di awal potongan
ternyata **tak pernah terjangkau** — kata dipecah sehingga spasi hanya jadi karakter terakhir, jadi
potongan berawalan spasi pasti berisi spasi saja dan dibuang utuh; dibuktikan dengan `throw` di sana
lalu menjalankan seluruh tes. Dan dua penjaga baru yang sudah ditulis ternyata **tidak menjaga apa
pun**: teks berspasi ganda tak pernah menaruh spasi di awal baris karena satu spasi masih muat di
ujung baris sebelumnya. Enam puluh spasi berurutan barulah memaksanya.

**Mengekstrak teks membangun ulang modelnya sekali per paragraf.** Membuka .docx 34 KB berisi 10.000
paragraf lalu mengekstrak teksnya: **39,7 MB, 1.205x ukuran berkasnya**. Penyebabnya bukan parsernya
melainkan propertinya — `Paragraphs` memakan 2,4 MB **setiap kali dibaca**, dan `Paragraph.Text`
menyusuri `Runs` yang juga membentuk ulang daftarnya. Ekstraksi kini menyusuri elemen langsung ke
satu builder: **biaya ekstraksinya 29,5 MB → 10,5 MB**, keluarannya identik karakter demi karakter.

**Dua hipotesis ditolak oleh pengukuran.** Lexer PdfNet: membuka PDF hanya 13,6x ukuran berkas dan
datar — bukan jalur panas. Penulisan paket: 152x dan 146x keluaran pada dua ukuran — linear, tak ada
masalah penskalaan. Keduanya tetap jadi gagasan di rencana, bukan pekerjaan.

Total sejak awal v1.2 untuk 10.000 paragraf: **241 MB → 82 MB**, berkasnya **1.161.639 → 634.591
byte**, dan kolom Gen2 pada benchmark — semula 1.000–2.000 koleksi per seribu operasi — kini kosong.
Waktunya turun dari 449 ms ke kisaran 180–210 ms; pada tahap ini ragam mesinnya sudah lebih besar
daripada selisih antar-perbaikan, jadi angka alokasi yang layak dibaca.

**Dua hipotesis ditolak oleh pengukuran**, dan itu juga hasil: alokasi ekstraksi teks PdfNet ternyata
datar di 331 KB per halaman (angka 167 MB pada benchmark adalah artefak harness-nya sendiri), dan
`Styles[styleId]` bukan jalur panas — paragraf bergaya dan polos hanya berbeda 6%.

## v1.3 — renderer

**Font tersemat.** PDF buatan pustaka ini sendiri, dengan font yang disematkan pustaka ini sendiri,
dirender oleh renderer pustaka ini: baris Devanagari keluar sebagai kotak kosong. `TextFragment`
kini membawa id glyph dan posisinya — memang harus, karena subset sengaja tidak membawa `cmap`
sehingga tidak ada jalan dari karakter kembali ke bentuknya; pemetaannya ada di `/CIDToGIDMap`.

Dua temuan yang hanya muncul karena ada yang benar-benar melihat pikselnya. Font uji `SyntheticFont`
menulis `maxp` dengan `maxPoints` dan `maxContours` nol: fontTools membaca setiap glyph-nya dengan
sempurna, rasteriser menggambar **nol piksel**. Dan penjaga pertamanya tidak menjaga apa pun — ia
hanya membandingkan "dua gambar ini berbeda", padahal itu tetap benar dengan penyematan dimatikan.

**Clipping dan gradien.** `W`/`W*` sebelumnya tidak diurai sama sekali, jadi yang seharusnya
tersembunyi tetap tergambar. Sekarang clip ikut `q`/`Q` dan **beririsan**, tidak menggantikan.
Shading aksial dan radial digambar, lewat `sh` maupun lewat isian pola. PdfNet dapat `PdfFunction`
dan `PdfShading` untuk itu.

Yang ditolak dengan sengaja, bukan dikira-kira: fungsi tipe 4 (bahasa PostScript), shading mesh
(tipe 1 dan 4–7), dan tiling pattern. Semuanya `null` sehingga pemanggilnya melewati alih-alih
menggambar warna keliru dengan percaya diri.

## Rilis v1.3.0

Tujuh paket beserta paket simbolnya, dirilis lewat tag `officenet-v1.3.0`. Isinya seluruh v1.2
(performa) dan v1.3 (renderer).

Versinya berasal dari tagnya, bukan dari berkas — workflow rilis membangun dengan
`-p:Version=` dari nama tag, jadi paket tidak mungkin terbit dengan versi yang tidak ada di
riwayat. `VersionPrefix` di `Directory.Build.props` disamakan supaya build lokal ikut menyebut
angka yang sama.

Diperiksa sebelum menandai: **setiap dependensi antar-paket menyebut 1.3.0**, bukan 1.1.0 yang
sudah ada di nuget.org. Paket yang menarik saudaranya versi lama lebih buruk daripada tidak
terbit sama sekali. Tidak ada dependensi baru dibanding 1.1.0; `Gravicode.OfficeNet.Rendering`
tetap satu-satunya yang membawa SkiaSharp, dan meta package tetap **tidak** menariknya.

**Dibuktikan setelah terbit, bukan sebelum.** Sebuah proyek yang tidak mereferensikan apa pun secara
lokal — `NuGet.config`-nya hanya menyebut nuget.org — memasang `[1.3.0]` dari nuget.org dan
menjalankan sembilan pemeriksaan: versinya benar-benar 1.3.0, ekspor Word→PDF bolak-balik, satu baris
seragam kembali sebagai **satu** fragment (perubahan v1.2), `Split` tidak lagi menyalin seluruh
dokumen, `TextFragment` membawa glyph dan offsetnya untuk font tersemat, `PdfFunction` dan
`PdfShading` publik dan berfungsi, renderer menggambar gradien **di dalam** clip-nya, `ExtractText`
membaca kembali, dan meta package tidak menyeret SkiaSharp. Kesembilannya lulus.

Indeks nuget.org tertinggal beberapa menit dari unggahannya, seperti pada 1.1.0: log workflow sudah
menyebut "Your package was pushed" untuk keempat belas berkas sementara `v3-flatcontainer` masih
hanya menampilkan 1.1.0. Yang layak dipercaya adalah paketnya benar-benar bisa dipasang, bukan
kode keluar workflow-nya.

Yang berubah bagi pemakainya:

| | |
|---|---|
| Ekspor Word→PDF | 10.000 paragraf: 241 MB → 82 MB, berkasnya 1.161.639 → 634.591 byte |
| `PdfDocument.Split` | 100 halaman: 39.759 us → 1.129 us, 29,9 MB → 1,3 MB |
| `WordDocument.ExtractText` | biaya ekstraksinya 29,5 MB → 10,5 MB |
| Renderer | memakai font tersemat berkasnya; aksara non-Latin tidak lagi jadi kotak kosong |
| Renderer | menggambar clipping path serta gradien aksial dan radial |
| PdfNet | tipe publik baru `PdfFunction` dan `PdfShading` |
| `TextFragment` | membawa `Font`, `Glyphs`, dan `GlyphOffsets` — parameter opsional, jadi kode lama tetap terkompilasi |

## v2.0 — berjalan

**Mesin HTML dipindah ke Core, dan HTML → DOCX selesai.** `HtmlToWord` memakai `HtmlFlattener`
yang sama dengan `HtmlToSlides`, jadi halaman yang dikonversi ke dokumen dan ke deck sepakat tentang
isinya. Keluarannya diperiksa dengan python-docx: gaya heading, tingkat daftar, tiga definisi
penomoran terpisah, tabel dengan header, relasi hyperlink eksternal, dan perataan.

Satu bug ditemukan sebelum terbit: dua `<ol>` bersebelahan berbagi satu definisi penomoran, jadi
daftar kedua melanjutkan yang pertama. `HtmlBlock.ListId` memperbaikinya, dan dua mutasi di kedua
ujungnya digagalkan tes yang tepat.

**DOCX → HTML selesai**, lewat `HtmlWriter` baru di Core, jadi kedua arah memakai model blok yang
sama. Round trip HTML → Word → HTML ternyata alat uji terbaiknya: ia menemukan dua cacat di
importer — bold tersirat dari `<h1>` dan gaya bawaan `<a>` ditulis sebagai format langsung — dan satu
di eksporter, daftar bersarang beda jenis yang lepas dari butirnya. Enam mutasi dicoba; lima
tertangkap, yang keenam lolos sampai tesnya ditambah.

Satu tes waktu, `AppendingParagraphsScalesLinearly`, gagal lagi di 61,5 lawan ambang 60 saat tujuh
assembly tes berjalan bersamaan. Diukur dulu sebelum disentuh: dalam isolasi biaya per paragraf datar
di 1,45–1,74 us dari 4.000 sampai 128.000 — algoritmanya linear, yang bising adalah pengukurannya,
karena kasus besar membengkak dari 98 ms ke 705 ms di bawah beban. Ambangnya dinaikkan ke 120, masih
separuh dari ~256 yang dihasilkan regresi kuadratik, dengan buktinya ditulis di komentar tesnya.

## Yang masih tersisa

**v1.1 selesai seluruhnya.** Setiap butir di bagian v1.1 [Plan.md](Plan.md) sudah dikerjakan, kecuali
ekspor video yang sengaja tidak dikerjakan dan alasannya dicatat di sana.

**v1.2 dan v1.3 selesai.** Setiap butir terukur sudah dikerjakan, dan yang tersisa di v1.2 sudah
diukur lalu **ditolak** dengan angkanya: lexer PdfNet hanya 13,6x ukuran berkas dan datar, penulisan
paket linear di ~150x keluarannya. Parser XML pull tetap jadi gagasan; kasus hanya-baca yang paling
nyata — ekstraksi teks — sudah ditangani tanpa parser kedua.

Yang tersisa adalah v2.0, dan tiga batasan yang dicatat sebagai batasan, bukan sebagai bug:
penataan aksara kompleks (GSUB/GPOS) di penulis font, tiling pattern di renderer, serta soft mask
dan transparency group.
