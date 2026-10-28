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

  **Sudah diukur, dan kasus utamanya sudah ditangani tanpa parser kedua.** Membuka .docx 34 KB berisi
  10.000 paragraf lalu mengekstrak teksnya mengalokasikan **39,7 MB — 1.205x ukuran berkasnya**.

  | | Alokasi | Rasio ke berkas |
  |---|---|---|
  | Buka, tidak disentuh | 10,2 MB | 311x |
  | Buka + `Paragraphs.Count` | 12,6 MB | 383x |
  | Buka + `Paragraphs.Count` lima kali | 22,2 MB | 673x |
  | Buka + `ExtractText` | 39,7 MB | 1.205x |
  | (unzip + `XDocument.Load` saja) | 4,5 MB | 135x |

  Baris tengahnya yang penting: `Paragraphs` memakan **2,4 MB setiap kali dibaca** — ia membentuk
  ulang daftar dan pembungkus baru per paragraf pada setiap akses, bentuk yang sama persis dengan bug
  indexer tabel, di properti yang paling sering dipakai. `ExtractText` menyusurinya, lalu memanggil
  `Paragraph.Text` yang menyusuri `Runs` — daftar dan pembungkus baru lagi per run — dan
  mengembalikan string yang langsung ditempel ke builder lain lalu dibuang.

  Ekstraksi kini menyusuri elemen body langsung ke satu builder. **Biaya `ExtractText` sendiri:
  29,5 MB → 10,5 MB**, dan sisanya mendekati yang memang tak terhindarkan: teks dokumennya sekitar
  3 MB sebagai UTF-16 dan hasilnya satu string. Dijaga `WordNet.Tests.TextExtractionTests`, yang
  memaku keluarannya karakter demi karakter.

  Sisa 10,2 MB untuk membuka saja adalah harga model `XDocument` yang memang sengaja dipilih. Parser
  kedua masih bisa menghapusnya, tapi kasus hanya-baca yang paling nyata sudah tertangani.
- **`Span<T>` di jalur panas PdfNet.** Lexer dan filter masih mengalokasikan array per objek.
  **Diukur dan ditolak untuk lexernya:** membuka PDF mengalokasikan 13,6x ukuran berkas pada 50
  halaman dan 13,7x pada 200 — kecil dan datar. Lexernya bukan jalur panas. Yang mahal saat membaca
  PDF adalah ekstraksi teks, datar di 242 KB per halaman, dan itu jalur kode yang lain.

  Bagian penulisannya sudah dikerjakan: **isi satu halaman disalin empat kali dalam perjalanan keluar.** Kanvas
  menyusun operatornya di `StringBuilder`, lalu menyerahkannya sebagai
  `Encoding.Latin1.GetBytes("q
" + _content + "Q
")` — rangkaian itu meminta string dari builder,
  membangun string kedua di sekelilingnya, lalu mengodekannya. Sekarang chunk milik buildernya
  disempitkan langsung ke array tujuan; operator content stream memang Latin-1, jadi penyempitan itu
  sendiri sudah merupakan pengodeannya.

  | Satu halaman A4 | Alokasi sebelum | Sesudah |
  |---|---|---|
  | Kanvas kosong | 9,7 KB | 9,7 KB |
  | Satu baris teks | 13,0 KB | 12,7 KB |
  | Empat puluh lima baris | 52,8 KB | 37,7 KB |

  Penghematannya sebanding dengan isi halaman, dan itu perlu dikatakan terus terang: halaman padat
  untung, halaman kosong hampir tidak. Ekspor 10.000 paragraf Word 111 MB → 104 MB; ekspor 200 slide
  20,6 MB → 20,1 MB. Dibuktikan dengan **hash**, bukan penalaran — dokumen yang mencakup keempat
  perataan dan seluruh format diekspor dua kali lalu setiap content stream halaman di-hash, dan
  keenamnya sama. Dijaga `PdfNet.Tests.CanvasContentTests`.
- **Buffer pooling di penulisan paket.** Setiap part saat ini disalin ke `MemoryStream` sendiri.
  **Diukur, dan tidak ada yang salah di sini:** menulis workbook mengalokasikan 152x ukuran keluaran
  pada 10.000 baris dan 146x pada 50.000 — linear, tanpa masalah penskalaan. Apakah pooling membantu
  adalah pertanyaan terpisah dari apakah biayanya sekarang keliru, dan tidak ada angka yang bilang
  begitu. Tetap sebagai gagasan, bukan pekerjaan.
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
- [x] **Setiap baris mengalokasikan daftar yang tumbuh dari empat — selesai.** Setelah penggambaran
  diperbaiki, 140 MB dari 159 MB sisanya ada di layout. Fasenya diinstrumentasi, bukan ditebak:

  | Fase | Alokasi | Per satuan |
  |---|---|---|
  | Membangun line filler | 35,1 MB | 3,5 KB / paragraf |
  | Mengambil baris berikutnya | 29,3 MB | 1,4 KB / baris |
  | Memulai halaman | 16,1 MB | 35 KB / halaman |
  | Membangun segmen | 15,3 MB | 1,5 KB / paragraf |
  | Menggambar baris | 15,1 MB | 740 B / baris |

  Dua teratas adalah kesalahan yang sama di dua tempat. `List<T>` mulai dari empat entri lalu
  berlipat ganda, menyalin dan membuang arraynya setiap kali — daftar untuk selusin kata
  mengalokasikan empat array berisi total dua puluh delapan slot untuk menyimpan dua belas.

  Daftar barisnya kini diberikan oleh pemanggil sehingga dipakai ulang untuk seluruh dokumen, dan
  daftar kata milik filler diberi kapasitas di muka dari panjang teksnya. **10.000 paragraf:
  253 ms → 235 ms, 159 MB → 123 MB.**

  **Pemakaian ulang butuh argumen, bukan harapan.** Daftar baris aman dipakai bersama karena ia
  hidup hanya dari saat kata diambil sampai saat digambar. Fillernya **tidak** aman: ia hidup
  melintasi `EnsureSpace`, dan memulai halaman berarti menggambar catatan kaki dan float tertunda
  halaman itu, yang melakukan layout sendiri. Memakai ulang filler akan merusak dokumen yang
  berganti halaman sambil membiarkan setiap tes kecil tetap hijau.

  Penjaganya sendiri harus dipertajam dulu: versi pertamanya hanya memastikan teks setiap paragraf
  ada di keluaran, dan buffer basi lolos begitu saja karena ia mengulang kata, bukan menghilangkannya.
  Sekarang ia menghitung kemunculan, dan `Clear()` yang sengaja dihapus membuatnya gagal.
