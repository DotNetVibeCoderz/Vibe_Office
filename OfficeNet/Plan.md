# Plan — OfficeNet

Peta jalan pengembangan. Arah, bukan jadwal: setiap butir menyebutkan *mengapa* ia ada di
posisinya, sehingga urutannya bisa diperdebatkan berdasarkan alasan, bukan selera.

[Progress.md](Progress.md) mencatat apa yang **sudah** selesai dan terverifikasi. Dokumen ini
mencatat apa yang **berikutnya** dan mengapa.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

---

## Prinsip yang mengunci keputusan

Empat hal ini menentukan hampir setiap pilihan teknis di bawah. Mengubahnya berarti mengubah
proyeknya, bukan sekadar rencananya.

1. **Tanpa dependensi native di inti.** PdfNet, WordNet, ExcelNet, PowerPointNet, dan Core tidak
   menautkan apa pun yang harus dikompilasi ulang per platform. Itulah yang membuat "hasil identik
   di Windows, Linux, macOS" bukan sekadar klaim. Rasterisasi butuh SkiaSharp, maka ia hidup di
   paket terpisah (`Gravicode.OfficeNet.Rendering`) yang boleh tidak dipakai.
2. **PdfNet adalah daun.** Word, Excel, dan PowerPoint mengekspor *melalui* PdfNet. Jangan pernah
   membuat PdfNet bergantung balik ke salah satu dari ketiganya; kalau butuh, pindahkan yang
   dibutuhkan ke Core.
3. **Diverifikasi pembaca independen.** Setiap format baru harus lolos validator yang tidak berbagi
   kode dengan library. Round-trip lewat library sendiri tidak membuktikan Office bisa membukanya.
4. **Kekurangan dicatat, bukan disembunyikan.** README dan Progress.md menyebut apa yang belum ada.
   Pengguna yang tahu batasannya bisa merencanakan; yang tidak tahu akan menemukannya saat rilis.

---

## v1.0 — **Terbit**

Dirilis ke NuGet sebagai 1.0.0 pada 8 September 2026: tujuh paket beserta symbol package. Semua
butir yang direncanakan untuk rilis pertama selesai.

| Butir | Hasil |
|---|---|
| Rasterisasi (`OfficeNet.Rendering`) | Paket terpisah; menggambar path, gambar, dan teks berwarna |
| Chart & pivot table ExcelNet | Keduanya ada; grid pivot sengaja dihitung Excel saat membuka |
| Lima aplikasi contoh | Dua CLI, satu Blazor, dua Avalonia (termasuk Gallery + asisten) |
| Dokumentasi bilingual + screenshot | 16 halaman; screenshot dihasilkan, bukan ditangkap |
| Notebook Polyglot | Lima notebook; setiap selnya dikompilasi oleh test suite |
| Benchmark | Dijalankan dan dilaporkan; menemukan dua jalur kuadratik yang lalu diperbaiki |
| CI + release workflow | Ada di akar repo; **hijau di Windows, Linux, dan macOS** |
| Registry plugin | `OfficeFormats`; dibuktikan dengan format OPC baru dari ujung ke ujung |

Yang dipelajari dan layak dibawa ke rilis berikutnya:

- **Benchmark membayar dirinya sendiri.** Dua jalur kuadratik yang tidak terlihat oleh tes mana pun
  — keluarannya benar, hanya lambat — muncul begitu ada angka.
- **Contoh kode yang dikompilasi menangkap 16 nama API yang salah** di draf pertama dokumentasi.
  Dokumentasi yang tidak dikompilasi adalah dokumentasi yang salah, hanya belum ketahuan.
- **Menguji dengan model sungguhan menemukan dua hal** yang tidak akan muncul dari membaca dokumen:
  Anthropic tidak punya konektor SK resmi, dan model bernalar menolak `temperature` selain bawaannya.

## v1.1 — Menutup kekurangan format

Setiap butir di sini adalah sesuatu yang orang *harapkan ada* dan tidak ada. Urutannya menurut
seberapa sering ketiadaannya menghentikan pekerjaan nyata.

