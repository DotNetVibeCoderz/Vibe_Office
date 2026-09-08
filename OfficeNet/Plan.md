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
- **Text box dan shape.** Saat ini hanya gambar inline; brosur dan formulir butuh objek mengambang.
- **Ekspor Word→PDF: objek mengambang.** Mesin layout sekarang mengalir; gambar dengan
  `wrap="square"` dirender inline dan posisinya salah.

### ExcelNet

- **Data validation** (dropdown). Template yang diisi manusia hampir selalu memerlukannya.
- **Proteksi sheet dan workbook.** Bukan keamanan — pencegahan kesalahan.
- **Streaming writer.** Model sekarang menaruh seluruh workbook di memori. Ekspor sejuta baris
  butuh penulis yang tidak pernah memegang lebih dari satu baris. API-nya akan berbeda dan itu
  wajar: kasusnya juga berbeda.
- **Formula: fungsi lookup penuh** (`INDEX`/`MATCH`, `XLOOKUP`). `VLOOKUP` sekarang mengasumsikan
  bentuk tabel dua kolom karena range tiba dalam bentuk datar; memperbaikinya butuh range
  mempertahankan bentuknya sampai ke fungsi.

### PowerPointNet

- **SmartArt.** Sering diminta, dan formatnya besar: satu diagram adalah empat part yang saling
  merujuk.
- **Ekspor gambar/video.** Gambar tinggal memakai `OfficeNet.Rendering`; video butuh FFMPEG dan
  karena itu paket terpisah lagi.
- **Animasi lanjutan.** Sekarang hanya urutan klik. Motion path dan trigger butuh pohon timing
  SMIL penuh.

### PdfNet

- **PDF → Word / PDF → Excel.** Ekstraksi teks sudah ada; yang kurang adalah rekonstruksi
  struktur — mengenali paragraf, tabel, dan kolom dari posisi glyph. Ini masalah riset kecil, bukan
  sekadar penulisan kode, dan hasilnya akan selalu perkiraan.
- **Penyematan font TrueType.** Sekarang teks dipetakan ke 14 font baku. Dokumen berbahasa yang
  tidak tercakup Latin-1 butuh font sungguhan disematkan.
- **Tanda tangan digital.** Verifikasi lebih dulu, penandatanganan kemudian.

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
- **`PdfDocument.Split` superlinear — terukur.** 10 halaman 0,7 ms, 100 halaman 69 ms; seharusnya
  ~7 ms. Setiap bagian mengimpor ulang graf sumber daya bersama alih-alih mengimpornya sekali.
  Ini satu-satunya butir di bagian ini yang sudah punya angka, jadi ia yang pertama.
- **Rust untuk kernel level rendah.** Spesifikasi mengizinkannya. Kandidat paling masuk akal:
  inflate/deflate dan predictor PNG. Benchmark saat ini **tidak** menunjukkan keduanya sebagai
  hambatan — ekspor PDF Word (477 ms untuk 10.000 paragraf) didominasi layout, bukan kompresi —
  jadi butir ini turun peringkat sampai ada pengukuran yang membenarkan dua toolchain build.

---

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