- [x] **Meresolusi gaya paragraf memindai ulang seluruh bagian styles — selesai.** Setelah daftarnya
  diperbaiki, instrumentasi ulang memindahkan butir terbesar ke tempat yang semula hanya masuk
  kategori "sisanya": `Resolve`, yang menghitung format efektif sebuah run, menghabiskan 1,1 KB per
  panggilan — dan ia dipanggil sekali per paragraf plus sekali per run.

  Dua hal di dalamnya. Menyusuri rantai `basedOn` mengalokasikan set penangkap siklus, iterator, dan
  buffer untuk pembalikannya; lalu setiap mata rantai memanggil indexer gaya, yang memindai linear
  seluruh gaya di part itu dan membungkus elemen yang ditemukannya. Dokumen punya segelintir gaya dan
  sangat banyak paragraf, jadi tiga-empat rantai yang sama dibangun puluhan ribu kali.

  Sekarang setiap rantai disusuri sekali lalu disimpan, sudah berurutan dari akar sehingga tak ada
  pemanggil yang perlu membalikkannya. **10.000 paragraf: 123 MB → 111 MB, 235 ms → 228 ms.**

  Menyimpan rantai hanya sahih kalau yang disimpan sama dengan yang akan dihasilkan penyusuran, jadi
  `WordNet.Tests.StyleChainTests` memaku dua sifat yang mudah hilang: pewarisan berlaku dari akar,
  dan rantai yang menunjuk balik ke dirinya berhenti alih-alih menggantung. Menghapus pembalikannya
  menggagalkan yang pertama; mengunci kunci cache ke satu konstanta menggagalkan yang kedua.

  **Total v1.2 untuk ekspor Word→PDF 10.000 paragraf: 241 MB → 82 MB, berkas 1.161.639 → 634.591
  byte** — dan kolom Gen2 benchmark yang semula 1.000–2.000 koleksi per seribu operasi kini kosong
  sama sekali. Waktunya turun dari 449 ms ke kisaran 180–210 ms, tapi pada tahap ini ragamnya sudah
  lebih besar daripada selisih antar-perbaikan, jadi angka alokasi yang layak dibaca.
- [x] **Melayout satu baris menyalin teks yang sedang dilayout — selesai.** Pemenggalan baris bekerja
  per kata, jadi filler-nya menyimpan daftar kata. Setiap entrinya adalah kata itu yang dipotong jadi
  string sendiri, ditambah salinan format yang sudah diresolusi, fontnya, dan lebarnya — sekitar
  **2,8 KB per paragraf**, dan butir tunggal terbesar yang tersisa.

  Sekarang sepotong baris hanya menyebut rentang: segmen mana, mulai di mana, sepanjang apa, selebar
  berapa. Teksnya baru diambil saat digambar, dan saat itu potongan bertetangga biasanya sudah
  digabung — jadi satu substring per run, bukan satu per kata.

  | Paragraf | Alokasi sebelum | Sesudah |
  |---|---|---|
  | 100 | 1,23 MB | 1,01 MB |
  | 1.000 | 10,45 MB | 8,26 MB |
  | 10.000 | 104 MB | 82 MB |

  Penggabungannya sekalian jadi lebih sederhana: kata bertetangga dalam satu segmen memang satu
  rentang utuh, jadi syaratnya cukup **segmen sama dan bersambung** — tidak perlu membandingkan font,
  warna, dan dekorasi satu per satu, dan tidak ada kemungkinan perbandingan itu kurang lengkap.
  Keluarannya identik byte demi byte, dibuktikan dengan hash setiap content stream halaman.

  **Sebuah cabang yang tidak bisa dijalankan.** Memangkas spasi di depan sepotong baris tampak perlu,
  dan sudah ada sejak versi string. Ternyata tak terjangkau: kata dipecah sehingga spasi hanya pernah
  menjadi karakter **terakhir** sebuah potongan, jadi potongan yang diawali spasi tidak berisi apa pun
  selain spasi dan dibuang utuh. Dibuktikan dengan menaruh `throw` di cabang itu lalu menjalankan
  seluruh tes — bukan dengan berargumen — dan invariannya kini ditulis di tempat kodenya bersandar.

  **Dua penjaga yang tidak menjaga apa pun.** Versi pertama `LinePieceRangeTests` memakai teks
  berspasi ganda, padahal satu spasi hampir selalu masih muat di ujung baris sebelumnya, jadi tak ada
  yang pernah mendarat di awal baris dan menghapus pengamannya pun tetap lulus. Dengan enam puluh
  spasi berurutan barulah ia gagal sebagaimana mestinya. Yang menemukan ini adalah uji mutasi;
  tesnya sendiri terbaca meyakinkan.
- **Rust untuk kernel level rendah.** Spesifikasi mengizinkannya. Kandidat paling masuk akal:
  inflate/deflate dan predictor PNG. Benchmark saat ini **tidak** menunjukkan keduanya sebagai
  hambatan — ekspor PDF Word (181 ms untuk 10.000 paragraf) didominasi layout, bukan kompresi —
  jadi butir ini turun peringkat sampai ada pengukuran yang membenarkan dua toolchain build.

---

## v1.3 — Renderer

`OfficeNet.Rendering` menggambar jalur, gambar, dan teks, tetapi mencari font di sistem berdasarkan
namanya alih-alih memakai font yang tersemat di dalam berkasnya. Itu batasan yang sudah ada sejak
awal dan sudah didokumentasikan — tapi sejak PdfNet bisa menyematkan font, akibatnya jadi lebih
terasa: PDF yang dibuat pustaka ini sendiri dengan aksara non-Latin dirender sebagai kotak kosong,
karena font penggantinya tidak punya glyph-nya.

- [x] **Pakai font yang tersemat — selesai.** `TextFragment` kini membawa id glyph dan posisinya di
  sepanjang baseline, bukan hanya teks hasil dekode. Itu memang harus: subset yang ditulis PdfNet
  sengaja tidak membawa `cmap`, jadi tidak ada jalan dari karakter kembali ke bentuk glyph-nya —
  pemetaannya ada di PDF (`/CIDToGIDMap`), bukan di fontnya.

  `PdfFontInfo` sekarang mengekspos program font yang tersemat (`FontFile2`) beserta `GlyphOf`, dan
  renderer memuatnya langsung ke SkiaSharp lalu menggambar per id glyph. Posisinya diambil dari lebar
  di berkasnya sendiri, bukan dari metrik fontnya, karena itulah yang dipakai produsernya saat
  menyusun baris.

  Dibuktikan dengan gambar: baris Devanagari yang sebelumnya keluar sebagai kotak kosong kini
  tergambar sebagai aksara sungguhan, dan huruf Latinnya berubah bentuk dari font pengganti sistem
  ke font di dalam berkasnya. Satu keuntungan lain menyusul: run yang tak punya `/ToUnicode` sama
  sekali dulu tidak menghasilkan fragment apa pun sehingga hilang dari hasil render — sekarang ia
  tetap tergambar.

  **Font uji ternyata bukan font sungguhan.** `SyntheticFont` di TestKit menulis `maxp` versi 1.0
  dengan `maxPoints` dan `maxContours` bernilai nol. fontTools membaca setiap glyph-nya dengan
  sempurna; rasteriser mana pun menggambar **nol piksel**, karena buffer titiknya dialokasikan dari
  angka itu. Ini kegagalan yang lebih buruk daripada font rusak: semua tes struktural lulus dan hanya
  piksel yang tidak setuju. Ditemukan hanya karena ada tes yang benar-benar melihat pikselnya.

  Dijaga `OfficeNet.Rendering.Tests.EmbeddedFontRenderingTests`. Penjaganya sendiri sempat salah:
  versi pertama hanya membandingkan "dua gambar ini berbeda", padahal mengganti font sintetis dan
  mengganti Helvetica juga menghasilkan dua gambar berbeda — jadi ia tetap lulus dengan penyematan
  dimatikan total. Yang membedakan adalah **bentuk**: glyph `SyntheticFont` adalah persegi pejal
  sehingga mengisi ~67% kotak batasnya, sedangkan huruf sungguhan hanya ~27%.

  Yang **belum** ditangani: penataan aksara kompleks. PDF menyimpan glyph yang sudah tertata, dan
  penulis font di PdfNet memetakan karakter ke glyph tanpa menerapkan GSUB/GPOS, jadi matra
  Devanagari berdiri sendiri alih-alih menyatu. Itu batasan penulisnya, bukan renderernya — renderer
  menggambar persis apa yang tertulis di berkasnya.