### WordNet

- [x] **Footnote dan endnote.** Selesai. Part dibuat saat pertama dipakai, id 0 dan 1 dicadangkan
  untuk pemisah, dan menghapus catatan sekaligus menghapus rujukannya. Footnote juga digambar di
  ekspor PDF, di kaki halaman tempat rujukannya berada.
- [x] **Komentar** (`comments.xml`). Selesai, termasuk mengomentari satu run saja.
- [x] **Text box dan shape.** Selesai. `wps:wsp` di dalam `wp:anchor` atau `wp:inline`; sebelas
  geometri preset dengan nama preset apa pun sebagai string. Sepuluh atribut wajib pada `wp:anchor`
  ditulis semuanya — satu saja hilang membuat Word menyatakan dokumen tidak terbaca.
- [x] **Ekspor Word→PDF: objek mengambang.** Selesai. Baris dipenuhi satu per satu terhadap
  daftar persegi terlarang, bukan sekali di muka pada lebar tetap, sehingga teks mengalir
  mengelilingi objek. Shape digambar sebagai jalur; teksnya ditata di dalam kotaknya dan ikut
  berputar. Gambar juga bisa mengambang lewat `AddPicture(wrap:)`.
  Sisa: `Tight` mengikuti kotak pembatas dan bukan garis luar, dan satu baris dipecah
  mengelilingi satu objek, bukan beberapa.

### ExcelNet

- [x] **Data validation** (dropdown). Selesai. Tujuh builder aturan; batas 255 karakter untuk
  daftar inline ditolak di muka daripada menghasilkan file yang dropdown-nya hilang diam-diam.
- [x] **Proteksi sheet dan workbook.** Selesai. Bukan keamanan — pencegahan kesalahan.
  `Protect(editable)` membuka kunci range isian, karena setiap sel terkunci secara default. Semua
  flag ditulis eksplisit: default skemanya tidak seragam, jadi menghilangkan atribut berarti hal
  yang berbeda tergantung atributnya. Keduanya juga dibaca kembali, agar membuka lalu menyimpan
  template tidak menghapusnya.
- [x] **Streaming writer.** Selesai. `StreamingWorkbook` menulis langsung ke entri ZIP: sejuta
  baris dalam ~4 detik dengan working set 34 MB, berapa pun jumlah barisnya. API-nya memang berbeda
  — hanya menulis dan hanya maju — karena kasusnya berbeda. Nama sheet ditetapkan di muka
  (`[Content_Types].xml` harus jadi entri pertama) dan string ditulis inline (tabel shared string
  harus lengkap sebelum ditulis, dan itu justru yang dihindari).
- [x] **Formula: fungsi lookup penuh.** Selesai. Argumen range kini membawa bentuknya
  (`RangeArgument`), yang memperbaiki `VLOOKUP` untuk tabel selebar apa pun dan memungkinkan
  `HLOOKUP`, `INDEX`, `MATCH`, dan `XLOOKUP`. Wildcard `*`, `?`, dan `~` didukung. Default
  aproksimasi `VLOOKUP` sengaja dipertahankan: formula yang berbeda perilakunya di sini dan di Excel
  lebih buruk daripada yang ikut membawa jebakannya.

### PowerPointNet

- [x] **SmartArt.** Selesai. Lima part, bukan empat: `data`, `layout`, `colors`, `quickStyle`,
  ditambah part ekstensi `dsp:drawing` yang menyimpan bentuk hasil render — dan part terakhir itu
  yang menggantung di part `data`, bukan di slide. Lima jenis diagram (List, Process, Cycle,
  Hierarchy, Pyramid). `layout` adalah algoritma, bukan gambar, jadi geometrinya dihitung di sini
  dan ditulis ke part drawing; itulah yang digambar setiap konsumen sampai seseorang mengedit
  diagramnya di PowerPoint.
