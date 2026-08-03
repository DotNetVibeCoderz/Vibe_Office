# Arsitektur

*AutoWork — Gravicode Studios, dipimpin oleh Kang Fadhil*

## Tiga subsistem

AutoWork disusun sebagai Brain, Eyes, dan Hands. Ini bukan bingkai untuk keperluan README — itu
cara proyeknya dipecah, cara tool diberi label, dan cara antarmuka melaporkan apa yang sedang
terjadi. Setiap langkah dan setiap baris log diatribusikan ke salah satunya, dan itulah yang
membuat baris organ di header bisa menjawab "sedang apa ia sekarang" dengan jujur.

```
                        ┌─────────────────────────────┐
                        │      AutoWork.Desktop       │
                        │  Antarmuka Avalonia · Pita  │
                        └──────────────┬──────────────┘
                                       │ aliran RunEvent
                        ┌──────────────▼──────────────┐
                        │      AutoWork.Agents        │   ← BRAIN
                        │  planner · orkestrator      │
                        │  sub-agen · peringkasan     │
                        │  verifikasi · visi          │
                        └───┬───────────────────┬─────┘
                            │                   │
          ┌─────────────────▼──────┐   ┌────────▼─────────────────┐
          │   AutoWork.Providers   │   │      AutoWork.Tools      │  ← HANDS + EYES
          │  Kompatibel-OpenAI     │   │  file · shell · dokumen  │
          │  Anthropic · Ollama    │   │  data · gambar · layar   │
          │  embedding             │   │  input · web             │
          └────────────┬───────────┘   └────────┬─────────────────┘
                       │                        │
                       │      ┌─────────────────▼──────────┐
                       │      │  AutoWork.Integrations     │
                       │      │  GitHub · Google · Notion  │
                       │      │  Asana · PayPal            │
                       │      └─────────────────┬──────────┘
                       │                        │
                    ┌──▼────────────────────────▼──┐
                    │        AutoWork.Core         │
                    │ konfigurasi · rahasia ·      │
                    │ PathGuard · log · pengetahuan│
                    └──────────────────────────────┘
```

Core tidak bergantung pada apa pun selain framework. Semuanya bergantung pada Core. Antarmuka
bergantung pada semuanya, dan tidak ada yang bergantung padanya.

## Bagaimana satu sesi berjalan

```
  tujuan
   │
   ├─ 1. Susun perangkat tool       kebijakan menentukan apa yang ada; tool terlarang
   │                                tidak pernah dijelaskan ke model sama sekali
   │
   ├─ 2. Ingat pengetahuan          cari konteks di basis pengetahuan yang auto-attach
   │
   ├─ 3. Rencanakan                 model planner mengembalikan JSON: langkah berurutan,
   │                                masing-masing dengan organ, dependensi, kriteria sukses
   │
   ├─ 4. Kelompokkan jadi gelombang langkah yang dependensinya terpenuhi dan ditandai
   │                                independen secara eksplisit boleh berjalan bersamaan
   │
   ├─ 5. Untuk tiap gelombang
   │      ├─ gerbang jeda           tahan di sini bila pengguna meminta jeda; sampaikan
   │      │                         koreksinya ke model sebelum langkah dijalankan
   │      ├─ ringkas bila perlu     ringkas giliran lama sebelum langkah, bukan sesudah,
   │      │                         agar langkah itu punya ruang untuk bekerja
   │      ├─ jalankan langkah       model eksekutor + tool yang dipanggil otomatis
   │      └─ catat hasilnya         langkah gagal hanya bila semua pemanggilan tool gagal
   │
   ├─ 6. Verifikasi                 cocokkan transkrip dengan kriteria keberhasilan
   │
   ├─ 7. Laporkan                   ringkasan, durasi, langkah, statistik peringkasan
   │
   └─ 8. Simpan transkrip           ditulis apa pun hasilnya, termasuk saat dihentikan
```

Progres dipancarkan sebagai record `RunEvent` yang imutabel. Core mendefinisikannya dan tidak
memuat tipe UI apa pun; lapisan desktop yang mengubahnya menjadi baris di Pita Kerja.

## Jeda dan koreksi

Pengguna yang melihat agen salah arah seharusnya bisa langsung mengatakannya, bukan membatalkan
lalu mengetik ulang seluruh pekerjaan. `RunController` adalah pegangan UI atas run yang sedang
berjalan: `Pause`, `Resume`, `Steer`, dan `Abandon`.