- [x] **Gradien dan clipping — selesai. Tiling pattern tidak.**

  **Clipping.** `W` dan `W*` sebelumnya tidak diurai sama sekali, jadi apa pun yang seharusnya
  disembunyikan tetap tergambar — kegagalan yang menggambar terlalu banyak, bukan terlalu sedikit.
  Sekarang clip menjadi bagian dari graphics state, ikut disimpan `q` dan dipulihkan `Q`, dan
  **beririsan** dengan clip sebelumnya, tidak menggantikannya. Diterapkan pada path maupun gambar;
  memotong foto dengan clip adalah cara biasa produsen menempatkannya dalam bingkai.

  **Gradien.** Shading tipe 2 (aksial) dan 3 (radial) digambar, baik lewat operator `sh` maupun
  lewat isian pola (`/Pattern cs` + `scn`). Isian pola dulu jatuh ke warna datar terakhir — biasanya
  hitam — menutupi seluruh bentuknya.

  Untuk itu PdfNet dapat dua hal baru: `PdfFunction` (tipe 0 tersampel, 2 eksponensial, 3 stitching)
  dan `PdfShading`, yang menyederhanakan semuanya jadi geometri plus tangga warna 64 langkah. Tangga
  itulah yang dimau Skia, dan menyamplingnya sekali di PdfNet membuat interpolasinya sama untuk
  semua konsumen.

  **Yang sengaja ditolak, bukan dikira-kira:**

  | | Alasan |
  |---|---|
  | Fungsi tipe 4 | Bahasa kalkulator PostScript; butuh interpreter tersendiri |
  | Shading tipe 1, 4–7 | Mesh; menggambarnya sebagai ramp linear adalah jawaban salah yang tampak masuk akal |
  | Tiling pattern (tipe 1) | Satu content stream yang dicap berulang |

  Semuanya mengembalikan `null` sehingga pemanggilnya melewati, bukan menggambar warna yang keliru
  dengan percaya diri.

  Dijaga `PdfNet.Tests.FunctionAndShadingTests` (aritmetiknya, tanpa Skia) dan
  `OfficeNet.Rendering.Tests.ClipAndGradientTests` (pikselnya). Dipisah dengan sengaja: gradien yang
  warnanya salah bisa berasal dari aritmetik atau dari penggambaran, dan memisahkannya adalah beda
  antara tes yang menyebut mana dan tes yang bilang "halamannya salah". Keempat mutasi yang dicoba —
  clip mengganti alih-alih beririsan, clip tak pernah diterapkan, `Extend` diabaikan, isian pola
  jadi warna datar — masing-masing digagalkan oleh tes yang memang untuk itu.

Yang masih tidak digambar: tiling pattern, soft mask, transparency group, blend mode, dan mesh
shading. Renderer ini tetap untuk thumbnail dan pratinjau, bukan penampil dokumen — tapi batasnya
sekarang jauh lebih sempit daripada saat v1.3 dimulai.

## v2.0 — Perluasan

- **VisioNet** (`.vsdx`) dan **OneNoteNet** (`.one`). Keduanya OPC, jadi Core sudah menanganinya;
  yang baru adalah model dokumennya. Registry plugin di `Gravicode.OfficeNet` adalah tempat
  keduanya mendaftar.
- **ODF** (`.odt`, `.ods`, `.odp`). Kontainer berbeda (zip tanpa `[Content_Types].xml`), model
  serupa. Nilainya: interoperabilitas dengan LibreOffice, yang dipakai luas di instansi.