- [x] **Ekspor gambar.** Selesai. `RenderSlide` untuk satu slide, `RenderPdf(range)` untuk rentang
  halaman, dan `RenderToFiles` untuk dokumen hidup — sebelumnya harus disimpan ke disk dulu hanya
  untuk dirender.
- **Ekspor video.** Sengaja tidak ada. Encoding berarti FFmpeg, yang berupa biner native, sementara
  satu-satunya dependensi native `OfficeNet.Rendering` adalah SkiaSharp — dan itulah yang membuatnya
  bisa diandalkan. Dokumentasinya menunjukkan cara merangkai gambarnya sendiri dengan FFmpeg.
- [x] **Animasi lanjutan.** Selesai. Empat kelas (entrance, emphasis, exit, motion path), tiga
  pemicu berurutan plus pemicu "saat bentuk lain diklik" — yang masuk ke `interactiveSeq` sendiri,
  bukan ke sequence utama. Motion path relatif terhadap posisi bentuknya. `presetID` hanya label
  untuk panel PowerPoint; yang dimainkan adalah elemen behaviour-nya, jadi preset yang tidak
  terdokumentasi dibiarkan kosong alih-alih ditebak.

### PdfNet

- [x] **PDF → Word / PDF → Excel.** Selesai, dan hasilnya memang perkiraan seperti yang diduga.
  `PdfNet.Text.PageStructure` menyimpulkan baris dari baseline, paragraf dari jarak vertikal, tabel
  dari kolom yang sejajar, dan level heading dari ukuran huruf relatif terhadap badan teks.
  Kegagalannya dibuat terlihat: struktur yang luput kembali sebagai paragraf, dan tidak ada teks
  yang hilang. Angka yang ambigu (`1.234`) dibiarkan teks alih-alih ditebak.
- [x] **Penyematan font TrueType.** Selesai. Font komposit (`/Type0` + `/Identity-H` di atas
  `/CIDFontType2`), disubset ke glyph yang benar-benar dipakai, dengan glyph dinomori ulang rapat
  dan `/CIDToGIDMap` sebagai penerjemahnya — sembilan aksara dari font 22 MB menjadi PDF 30 KB.
  CMap `/ToUnicode` selalu ditulis, karena tanpanya teksnya tidak bisa dibaca kembali sama sekali.
  Glyph komposit membawa komponennya. Diverifikasi dengan memuat subset-nya ke SkiaSharp — parser
  yang bukan milik pustaka ini — dan menggambar seluruh 118 glyph-nya.
- [x] **Tanda tangan digital.** Selesai, keduanya. Verifikasi menjawab dua pertanyaan terpisah —
  apakah byte-nya masih menghasilkan hash yang sama, dan apakah tanda tangannya mencakup seluruh
  berkas — karena tanda tangan yang sempurna secara kriptografis atas separuh berkas adalah serangan
  yang nyata. Penandatanganan menghasilkan `adbe.pkcs7.detached` dengan SHA-256; placeholder dicari
  lewat nilai sentinel-nya sehingga dokumen yang sudah bertanda tangan bisa ditandatangani lagi
  tanpa merusak yang lama. Diverifikasi dengan openssl, bukan hanya dengan pembacanya sendiri.
  Yang tidak dihasilkan dan didokumentasikan: timestamp tepercaya dan arsip LTV, yang butuh layanan
  jaringan.

---

## v1.2 — Performa

Benchmark sudah ada dan sudah dijalankan; lihat [benchmarks/README.md](benchmarks/README.md) untuk
angkanya. Dua jalur kuadratik sudah ditemukan **dan diperbaiki** berkat pengukuran itu:

- **`WordDocument.InsertBlock` adalah O(jumlah blok) per sisipan.** `AddBeforeSelf` harus menyusuri
  daftar anak dari depan karena LINQ to XML memakai singly linked list. Membangun dokumen 10.000
  paragraf: **1.576 ms → 99 ms**.
- **Indexer tabel mengalokasikan pembungkus untuk setiap baris dan sel pada setiap akses.** Mengisi
  tabel 500 baris sel demi sel: **92 ms → 9,3 ms**, alokasi **55 MB → 7 MB**.