Jeda berlaku **di batas langkah berikutnya**, tidak pernah di tengah langkah. Menyela di tengah
berarti menelantarkan pemanggilan tool yang sudah dikirim ke penyedia, atau meninggalkan tulisan
berkas yang setengah jadi — dan keduanya menyisakan percakapan yang tidak bisa dilanjutkan oleh
apa pun. Menunggu batas langkah hanya makan beberapa detik. Alasannya sama dengan mengapa
peringkasan pun hanya terjadi di titik itu.

Koreksi disisipkan sebagai giliran pengguna **sebelum** langkah yang hendak diubahnya, dengan
kalimat yang membuat model memperlakukannya sebagai pengguna berubah pikiran — bukan sebagai satu
syarat tambahan di atas permintaan awal. Mengoreksi tanpa menjeda dulu juga boleh: koreksinya
diantrikan dan tetap diterapkan di batas berikutnya.

Menghentikan run juga harus melepaskan run yang sedang dijeda, kalau tidak thread agen akan
menunggu selamanya untuk perintah lanjut yang tak akan pernah datang — sama persis dengan kartu
persetujuan yang tidak pernah dijawab.

## Riwayat run

Setiap run disimpan di `runs/` sebagai dua berkas:

- `<id>.json` — ringkasannya: tujuan, model, status, waktu, jumlah langkah dan pemanggilan tool.
- `<id>.events.json` — aliran `RunEvent` selengkapnya.

Dipisah karena daftar Riwayat kalau tidak harus membayar setiap transkrip yang tidak sedang
ditampilkannya, sementara satu transkrip memuat seluruh hasil tool yang pernah dilihat run itu.
Setiap hasil tool dipotong di sekitar 4.000 karakter sebelum disimpan.

Event ditulis secara polimorfik dengan penanda tipe pendek yang tetap. Transkrip yang tersimpan
berumur lebih panjang daripada build yang menulisnya, jadi kelas yang diganti nama tidak boleh
membuat riwayat kemarin tak terbaca — dan ada tes yang gagal bila ada subtipe `RunEvent` baru
tanpa penanda.

Membuka run lama memutar ulang event-nya lewat **handler yang sama dengan run langsung**, bukan
lewat perender khusus baca-saja yang kedua. Satu perender berarti run lama tampak persis seperti
saat berjalan, dan tidak ada tempat kedua yang bisa melenceng.

Riwayat adalah kemudahan, bukan syarat: transkrip yang gagal ditulis dicatat di log, dan hasil
run-nya tetap berlaku.

## Meter token

`UsageTrackingChatClient` membungkus setiap klien selama satu run dan melaporkan pemakaian tiap
permintaan ke `RunMeter`. Ia sengaja diletakkan **di luar** loop pemanggilan fungsi: satu langkah
agen bisa menghasilkan beberapa perjalanan bolak-balik ke penyedia saat tool dipanggil dan
hasilnya dikembalikan, dan semuanya ditagih. Menghitung hanya panggilan terluar akan melaporkan
sebagian kecil dari run yang banyak memakai tool.

Biaya dijumlahkan per model, bukan dari total run — run yang merencanakan dengan satu model dan
mengeksekusi dengan model lain punya dua harga, dan satu tarif campuran akan salah untuk keduanya.

Tiga hal yang tidak akan dilakukannya:

- **Mengarang harga.** Tidak ada tabel harga bawaan. Kedua harga harus diisi pada model, kalau
  tidak, tidak ada biaya yang dilaporkan. Setengah harga tidak cukup.
- **Membulatkan kekosongan.** Penyedia yang menjawab tanpa melaporkan pemakaian menambah
  penghitung terpisah, dan totalnya lalu ditampilkan sebagai "minimal *n*".
- **Mengestimasi.** `TokenEstimator` ada untuk peringkasan, di mana kelebihan estimasi adalah
  kesalahan yang aman. Meter melaporkan apa yang dikatakan penyedia, atau menyatakan tidak tahu.

## Aturan persetujuan