- **Konversi dua arah HTML.** Sekarang HTML→PPTX. Arah sebaliknya, dan HTML→DOCX, memakai mesin
  yang sama.

  - [x] **Mesinnya dipindah ke Core.** `HtmlParser`, `HtmlNode`, dan `CssStyle` pindah dari
    `PowerPointNet.Html` ke `OfficeNet.Core.Html`, ditambah `HtmlFlattener` — langkah yang mengubah
    HTML menjadi daftar blok berformat, yang tadinya tertanam privat di `HtmlToSlides`. Tanpa itu
    WordNet tidak bisa membaca HTML tanpa bergantung pada saudaranya, dan dua pembaca terpisah akan
    berbeda pendapat tentang halaman yang sama. Ini **perubahan yang memutus kompatibilitas** bagi
    siapa pun yang memakai ketiga tipe itu langsung dari `PowerPointNet.Html`; `HtmlToSlides`
    sendiri tidak berubah. Pantas untuk sebuah versi mayor. Keselarasan perataan antar-pustaka lewat
    `TextAlign` baru di Core, dipetakan eksplisit — bukan cast — ke enum tiap format.
  - [x] **HTML → DOCX — selesai.** `WordNet.Import.HtmlToWord`: heading ke gaya heading, daftar ke
    daftar Word sungguhan dengan tingkatnya, tabel, gambar, `pre` dengan pemisah barisnya, `hr`
    sebagai garis bawah paragraf, format inline run demi run. Diperiksa dengan pembaca independen
    (python-docx), bukan hanya dengan pustaka ini sendiri.

    **Bug yang ditemukan di jalan:** dua `<ol>` bersebelahan dinomori sebagai satu daftar — "1, 2"
    lalu "3". Word menomori per definisi, bukan per posisi, dan daftar datar dari flattener tidak
    bisa bilang di mana satu daftar berakhir bila tak ada blok di antaranya. `HtmlBlock.ListId` kini
    membawa daftar asal setiap butir. Komentar di kodenya sudah mengklaim ini dicegah sebelum benar-
    benar dicegah. Dua mutasi — importer mengabaikan `ListId`, flattener memberi semua daftar id yang
    sama — masing-masing digagalkan tes yang ditulis untuk itu.

    **Konverter slide punya masalah serupa dalam bentuk lain, dan belum ditangani:** `buAutoNum`
    meneruskan penomoran lintas paragraf bernomor yang berurutan dan tidak punya atribut "mulai
    ulang". Ini menurut model DrawingML-nya; belum diperiksa di PowerPoint sendiri.
  - [x] **DOCX → HTML — selesai.** `WordNet.Export.WordToHtml`, lewat `HtmlWriter` baru di Core,
    jadi kedua arah memakai model blok yang sama: `HtmlFlattener` masuk, `HtmlWriter` keluar.
    Heading dari gayanya, daftar dari definisi penomorannya dan bersarang di dalam butirnya, daftar
    bernomor yang disela melanjutkan nomornya lewat `start`, tabel dengan header, gambar sebagai URI
    `data:`, format inline run demi run. Tautan disaring: skema di luar daftar izin — `javascript:`
    misalnya — kehilangan tautannya, karena HTML yang ditulis dari dokumen bisa saja disajikan.
    Keluarannya diperiksa dengan `html.parser` Python untuk keseimbangan tag dan sarang daftar yang
    sah, bukan dengan penulisnya sendiri.

    **Round trip menemukan dua cacat di importer, bukan di eksporter.** Bold yang tersirat dari
    `<h1>` dan biru-bergaris-bawah bawaan `<a>` ditulis sebagai format langsung, jadi setiap heading
    dan tautan membawa format yang tidak bisa diubah lewat gayanya, lalu keluar lagi sebagai
    `<strong>` dan `<span>` yang menyasar. Keduanya pernah terlihat di keluaran python-docx lebih
    awal dan sempat dianggap tidak berbahaya; ternyata berbahaya. Satu cacat di eksporter: daftar
    dikunci per id penomoran, sehingga bullet yang bersarang di bawah nomor — yang punya definisinya
    sendiri — menutup daftar luarnya alih-alih bersarang di dalam butirnya.

    Setiap perilaku baru diuji mutasi, dan lima dari enam langsung digagalkan tes yang tepat. Yang
    keenam — warna tautan bocor ke format langsung — lolos, jadi ditambah tes yang memeriksa run
    tautan di XML-nya, beserta tes kebalikannya agar tautan yang sengaja diwarnai tetap berwarna.

    Yang tidak ikut terbawa: format dari gaya selain heading, struktur di dalam sel tabel, posisi
    gambar di dalam paragrafnya, dan `pre` yang kembali sebagai paragraf monospace, bukan `pre`.
  - [ ] **PPTX → HTML.** Tinggal memetakan slide ke blok; penulisnya sudah ada.

---

## v2.1 — Celah fitur terhadap pustaka komersial

Daftar fitur GemBox.Document/Spreadsheet/Presentation/Pdf (`reference.md`) dicocokkan dengan kode
yang ada pada 28 Oktober 2026 — dengan grep atas API publik dan penulis XML-nya, bukan dengan
membaca README. Yang tercantum di sini adalah yang **belum ada dan bisa diwujudkan tanpa melanggar
keempat prinsip di atas**. Yang tidak bisa dicatat di bagian "Yang sengaja tidak akan dikerjakan".

Urutannya: kehilangan data dulu, lalu yang paling sering menghentikan pekerjaan nyata, lalu yang
melengkapi.

### Didahulukan: kehilangan data diam-diam

- [x] **Komentar sel ExcelNet tidak pernah ditulis — selesai.** `Cell.Comment` disimpan di model
  tetapi `SheetXml` tidak menulis part `comments` maupun VML-nya, dan tidak membacanya kembali.
  Sekarang `SheetComments` menulis keduanya plus `legacyDrawing`, membacanya saat dibuka (termasuk
  komentar rich text buatan Excel, tanpa bacaan fonetik `rPh`), dan menghapus keduanya bila komentar
  terakhir dihapus — VML yang berisi selain catatan dibiarkan. Id shape VML dibagikan per blok
  `o:idmap` di tingkat workbook, karena keunikannya lintas sheet, bukan per sheet.

  **Tiga temuan di jalan.** Pertama, **hyperlink juga hilang**: ditulis tetapi tidak pernah dibaca,
  jadi buka-lalu-simpan menghapus setiap tautan. Sekarang dibaca, termasuk `ref` berupa range dan
  tautan internal (`#Sheet2!B2` → atribut `location`, bukan relasi). Kedua, **`sheet["A1"].Comment =
  …` tidak bisa dikompilasi** — `Cell` adalah `readonly struct` dari indexer, jadi setter-nya hanya
  terjangkau lewat variabel lokal. Itu mungkin sebabnya bug ini tak pernah ketahuan; `WithComment`
  dan `WithHyperlink` kini ada. Ketiga, `System.Security.Cryptography.Pkcs` 10.0.0 kini punya
  advisory **high** (GHSA-846f-f764-76mx), yang dengan `-warnaserror` membuat CI merah; dinaikkan ke
  10.0.14.

  Validator kini menolak part komentar tanpa `legacyDrawing` dan id shape VML ganda lintas sheet.
  Empat mutasi — tanpa `legacyDrawing`, tanpa pembaca hyperlink, satu blok idmap untuk semua sheet,
  `rPh` ikut terbaca — masing-masing digagalkan tes; yang keempat baru setelah fixture-nya diberi
  `rPh`. openpyxl membaca kembali komentar di dua sheet, tautan eksternal, dan `location` internal.
  Yang **belum** dibuktikan: tampilan catatannya di Excel sendiri — tidak ada Excel di mesin ini.

### WordNet