Keduanya dijaga `WordNet.Tests.ScalingTests`, yang membandingkan waktu N terhadap 4N alih-alih
menetapkan anggaran waktu absolut.

Yang tersisa di bawah masih *hipotesis* kecuali disebutkan sudah terukur. Mengoptimalkan sebelum
mengukur adalah cara membuang waktu pada jalur yang tidak panas.

- **Parser XML pull untuk WordNet.** ExcelNet sudah streaming; WordNet memakai `XDocument` karena
  edit-in-place membutuhkannya. Dokumen besar hanya-baca bisa memakai jalur kedua.
- **`Span<T>` di jalur panas PdfNet.** Lexer dan filter masih mengalokasikan array per objek.
- **Buffer pooling di penulisan paket.** Setiap part saat ini disalin ke `MemoryStream` sendiri.
- [x] **`PdfDocument.Split` superlinear — selesai.** Penyebabnya lebih besar daripada dugaan awal:
  bukan graf sumber daya yang diimpor ulang, melainkan **seluruh dokumen**. Kamus halaman membawa
  `/Parent`, dan `/Kids` milik parent itu menyebut setiap halaman di sumbernya — jadi mengimpor satu
  halaman menyeret semuanya. Keluarannya benar, tapi setiap bagian membawa semua halaman lain sebagai
  objek yatim.

  | Halaman | Sebelum | Sesudah | Alokasi sebelum | Alokasi sesudah |
  |---|---|---|---|---|
  | 10 | 337 us | 127 us | 417 KB | 141 KB |
  | 100 | 39.759 us | 1.129 us | 29,9 MB | 1,3 MB |

  Satu bagian satu halaman dari dokumen 100 halaman: **305 objek dan 67,5 KB → 6 objek dan 1,1 KB**.
  Perbaikannya satu baris — jangan ikuti `/Parent` saat mengimpor halaman; parent lamanya tidak ada
  artinya di dokumen baru karena `PdfPageCollection.Flush` menuliskannya ulang. Dijaga
  `PdfNet.Tests.SplitScalingTests`, yang menegaskan jumlah objek dan ukuran, bukan waktu.
- [x] **Ekspor Word→PDF menggambar satu kata sekaligus — selesai.** Pemenggalan baris memang
  bekerja per kata, tapi *menggambarnya* per kata membuat setiap kata membawa operator font, warna,
  dan posisi teksnya sendiri. Diukur dengan probe kanvas terpisah: **713 byte per kata** ketika
  setiap kata satu `DrawText`, lawan **147 byte** ketika dua belas kata keluar dalam satu panggilan.

  Sekarang potongan yang berurutan dan formatnya sama digambar sebagai satu. Ini eksak, bukan
  hampiran: `SplitWords` menyimpan spasi di ekor setiap kata sehingga penyambungannya mengembalikan
  teks aslinya karakter demi karakter, dan lebar sebuah potongan adalah jumlah lebar majunya
  masing-masing glyph sehingga posisinya identik. Baris rata kanan-kiri tetap menempatkan setiap
  kata sendiri, karena spasinya diregangkan.

  | Paragraf | Waktu sebelum | Sesudah | Alokasi sebelum | Sesudah | PDF sebelum | Sesudah |
  |---|---|---|---|---|---|---|
  | 100 | 4,4 ms | 2,6 ms | 2,61 MB | 1,78 MB | 12.636 B | 7.298 B |
  | 1.000 | 47 ms | 20 ms | 24,1 MB | 16,0 MB | 116.750 B | 64.038 B |
  | 10.000 | 449 ms | 253 ms | 241 MB | 159 MB | 1.161.639 B | 634.591 B |

  Berkasnya **45% lebih kecil** dengan tampilan yang sama persis — dibuktikan dengan membandingkan
  posisi dan isi setiap baris terhadap kode lama pada keempat perataan dan seluruh format, dan
  hasilnya identik. Dijaga `WordNet.Tests.RunMergingTests`.

  Dua hipotesis lain di jalur yang sama **ditolak oleh pengukuran**: alokasi ekstraksi teks PdfNet
  ternyata datar di 331 KB per halaman (angka 167 MB di benchmark adalah artefak harness), dan
  pencarian `Styles[styleId]` bukan jalur panas — paragraf bergaya dan polos hanya beda 6%.