`ApprovalRuleEngine` dikonsultasikan oleh `ApprovalCoordinator` sebelum apa pun sampai ke
pengguna atau ke memori per-run. Batasannya dijelaskan di
[keamanan.md](keamanan.md#aturan-tetap); yang penting secara arsitektural adalah evaluasi aturan
terjadi di lapisan *persetujuan*, sementara `PathGuard` berjalan di dalam badan tool sesudahnya.

Urutan itulah seluruh argumen keamanannya. Sebuah aturan hanya bisa meredam pertanyaan tentang
sesuatu yang memang akan diizinkan sandbox — ia tidak berada di jalur yang menentukan apa yang
terjangkau, dan tidak bisa dibuat berada di sana.

## Streaming

Loop langkah melakukan streaming secara bawaan lalu menggabungkan kembali pembaruannya menjadi
satu respons, karena loop tetap membutuhkan satu jawaban utuh dan sekumpulan pesan untuk
ditambahkan. Potongan dikirim sebagai `AssistantDeltaEvent`, masing-masing membawa **total
berjalan untuk langkah itu**, bukan sekadar potongan barunya — sehingga tampilan yang melewatkan
satu pembaruan, atau baru mulai menonton, tetap menampilkan yang benar.

Pemanggilan tool yang terpotong bukan urusan lapisan ini. Klien function-invocation berada di
bawahnya dan merangkai kembali panggilan yang datang berkeping-keping, lalu melanjutkan stream
dengan hasilnya. Yang ditambahkan orkestrator hanyalah penggabungan dan event-nya.

Bila streaming gagal **sebelum ada konten yang tiba**, permintaan yang sama diulang tanpa
streaming — sebagian gateway mengiklankan streaming lalu menolaknya, dan run yang mati karena itu
lebih buruk daripada run yang diam-diam kembali menunggu. Kegagalan *setelah* konten tiba adalah
kegagalan sungguhan dan diserahkan ke penanganan galat langkah: mengulang saat itu berisiko
menjalankan ulang tool yang sudah berjalan.

## Tugas tersimpan

`JobScheduler` mengawasi jam dan folder, lalu meminta sebuah tugas dijalankan. Ia tidak tahu apa
arti menjalankan tugas — itu sebuah delegate — sehingga bisa diuji dengan jam palsu dan folder
sungguhan tanpa agen, penyedia, atau jaringan sama sekali.

Tiga aturan membentuknya:

- **Satu per satu.** Sebuah run memegang halaman Kerja dan broker persetujuan. Tugas yang jatuh
  tempo saat run lain berjalan dilewati, bukan diantrikan: "ringkas kemarin" berjalan dua kali
  beruntun lebih buruk daripada berjalan sekali.
- **Waktu yang terlewat tidak menumpuk.** Waktu jatuh tempo dihitung dari jam, jadi aplikasi yang
  ditutup sepanjang akhir pekan bangun tanpa utang.
- **Pemicu folder hanya boleh memantau yang diizinkan sandbox.** Kalau tidak, menyimpan sebuah
  tugas menjadi cara membuat AutoWork membaca folder yang tidak pernah diberikan kepadanya.

Pemicu folder menunggu masa tenang sebelum dijalankan. Menyalin lima puluh berkas memunculkan
lima puluh event; tanpa itu tugas dimulai pada berkas pertama dan membaca folder setengah jadi.

## Rapat

Transkripsi mati sampai dikonfigurasi, dan bersifat lokal kecuali pengguna memilih sebaliknya.
AutoWork tidak membawa model suara — beberapa ratus megabita bobot bukan sesuatu yang dipasang
diam-diam — jadi `LocalTranscriber` menjalankan model yang sudah terpasang, sebagai perintah, dan
membaca kembali stdout-nya atau berkas `.txt`/`.srt` yang ditulis di samping audio.

Hanya transkripsi yang berupa tool. Menarik keputusan dan penanggung jawab adalah penalaran, yang
sudah dilakukan agen lebih baik daripada prompt tetap yang dikubur di dalam sebuah tool, dan
menuliskan hasilnya adalah `doc_create_word` atau `knowledge_save` yang sudah ada.

## Sesi peramban

`BrowserSession` menjalankan peramban yang **sudah terpasang**, lewat protokol DevTools, dengan
profil tersendiri yang bertahan. Kombinasi itulah intinya: `web_fetch` melihat halaman yang
dilihat orang asing, sedangkan pekerjaan yang layak diotomatiskan ada di balik login.

Tiga keputusan:

- **Tidak membundel peramban.** Edge atau Chrome sudah ada; mengunduh satu lagi hanya untuk
  diotomatiskan bukan permintaan yang wajar.
- **Profil sendiri, bukan milik pengguna.** Menempel ke peramban yang sedang mereka buka akan
  berebut kunci profil. Direktori terpisah tetap mengingat login antar-run, dan itulah yang
  diinginkan.
- **Terlihat kecuali diminta sebaliknya.** Sesuatu yang bertindak sebagai Anda seharusnya bisa
  Anda tonton.

Koneksinya ke target **halaman** dari `/json/list`, bukan target peramban dari `/json/version`.
Yang terakhir itu yang paling mudah dijangkau dan justru salah: ia bicara `Target` dan `Browser`
tetapi tidak `Page` atau `Runtime`, sehingga navigasi dan evaluasi berhasil tanpa melakukan
apa pun.

## Berkas terhapus

Hapus lunak memindahkan berkas ke `recycle/` di bawah `AppPaths.Root`, yang ditolak `PathGuard`
tanpa syarat, sehingga agen tidak bisa membaca kembali apa yang dihapusnya. Di sampingnya,
`RecycleBin` menyimpan indeks JSONL append-only berisi asal tiap item — bagian yang membuat
"bisa dipulihkan" menjadi benar, bukan sekadar nama.

Penambahan baris menutup baris terakhir yang robek lebih dulu. Tanpa itu, proses yang mati di
tengah penulisan tidak hanya kehilangan catatannya sendiri tetapi juga catatan *berikutnya*, yang
menyambung ke potongan tadi.

Pemulihan adalah tindakan pengguna atas datanya sendiri, jadi ia tidak terikat pada izin folder
milik agen — berkas yang dihapus dari folder yang kini izinnya dicabut harus tetap bisa
dipulihkan. Satu aturan kerasnya: tidak ada yang boleh ditulis ke dalam direktori AutoWork
sendiri, sehingga baris indeks yang dipalsukan tidak bisa dipakai menimpa `config.json`.

## Mengapa berbentuk langkah, bukan satu percakapan panjang

Satu giliran obrolan panjang memang lebih sederhana. Bentuk berlangkah memberi tiga hal yang
sepadan dengan kerumitannya:

- **Batas bagi kegagalan.** Satu langkah buruk terkurung dan diumpankan balik ke model, bukan
  mengakhiri seluruh sesi.
- **Sesuatu untuk ditonton.** Pengguna bisa melihat progres, dan itulah yang membuat meninggalkan
  pekerjaan panjang terasa dapat diterima.
- **Sesuatu yang konkret untuk diverifikasi.** Tahap verifikasi memeriksa kriteria yang
  dideklarasikan, bukan menerka ulang maksud dari sebuah transkrip.

## Peringkasan otomatis

Saat sesi mendekati batas jendela konteks model, giliran-giliran lama diganti dengan ringkasan.

Bagian yang perlu kehati-hatian adalah **titik potongnya**. Pemanggilan tool dan hasilnya
berpasangan, dan setiap penyedia menolak percakapan yang hasilnya tidak punya pemanggilan yang
bersesuaian. Karena itu batas potong ditarik mundur sampai jatuh di tepi giliran yang bersih
sebelum apa pun dibuang. Pesan sistem di awal dan tujuan asli selalu dipertahankan — keduanya
adalah piagam sesi tersebut.

Bila panggilan peringkasan itu sendiri gagal, ringkasan mekanis (tool yang dipakai, catatan
terakhir) dipakai sebagai pengganti, alih-alih kehilangan sesi.

`ContextCompactionTests` memeriksa invarian batas ini di **setiap titik potong yang mungkin**,
karena inilah jenis bug yang hanya muncul pada sesi panjang, di lingkungan nyata, pada saat
paling buruk.

## Estimasi token

AutoWork berbicara dengan selusin penyedia, dan hitungan persis memerlukan tokenizer
masing-masing. Karena itu estimator sengaja **melebihkan** — 3,5 karakter per token, ditambah
overhead per pesan, ditambah skema tool (yang ikut terkirim pada setiap permintaan), ditambah
sekitar 1.200 token per gambar.

Meringkas sedikit lebih awal berbiaya satu panggilan peringkasan. Meringkas terlambat berbiaya
seluruh sesi.

## Sub-agen

Langkah-langkah independen berjalan bersamaan, masing-masing dengan percakapannya sendiri,
sehingga pemakaian token tidak menumpuk dan induknya hanya melihat laporan akhir mereka.

Penjadwalnya sengaja konservatif: hanya langkah yang **secara eksplisit** ditandai planner
bergantung pada sesuatu yang dianggap aman diparalelkan. Planner yang sekadar lupa mengisi
`dependsOn` tidak boleh menyebabkan dua agen mengganti nama file di folder yang sama secara
bersamaan.

## Penyedia

Satu jalur kode — format OpenAI — mencakup OpenAI, Azure OpenAI, Gemini, DeepSeek, Qwen,
Moonshot, OpenRouter, LM Studio, dan gateway apa pun yang dituju pengguna. Dua punya klien
sendiri:

- **Anthropic**, karena Messages API-nya berbeda secara material dan lapisan kompatibel-OpenAI
  miliknya secara eksplisit hanyalah alat bantu migrasi. Brain adalah bagian yang paling
  membutuhkan ketepatan penuh pada tool-use dan visi, sehingga `AnthropicChatClient` berbicara
  langsung ke `/v1/messages`.
- **Ollama**, lewat OllamaSharp.

Klien di-cache per profil, dikunci pada kredensial hasil resolusi, sehingga merotasi kunci
otomatis membatalkan cache. Pemanggilan fungsi dibungkus dengan batas iterasi agar model yang
terus memanggil tool tanpa mengerucut tidak menghabiskan kuota dalam satu langkah.

## Tools

Sebuah tool adalah `AIFunction` ditambah metadata yang tidak dibawa Microsoft.Extensions.AI:
organ pemiliknya, tingkat bahayanya, kategorinya, dan apa yang perlu ditanyakan sebelum
menjalankannya.

Tool mengembalikan **string, bukan exception**. Penolakan yang bisa dibaca model — `REFUSED:
~/Dokumen bersifat hanya-baca` — memungkinkannya memilih pendekatan lain; exception hanya
mengakhiri giliran. `ToolSetBase` mengubah exception yang realistis ditemui tool menjadi
kosakata tersebut dan mencatat setiap hasilnya.

Setiap pemanggilan dibungkus `ObservableAIFunction` sehingga Pita Kerja dapat menampilkannya saat
berlangsung, bukan hanya setelah giliran selesai.

![Pemanggilan tool di Pita Kerja, masing-masing diatribusikan ke organ pemiliknya](../images/work-tools.png)

### Pencarian web

`web_search` menjalankan rantai backend dan berhenti pada yang pertama menjawab: **Tavily** bila
kuncinya diatur, lalu **DuckDuckGo**, lalu **Wikipedia**.

Rantai ini ada supaya pencarian bukan fitur berbayar. Pengguna yang belum mendaftar apa pun tetap
mendapat tool yang bekerja, bukan tool yang menolak — aturan yang sama yang memberi pencarian
pengetahuan cadangan berbasis kata kunci saat tidak ada model embedding. Tavily memimpin karena
mengembalikan isi halaman yang sudah diekstrak beserta jawaban ringkas, sehingga agen tidak perlu
mengambil dan membersihkan lima halaman lebih dulu. Wikipedia berada di urutan terakhir karena
cakupannya sempit tapi hampir selalu terjangkau, jadi rantainya tidak berakhir sunyi di jaringan
yang memblokir mesin pencari.

Backend yang tidak mengembalikan apa pun bukan kesalahan, melainkan alasan untuk bertanya ke yang
berikutnya. Daftar host keluar diperiksa per backend, jadi fallback tidak akan pernah menjangkau
host yang belum diizinkan pengguna.

![Tampilan Aktivitas, mengalirkan log tindakan saat sesi berjalan](../images/activity.png)

### Skill

Skill adalah sebuah folder: manifes, plus rujukan, template, dan skrip yang menyertainya. Skill
yang terpasang menyumbang satu baris di prompt sistem (nama plus kapan dipakai), dan tiga tool —
`skill_open` untuk instruksinya, `skill_file` untuk berkas bundel, dan `skill_run` untuk skrip
bila izinnya mengizinkan.

Kedua tool yang menerima path menyelesaikan path itu lalu memastikan hasilnya berada di dalam
folder skill — pemeriksaan yang sama saat bundel ditulis, karena path dari repositori dan path
dari model sama-sama layak dicurigai.

Pemisahan itulah inti desainnya. Menempelkan selusin skill ke setiap permintaan berbiaya puluhan
ribu token demi membuat satu di antaranya relevan; menyebut namanya berbiaya beberapa ratus dan
membiarkan model memilih. Pertukaran yang sama dengan mencari di basis pengetahuan alih-alih
melampirkannya.

![Galeri Skill](../images/skills-gallery.png)

### MCP

Tool MCP datang dari SDK C# sudah berupa `AIFunction`, jadi integrasinya tipis: sambung, daftar,
bungkus tiap tool dalam `ToolDescriptor` agar Pita Kerja bisa mengatribusikannya seperti tool lain.

Bagian yang canggung adalah waktunya. `ToolRegistry.Build` bersifat sinkron, sementara menjalankan
server stdio makan beberapa detik. Karena itu orkestrator menyediakan kait `PrepareToolsAsync`
yang berjalan sekali sebelum perangkat tool disusun, dan klien dipertahankan selama aplikasi hidup
alih-alih per sesi.

Tool-nya ditandai `ToolRisk.Write`, tidak pernah `Safe`. AutoWork tidak bisa melihat apa yang
dilakukan tool eksternal, dan label risiko adalah klaim tentang perilaku.

![Galeri MCP](../images/mcp-gallery.png)

## Basis pengetahuan

File JSON biasa, satu per basis, vektor tersimpan di dalamnya, dicari dengan kemiripan kosinus
atas daftar di memori.

Basis pengetahuan desktop berisi ribuan entri, bukan jutaan. Pemindaian linier ber-SIMD lebih
baik daripada membawa basis data vektor sebagai dependensi, dan menjaga memori pengguna tetap
dalam file yang bisa mereka baca, cadangkan, dan hapus. Tanpa model embedding, pencarian turun
menjadi tumpang tindih kata alih-alih gagal.

![Tampilan Pengetahuan](../images/knowledge.png)

## Antarmuka

**Sistem desain.** Paletnya diturunkan dari anatomi produk itu sendiri: Brain, Eyes, dan Hands
masing-masing memiliki rona, dan setiap langkah, baris tool, dan baris log diwarnai oleh yang
bertanggung jawab. Warna membawa informasi, bukan hiasan. Selebihnya adalah netral yang
disiplin.

Pewarnaan organ diterapkan lewat **kelas styling yang terikat pada organ baris tersebut**, bukan
value converter, sehingga pergantian tema mengecat ulang secara langsung alih-alih meninggalkan
kuas usang.

**Pita Kerja** adalah elemen khasnya: tulang punggung garis rambut menerus dengan stempel
bernomor dan baris tool bersarang. Penomoran dibenarkan di sini karena langkah agen memang
sebuah urutan dan urutannya membawa informasi yang dibutuhkan pembaca.

**Baris organ** di header adalah tiga segmen yang menyala dengan rona masing-masing saat sebuah
fakultas mengambil alih — jawaban jujur atas apakah AutoWork sedang berpikir, melihat layar
Anda, atau menyentuh file Anda.

**Komposisi** dirangkai manual di `AppServices`, bukan lewat kontainer DI. Grafnya berisi
belasan objek yang bentuknya tidak pernah berubah, dan biaya refleksi sebuah kontainer jatuh
langsung pada waktu sampai jendela muncul.

## Rujukan proyek

| Proyek | Bergantung pada | Berisi |
|---|---|---|
| `AutoWork.Core` | — | Konfigurasi, preset, rahasia, `PathGuard`, persetujuan, log tindakan, pengetahuan, kontrak agen |
| `AutoWork.Providers` | Core | Factory klien chat, klien Anthropic, embedding, uji koneksi |
| `AutoWork.Tools` | Core | File, shell, dokumen, data, gambar, tangkapan layar, input, web |
| `AutoWork.Agents` | Core, Providers, Tools | Planner, orkestrator, sub-agen, peringkasan, visi, tool pengetahuan, registri tool |
| `AutoWork.Integrations` | Core | Kerangka konektor dan konektornya |
| `AutoWork.Desktop` | semua | Antarmuka Avalonia, view model, tema, pelokalan |