- [x] **Track changes — selesai.** `WordDocument.Revisions` membaca sepuluh jenis revisi di body,
  header, footer, catatan, dan komentar; `Accept`/`Reject` per revisi dan `AcceptAll`/`RejectAll`;
  `AddTrackedRun`, `Run.TrackDeletion`/`TrackInsertion`, `Paragraph.TrackDeletion` (run beserta
  tanda paragrafnya), dan `TrackRevisions` untuk sakelar Word.

  **Pemeriksaan "entah bagaimana" itu menemukan bug yang lebih besar dari fiturnya:** run di dalam
  `w:ins` sama sekali tidak terlihat. `Paragraph.Runs` hanya turun ke `w:hyperlink`, jadi setiap
  sisipan terlacak — juga isi content control (`w:sdt`) dan `w:fldSimple` — hilang dari `Text`,
  `ExtractText`, ekspor PDF, dan ekspor HTML. Keempatnya kini lewat satu penelusur,
  `ParagraphContent`, yang menetapkan **tampilan final**: sisipan tampil, hapusan tidak.

  Satu nama elemen, tiga arti: `w:ins` di sekeliling run adalah teks, di dalam `w:pPr/w:rPr` adalah
  pemisah paragraf, di dalam `w:trPr` adalah baris. Klasifikasinya menurut induk, dan menerima tanda
  paragraf yang dihapus menggabungkan isinya ke paragraf berikut, yang mempertahankan propertinya
  sendiri — seperti Word. Penggabungan itu harus **memindahkan** node, bukan menyalinnya: revisi
  yang masih tertunda di isi yang dipindah memegang node itu, dan LINQ to XML diam-diam menyalin
  elemen yang masih punya induk.

  **Dua cacat skema lama ikut ditemukan.** `w:pPr` bisa tertulis *setelah* `w:ins` karena daftar
  penerusnya tidak menyebut `w:ins`; dan `updateFields`/`evenAndOddHeaders` ditulis dengan
  `AddFirst` di depan `zoom`, padahal `CT_Settings` adalah sequence. Settings kini lewat
  `DocumentSettings` dengan urutan skema lengkap. Validator kini memeriksa `w:pPr` sebagai anak
  pertama, `w:t` tidak di dalam hapusan (dan `w:delText` hanya di sana), id revisi unik, dan urutan
  settings.

  Tiga belas mutasi; sepuluh langsung tertangkap. Tiga lolos dan masing-masing membetulkan
  sesuatu: komentar yang mengklaim urutan mundur itu wajib ternyata keliru (yang wajib adalah
  pemindahannya), fixture `pPrChange` tak punya `rPr` tanda paragraf, dan tes id hanya menambah satu
  revisi sehingga tak mungkin bertabrakan. Ketiganya kini tertangkap. python-docx membuka hasilnya.

  Yang belum: `w:cellIns`/`w:cellDel`/`w:cellMerge`, perubahan penomoran, dan ekspor PDF dengan
  markup revisi (coretan/garis bawah berwarna) — ekspor sekarang selalu tampilan final.
- [x] **Proteksi dokumen dan range yang boleh diedit — selesai.** `Protect`/`Unprotect`,
  `Protection.VerifyPassword`, `AllowEditing(first, last, editor)`, dan `EditableRanges`. Empat
  pembatasan; `TrackedChanges` sekaligus menyalakan `TrackRevisions`.

  **Hash-nya bukan hash Excel**, dan di situlah jebakannya. Word lebih dulu menurunkan password ke
  kunci 32-bit skema Word 1997 (word tinggi dari encryption matrix, word rendah dari XOR verifier),
  menulis byte kunci itu terbalik sebagai delapan digit hex, baru menjalankan SHA-512 bersalt atas
  UTF-16 digit itu. Meng-hash password langsung menghasilkan berkas yang terproteksi dengan benar
  tapi menolak password yang benar. Algoritmanya diambil dari ECMA-376 Part 4 §2.15.1.28 dan
  dibandingkan dengan Apache POI; vektor uji spesifikasinya sendiri ("Example" → `0x64CEED7E` →
  `7EEDCE64`) menjadi tes, dan hash berulangnya dicocokkan dengan
  `tools/word_protection_hash.py` yang ditulis terpisah dari kode pustaka.

  **POI berbeda dari spesifikasi untuk karakter di atas 0x7F**: ia meng-XOR byte bertanda Java ke
  dalam `short`, sehingga karakter seperti `é` diperluas tandanya dan menghasilkan kunci lain. Di
  sini byte-nya tak bertanda, sesuai spesifikasi. Mana yang dipakai Word untuk karakter non-ASCII
  **belum dibuktikan** — tidak ada Word di mesin ini.

  `VerifyPassword` mengembalikan `null`, bukan `false`, untuk skema yang tidak diimplementasikan
  (atribut gaya ISO `algorithmName`, atau MD5), supaya berkas produsen lain tidak dinyatakan salah
  password padahal tidak ada yang memeriksa. Tujuh mutasi, ketujuhnya tertangkap.
- [x] **Menggabungkan dokumen — selesai.** `AppendDocument(source, AppendOptions)` dengan
  `SectionJoin` (halaman baru, continuous, tanpa break) dan `StyleConflict` (gaya target, atau
  salin gaya sumber yang berbeda dengan id baru). Semua dipetakan **malas** — gaya atau daftar baru
  disalin saat pertama dirujuk — sehingga hanya yang dipakai ikut, dan header serta catatan yang
  disalin mendapat perlakuan yang sama dengan body: `r:id` per pasangan part, gaya, `numId`/
  `abstractNumId`, id catatan/komentar, id revisi/bookmark/permission, nama bookmark (beserta anchor
  tautan internalnya), `wp:docPr`, dan `w14:paraId`.

  **`w:nsid` adalah jebakan yang tidak terlihat.** Word menentukan dua daftar itu daftar yang sama
  menurut `nsid`, bukan id, dan dua dokumen dari `Normal.dotm` yang sama sering berbagi satu. Salinan
  yang mempertahankannya melanjutkan penomoran target. Daftar buatan WordNet sendiri tidak menulis
  `nsid`, jadi tes pertamanya lulus dengan mutasi itu — fixture-nya kini menanam nsid bersama
  seperti berkas Word sungguhan.

  **Bug lama yang ditemukan di jalan: setiap gambar dan tautan di header/footer rusak.**
  `Run.AddPicture` dan `Paragraph.AddHyperlink` selalu menaruh relasinya di part dokumen, padahal
  `r:embed` di header di-resolve terhadap relasi header — yang tidak punya berkas `.rels` sama
  sekali. Validator hanya memeriksa `document.xml`, jadi tak ada yang melihat. Sekarang relasi
  dibuat di part pemilik elemennya (`OwnerPartOf`), dan validator memeriksa setiap part Word.
  Ditemukan karena tes gabungan memeriksa relasi header, dan **sumbernya** sudah gagal.

  Dua cacat lain: penggabung sendiri sempat menulis `w:pPr` setelah run (jebakan `GetOrCreate`
  dengan daftar urutan satu nama, persis yang tercatat di CLAUDE.md), dan penghitung id drawing
  dokumen tidak tahu id yang dibagikan penggabung, sehingga `AddPicture` berikutnya bisa memakai id
  yang sama — kini disemai ulang lintas header juga. Enam belas mutasi; tiga lolos karena fixture
  terlalu bersih (tanpa nsid, tanpa gaya default yang bentrok, tanpa section di tengah sumber), dan
  ketiganya kini tertangkap. python-docx membaca tiga section dengan header masing-masing.

  Yang tidak ikut: balasan komentar berutas (`commentsExtended`), dan field `REF`/`PAGEREF` yang
  menyebut bookmark yang diganti namanya.