- **Rust untuk kernel level rendah.** Spesifikasi mengizinkannya. Kandidat paling masuk akal:
  inflate/deflate dan predictor PNG. Benchmark saat ini **tidak** menunjukkan keduanya sebagai
  hambatan — ekspor PDF Word (253 ms untuk 10.000 paragraf) didominasi layout, bukan kompresi —
  jadi butir ini turun peringkat sampai ada pengukuran yang membenarkan dua toolchain build.

---

## v1.3 — Renderer

`OfficeNet.Rendering` menggambar jalur, gambar, dan teks, tetapi mencari font di sistem berdasarkan
namanya alih-alih memakai font yang tersemat di dalam berkasnya. Itu batasan yang sudah ada sejak
awal dan sudah didokumentasikan — tapi sejak PdfNet bisa menyematkan font, akibatnya jadi lebih
terasa: PDF yang dibuat pustaka ini sendiri dengan aksara non-Latin dirender sebagai kotak kosong,
karena font penggantinya tidak punya glyph-nya.

- **Pakai font yang tersemat.** `TextFragment` perlu membawa id glyph-nya, bukan hanya teks hasil
  dekode, karena subset yang ditulis PdfNet sengaja tidak membawa `cmap` — pemetaan karakternya ada
  di PDF-nya, bukan di fontnya. Setelah itu SkiaSharp bisa memuat `FontFile2`-nya langsung.
- **Gradien, pattern, dan clipping.** Ketiganya diabaikan sekarang.

Sampai itu ada, renderer ini tetap untuk thumbnail dan pratinjau, bukan penampil dokumen.

## v2.0 — Perluasan

- **VisioNet** (`.vsdx`) dan **OneNoteNet** (`.one`). Keduanya OPC, jadi Core sudah menanganinya;
  yang baru adalah model dokumennya. Registry plugin di `Gravicode.OfficeNet` adalah tempat
  keduanya mendaftar.
- **ODF** (`.odt`, `.ods`, `.odp`). Kontainer berbeda (zip tanpa `[Content_Types].xml`), model
  serupa. Nilainya: interoperabilitas dengan LibreOffice, yang dipakai luas di instansi.
- **Konversi dua arah HTML.** Sekarang HTML→PPTX. Arah sebaliknya, dan HTML→DOCX, memakai mesin
  yang sama.

---

## Yang sengaja tidak akan dikerjakan

Menyebutkan ini sama pentingnya dengan menyebutkan rencana: ia mencegah orang menunggu sesuatu
yang tidak akan datang.

- **Mesin layout setara Word.** Ekspor PDF sudah cukup untuk laporan yang dibuat program. Meniru
  hifenasi, kerning, dan penempatan objek mengambang Word secara persis adalah proyek bertahun-tahun
  dan hasilnya tetap akan berbeda.
- **Format biner lama** (`.doc`, `.xls`, `.ppt`). OLE compound document adalah format berbeda
  sepenuhnya. Konversi lebih baik dilakukan alat yang memang untuk itu.
- **Perhitungan ulang formula seperti Excel.** Mesin formula ada untuk mengisi cache sehingga
  konsumen non-Excel melihat angka. Menargetkan kesetaraan dengan 500+ fungsi Excel, beserta
  array formula dan iterative calculation, bukan tujuan yang sepadan.
- **Rendering di server tanpa font.** Metrik 14 font baku sudah tertanam, tapi rasterisasi
  membutuhkan font sungguhan. Kontainer tanpa font akan menghasilkan gambar dengan glyph pengganti,
  dan itu masalah lingkungan, bukan bug library.
