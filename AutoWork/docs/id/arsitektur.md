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
   │      ├─ ringkas bila perlu     ringkas giliran lama sebelum langkah, bukan sesudah,
   │      │                         agar langkah itu punya ruang untuk bekerja
   │      ├─ jalankan langkah       model eksekutor + tool yang dipanggil otomatis
   │      └─ catat hasilnya         langkah gagal hanya bila semua pemanggilan tool gagal
   │
   ├─ 6. Verifikasi                 cocokkan transkrip dengan kriteria keberhasilan
   │
   └─ 7. Laporkan                   ringkasan, durasi, langkah, statistik peringkasan
```

Progres dipancarkan sebagai record `RunEvent` yang imutabel. Core mendefinisikannya dan tidak
memuat tipe UI apa pun; lapisan desktop yang mengubahnya menjadi baris di Pita Kerja.

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