- [x] **Memecah dokumen — selesai.** `SplitBySection`, `SplitByHeading(level)`,
  `SplitAtPageBreaks`. Kebalikan cara kerja penggabung, dengan sengaja: setiap bagian bermula dari
  salinan utuh sumber lalu dipotong, sehingga tak ada rujukan yang bisa salah, kemudian yang tak
  terpakai dipangkas — gambar, header, footnote, komentar, serta bookmark dan range komentar yang
  terbelah batas. Break di tengah paragraf memecah paragrafnya. Pecah lalu gabung kembali
  menghasilkan teks yang sama. Per *halaman* tetap tidak ada: itu keputusan mesin layout Word.

  **Bug lama yang ditemukan: `AddSection` menyalin referensi header**, sehingga semua section
  berbagi satu part dan menulis header section baru menimpa header section lama — tanpa cara untuk
  memisahkannya. Word mewujudkan "Link to Previous" dengan *tidak menulis* referensi. Sekarang
  section baru mewarisi sampai `GetHeader()` dipanggil padanya. Agar itu tidak merusak yang lain,
  ekspor PDF kini mencari header ke section sebelumnya (sebelumnya hanya membaca referensi section
  terakhir), dan pemecah menuliskan referensi warisan ke bagian yang dipotong lepas dari section
  asalnya. Ini **perubahan perilaku**: kode yang sengaja mengandalkan header bersama setelah
  `AddSection` kini mendapat header terpisah begitu ia menulis ke section baru. Sebelas mutasi,
  semuanya tertangkap.
- [x] **Mail merge bersarang / region berulang — selesai.** `FillTemplate(data, TemplateOptions)`:
  field dengan path bertitik dan format .NET (`{{total:N2}}` dalam culture pilihan), bagian
  berulang `{{#x}}…{{/x}}`, bagian terbalik `{{^x}}`, `{{.}}`, bersarang dengan pencarian yang jatuh
  ke konteks luar, data berupa objek/objek anonim/dictionary/daftar. Body, header, footer, dan
  catatan semuanya diisi. **Posisi kedua tag menentukan apa yang berulang** — satu paragraf: inline;
  baris tabel yang sama: barisnya; selain itu: rentang blok, dan paragraf yang hanya berisi tag
  dibuang — sehingga template tidak butuh markup tambahan. `DataTable` tidak didukung langsung;
  `rows.Select(r => ...)` ke objek sudah cukup.

  **Token dipotong dari run-nya, bukan diratakan bersama paragrafnya.** `MailMerge` lama (dan
  `ReplaceTextAcrossRuns` di bawahnya) menulis ulang seluruh paragraf sebagai satu run berformat run
  pertama, jadi kata tebal di sebelah `{{nama}}` kehilangan tebalnya. **Dokumentasinya mengklaim
  sebaliknya** — "preserving each run's formatting" — dan itu tidak pernah benar; kini dikoreksi.
  `FillTemplate` memecah run tepat di tepi setiap token lebih dulu, sehingga format setiap karakter
  lain tetap di tempatnya.

  Dua belas mutasi; sebelas tertangkap. Yang lolos membuktikan sebuah komentar salah: kode yang
  "mencegah token yang dipertahankan diisi ulang oleh konteks luar" tidak mencegah apa pun, karena
  konteks luar selalu subset konteks dalam. Kodenya dihapus. Satu keputusan aturan dicatat: di
  tabel satu kolom kedua tag jatuh di paragraf yang sama dan berulang inline; tag penutup di
  paragraf kedua sel itu mengulang barisnya.
- [x] **Watermark sungguhan di .docx — selesai.** `AddWatermark`, `Watermark`, `RemoveWatermark`.
  Shape `wps` di header, di belakang teks, diputar 315°, ukuran huruf menyesuaikan panjang teks
  agar frasa panjang tidak keluar halaman. Ekspor PDF kini ikut menggambar watermark dokumen kecuali
  `PdfExportOptions.Watermark` menyebut yang lain — sebelumnya watermark .docx akan hilang begitu
  diekspor. Dipilih DrawingML, bukan VML WordArt yang dimasukkan dialog Word: formula
  `shapetype` VML-nya tidak bisa diverifikasi tanpa Word, sedangkan shape `wps` sudah divalidasi di
  pustaka ini sejak v1.1.

  Hanya part header yang benar-benar dirujuk yang diberi watermark, sekali per part: memanggil
  `GetHeader()` pada section yang mewarisi akan memberinya header kosong dan memutus pewarisannya.
  Tujuh mutasi; satu lolos sampai ada tes untuk dua section yang merujuk satu header secara
  eksplisit — bentuk yang ditulis `AddSection` versi lama, jadi setiap dokumen lama punya bentuk itu.
  Yang **belum** dibuktikan: tampilannya di Word sendiri.
- [ ] **Chart di dokumen Word.** `ChartData`/`ChartXml` sudah di Core dan sudah dipakai ExcelNet dan
  PowerPointNet; Word hanya butuh `wp:inline` + `c:chart` dan part chart-nya. `ChartRenderer`
  untuk ekspor PDF perlu dipindah ke tempat yang bisa dipakai WordNet (Core, atau PdfNet bila ia
  tetap daun).
- [ ] **Markdown → DOCX dan DOCX → Markdown.** Lewat model blok yang sama dengan HTML
  (`HtmlFlattener` masuk, penulis baru keluar), sehingga Markdown dan HTML sepakat tentang isinya.
  Parser CommonMark ditulis sendiri atau Markdown dirender ke HTML dulu — keputusan yang harus
  diambil dengan alasan dependensi, bukan selera. TXT → DOCX ikut sebagai kasus paling sederhana.
- [ ] **Ekspor PDF/A** (lihat PdfNet) sebagai opsi `PdfExportOptions.Conformance`.

### ExcelNet

- [x] **Named range — selesai.** `Workbook.Names` (global dan lokal per sheet, konstanta dan formula,
  komentar), dipakai mesin formula sebagai range utuh (`SUM(Penjualan)`) maupun nilai, dengan nama
  lokal menutupi nama global; `Worksheet.PrintArea` dan `PrintTitleRows` lewat `_xlnm.*`.

  **`localSheetId` adalah posisi, bukan nama** — dan posisi itu menghitung chartsheet juga. Cakupan
  disimpan sebagai objek sheet dan indeksnya dihitung saat menyimpan, sehingga `MoveSheet` tidak
  memindahkan area cetak ke sheet lain; saat membaca, indeks dipetakan lewat urutan `<sheet>` asli
  termasuk chartsheet (diuji dengan berkas tulisan tangan yang menaruh chartsheet di tengah).
  Ganti nama, salin, dan hapus sheet menjaga nama tetap konsisten. Rantai nama yang berputar berakhir
  `#REF!`; tanpa penjaganya proses tes mati karena stack overflow — mutasi itu tertangkap justru
  karena crash. Sembilan mutasi, semuanya tertangkap; openpyxl membaca nama global, area cetak
  setelah `MoveSheet`, dan nilai cache formulanya.
- [ ] **Excel Table** (`ListObject`, part `xl/tables/tableN.xml`). Range bernama dengan header,
  gaya tabel, baris total, autofilter-nya sendiri. Referensi terstruktur (`Tabel1[Kolom]`) di mesin
  formula adalah langkah kedua yang terpisah.
- [ ] **Rich text di sel.** `SharedStrings` saat ini menggabungkan run rich text jadi teks polos
  saat membaca, jadi membuka lalu menyimpan berkas **menghapus** format dalam selnya. Model run
  (`CellRichText`) yang ditulis kembali utuh.
- [ ] **Sort range.** Menurut satu atau beberapa kolom, naik/turun, dengan urutan tipe Excel (angka
  < teks < boolean < error, kosong selalu terakhir) dan perbandingan teks tak peka huruf besar.
  Formula yang ikut berpindah **tidak** ditulis ulang referensinya di versi pertama — dicatat.
- [ ] **Conditional formatting lanjutan.** `duplicateValues`/`uniqueValues` (deteksi duplikat dari
  `reference.md`), `top10`, `aboveAverage`, `containsText`, `expression`, dan icon set. Plus
  `FindDuplicates(range)` di sisi API untuk yang butuh hasilnya, bukan warnanya.
- [x] **Fungsi finansial — selesai.** `PMT`, `PV`, `FV`, `NPER`, `RATE`, `NPV`, `IRR`, `XNPV`,
  `XIRR`, diuji terhadap **contoh di halaman resmi Microsoft** — nilai yang dihitung Excel — bukan
  terhadap rumus yang diturunkan ulang. Yang iteratif memakai Newton dan menjawab `#NUM!` bila tidak
  konvergen, bukan iterasi terakhir.

  **Parser formula tidak menerima argumen yang dikosongkan** (`PV(r, n, pmt, , 0)`), bentuk yang
  dipakai contoh Microsoft sendiri: seluruh formula gagal. Kini argumen kosong bernilai kosong dan
  fungsinya memakai default. Delapan mutasi; dua lolos karena fixture-nya tidak bisa membedakan —
  contoh IRR hanya punya satu akar sehingga tebakan awal tak berpengaruh, dan teks di *akhir* range
  NPV tidak menggeser apa pun — lalu ditambah arus kas dengan dua IRR eksak (10% dan 20%) dan teks
  di tengah range. Satu mutasi ekuivalen dan dicatat: pemeriksaan tanda arus kas di IRR hanyalah
  jalan pintas, karena Newton tanpa akar tetap berakhir `#NUM!`.
- [ ] **Chart kombinasi** (batang + garis, sumbu sekunder) di `ChartData`/`ChartXml` — berlaku
  sekaligus untuk ExcelNet, PowerPointNet, dan chart Word di atas.
- [ ] **Enkripsi workbook** (ECMA-376 Agile Encryption, AES-256 + SHA-512). Berkas terenkripsi
  bukan ZIP melainkan compound file (CFB) berisi `EncryptionInfo` dan `EncryptedPackage`. Ini
  **bukan** format biner lama: yang dibutuhkan hanya pembaca/penulis CFB minimal untuk dua stream,
  bukan model `.xls`. Berlaku sama untuk .docx dan .pptx, jadi tempatnya di Core.
- [ ] **XLSX → HTML dan HTML → XLSX** (tabel). `HtmlWriter`/`HtmlFlattener` sudah di Core.
- [ ] **XLSM.** Ekstensinya dikenali dan part yang tidak dimodelkan dibawa utuh; yang belum
  diperiksa adalah content type bagian utamanya (`sheet.macroEnabled.main+xml`) bertahan saat
  disimpan. Satu tes round-trip dengan `vbaProject.bin` menjawabnya.

### PowerPointNet

- [ ] **Garis, panah, dan konektor** (`p:cxnSp`, `a:headEnd`/`a:tailEnd`, konektor yang terhubung
  ke shape lewat `stCxn`/`endCxn`). Dibaca sebagai `ShapeKind.Connector` tapi tidak bisa dibuat.
- [ ] **Custom geometry** (`a:custGeom`) — API path (moveTo/lineTo/cubicBezTo/close) untuk shape
  bebas, dan ikut digambar di ekspor PDF.
- [ ] **Objek tersemat (OLE)** — sematkan .xlsx/.docx/.pdf sebagai `p:oleObj` dengan gambar
  pratinjau. Paket tersemat adalah berkas OPC biasa, jadi tidak butuh CFB untuk format Office.

### PdfNet

- [ ] **Membuat field AcroForm.** `AcroForm` sekarang membaca, mengisi, dan meratakan field yang
  sudah ada, tetapi tidak bisa **membuat** text field, checkbox, radio group, combo box, list box,
  maupun push button. Appearance stream-nya sudah dibangkitkan untuk mengisi, jadi jalurnya ada.
- [ ] **Lampiran (embedded files).** Ekstrak dan tambah lewat name tree `/EmbeddedFiles` dan
  anotasi `/FileAttachment`. Prasyarat PDF/A-3 (ZUGFeRD/Factur-X untuk e-invoice).
- [ ] **PDF/A-2b dan PDF/A-3b.** Metadata XMP yang selaras dengan `/Info`, `/OutputIntent` dengan
  profil ICC sRGB, semua font tersemat (14 font baku **tidak** boleh dipakai — ekspornya harus
  memaksa font tersemat), tanpa enkripsi dan tanpa transparansi untuk PDF/A-1. Diverifikasi dengan
  veraPDF, bukan dengan klaim sendiri.
- [ ] **Metadata XMP** baca/tulis — berdiri sendiri juga, bukan hanya untuk PDF/A.
- [ ] **Timestamp tepercaya (RFC 3161).** Sebelumnya ditolak karena butuh layanan jaringan. Bisa
  tanpa membuat pustaka ini melakukan request sendiri: pemanggil memberi delegasi
  `Func<byte[], byte[]>` yang mengirim permintaan ke TSA-nya, sama seperti `ImageResolver` di
  konversi HTML. Verifikasi token timestamp yang sudah ada di berkas tidak butuh jaringan sama
  sekali.

### OfficeNet.Rendering

- [ ] **Keluaran TIFF multi-halaman.** SkiaSharp tidak mengodekan TIFF, tapi TIFF baseline dengan
  Deflate cukup sederhana untuk ditulis tangan — dan satu TIFF berisi seluruh halaman adalah yang
  diminta alur arsip dan faks.

### Kandidat dengan nilai lebih rendah

- **XPS.** Format OPC (FixedDocument/FixedPage XAML), jadi Core sudah menangani kontainernya.
  Pemakainya makin sedikit; dikerjakan hanya bila ada yang meminta.
- **RTF masuk.** Berbasis teks, tidak butuh OLE, tetapi spesifikasinya panjang dan berkas dunia
  nyata penuh kontrol vendor. Subset (paragraf, run, tabel, gambar) layak bila ada permintaan.

---

## v2.2 — Server MCP untuk asisten AI

Asisten AI (Claude, Copilot, Cursor) semakin sering diminta "buatkan laporannya dalam Word" atau
"rapikan spreadsheet ini". [Model Context Protocol](https://modelcontextprotocol.io) adalah cara
baku untuk memberi mereka alat. OfficeNet sudah punya semua kemampuannya; yang kurang hanya
pintunya. Paket baru `Gravicode.OfficeNet.Mcp` (perintah `officenet-mcp`, dipasang sebagai
`dotnet tool`) menaruh keempat pustaka di balik tool MCP lewat stdio.

Keputusan yang mengunci bentuknya:

1. **Tool tanpa state, berbasis path.** Setiap panggilan membuka berkas, mengerjakan satu hal, lalu
   menyimpan. Tidak ada handle dokumen yang hidup di antara panggilan, jadi tidak ada state yang
   bisa basi atau bocor antar-percakapan, dan server bisa dimatikan kapan saja.
2. **Satu workspace, ditegakkan di kode.** Model bisa dibujuk meminta apa saja — oleh pengguna, atau
   oleh teks tersembunyi di dokumen yang disuruh dibacanya. Setiap path di-resolve terhadap
   `--root` dan ditolak bila keluar, termasuk lewat symbolic link. `--read-only` menolak setiap
   penulisan. Tidak ada tool yang menyentuh jaringan.
3. **Model bisa melihat hasilnya.** `render_page` mengembalikan halaman sebagai gambar PNG di dalam
   respons MCP. Tanpanya server hanya pembuat berkas buta; dengannya asisten bisa membuat, melihat,
   lalu membetulkan.
4. **Input yang memaafkan.** Model mengisi skema dari deskripsi, jadi jenis blok adalah string biasa,
   hampir semua anggota opsional, `**tebal**` dan `*miring*` dipahami, tanggal ISO menjadi tanggal,
   dan string berawalan `=` menjadi formula yang langsung dihitung.
5. **Kesalahan dijelaskan agar bisa dibetulkan.** Eksepsi pustaka dan argumen yang keliru kembali
   sebagai error yang terbaca ("Unknown block type 'tabel'"); kegagalan lain tetap generik, tanpa
   stack trace.
6. **SkiaSharp boleh di sini.** Server adalah aplikasi, bukan pustaka inti, jadi bergantung pada
   `OfficeNet.Rendering` tidak melanggar prinsip pertama.

- [x] **Server stdio dengan 23 tool — selesai.** Word: buat, tambah, isi template, cari-ganti,
  track changes, proteksi, watermark, gabung, pecah. Excel: buat (formula, format, chart), tulis
  sel, baca, tambah chart. PowerPoint: buat (tujuh jenis slide, catatan pembicara), HTML → slide.
  PDF: gabung, pecah, enkripsi. Lintas format: konversi, ekstraksi teks, info dokumen, render
  halaman, daftar berkas. SDK C# resmi `ModelContextProtocol` 2.2.0. Setiap tool membawa
  `readOnlyHint`/`destructiveHint` agar klien tahu apa yang perlu dikonfirmasi.
- [x] **Diuji lewat protokol sungguhan.** `tests/OfficeNet.Mcp.Tests` menjalankan server sebagai
  proses terpisah dan memakai `McpClient` lewat stdio, bukan memanggil kelas tool langsung — yang
  akan melewatkan skema JSON, pengikatan argumen, konversi eksepsi, dan keluaran nyasar ke stdout.
  Setiap jenis dokumen dibuat lewat protokol, dibuka kembali oleh pustakanya, dan diperiksa
  validator OPC. Smoke test kedua memakai Python yang menulis JSON-RPC mentah, tanpa SDK.
- [x] **Dokumentasi** `docs/Mcp.md` dan `docs/id/Mcp.md`: instalasi, konfigurasi Claude Code,
  Claude Desktop, dan VS Code, model keamanan, daftar tool, contoh klien C#.
- [x] **Melihat hasilnya menemukan dua cacat yang tidak ditangkap tes.** Kolase pertama
  memperlihatkan (1) tanggal invoice tercetak `2026-10-28`: JSON tidak punya tipe tanggal, jadi
  `{{tanggal:dd MMMM yyyy}}` menerima string yang tak bisa diformat — sekarang string tanggal ISO di
  data template menjadi tanggal; dan (2) **chart workbook tidak ada di PDF-nya sama sekali**.
  `ChartRenderer` privat di PowerPointNet, dan ExcelNet tidak boleh bergantung pada saudaranya.
  Renderer itu kini **pindah ke PdfNet** (`PdfNet.Content.ChartRenderer`, publik) — sah karena ia
  hanya butuh `ChartData` dari Core dan `PdfCanvas` — dan `ExcelToPdf` menggambar chart di bawah
  data, berukuran dari anchor-nya. Ini sekaligus prasyarat "chart di dokumen Word" di v2.1.
- [x] **Regresi stdout sekarang gagal, bukan menggantung.** Mutasi "log ke stdout" membuat seluruh
  run tes macet tanpa batas: klien menunggu respons yang tak pernah bisa diurai. Setiap panggilan di
  tes kini punya tenggat 60 detik. Mutasi "tanpa pemeriksaan batas" juga membuktikan tesnya nyata —
  server yang dirusak itu benar-benar menulis `lolos.docx` ke luar workspace — dan sisa berkas itu
  sempat menggagalkan run berikutnya, jadi namanya kini unik per run. Sembilan mutasi, semuanya
  tertangkap.
- [x] **Screenshot dari protokol.** `tools/ScreenshotGen` kini juga klien MCP: ia menjalankan server,
  membuat laporan, workbook, deck, dan invoice dari template lewat panggilan tool, dan PNG-nya
  ditulis oleh `render_page` milik server sendiri. Tampil di README dan di panduan MCP.

Berikutnya, bila ada permintaan:

- [ ] **Transport HTTP** (streamable HTTP) untuk server bersama. Sengaja belum: begitu server bisa
  dijangkau lewat jaringan, ia butuh autentikasi dan otorisasi per pengguna, dan "satu workspace"
  berubah menjadi "satu workspace per pengguna". stdio menghindari semua itu karena server adalah
  proses milik pengguna sendiri.
- [ ] **Resources dan prompts MCP** — dokumen di workspace sebagai resource yang bisa dibaca klien,
  dan prompt siap pakai ("buat laporan kuartal dari spreadsheet ini").
- [ ] **Markdown sebagai input `word_create`** begitu Markdown → DOCX (v2.1) selesai: model menulis
  Markdown dengan sangat fasih, dan blok terstruktur hanyalah jembatan sampai saat itu.

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
- **Dari `reference.md`, yang sengaja tidak diambil:** membaca/menulis `.doc`/`.xls`/`.ppt` (lihat
  format biner lama di atas), memecah Word **per halaman** (butuh mesin layout setara Word), dan
  SmartArt Word selain mempertahankannya apa adanya — part yang tidak dimodelkan memang sudah
  terbawa utuh.
- **Rendering di server tanpa font.** Metrik 14 font baku sudah tertanam, tapi rasterisasi
  membutuhkan font sungguhan. Kontainer tanpa font akan menghasilkan gambar dengan glyph pengganti,
  dan itu masalah lingkungan, bukan bug library.
