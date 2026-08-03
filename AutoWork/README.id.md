# AutoWork

**Rekan kerja digital Anda.** Asisten desktop otonom yang merapikan file, membuat dokumen
profesional, dan menjalankan workflow multi-langkah — di komputer Anda sendiri, di dalam
sandbox yang Anda tentukan.

Dibangun dengan .NET 10 dan Avalonia UI. Berjalan di Windows, macOS, dan Linux.

> 🇬🇧 **English:** [README.md](README.md) · Full documentation in [docs/en](docs/en)

![AutoWork menyusun rencana lalu mengerjakannya di Pita Kerja](docs/images/work-tape.png)

---

## Apa yang dikerjakan

AutoWork menerima permintaan dalam bahasa biasa, merencanakannya, mengerjakannya, lalu
memeriksa hasil kerjanya sendiri.

```
"Baca semua PDF invoice di ~/Documents/Invoice, ambil nama vendor,
 tanggal, dan totalnya, lalu buatkan rekap Excel beserta grand total."
```

AutoWork membaca PDF-nya, mengekstrak angkanya, menulis file `.xlsx` sungguhan dengan formula
hidup, dan melaporkan persis apa yang dihasilkan — sementara setiap langkah terlihat saat
berlangsung dan tercatat di log yang bisa Anda audit kemudian.

## Arsitektur

AutoWork dibangun sebagai tiga subsistem yang bekerja sama. Ini bukan kiasan — begitulah kode
ini disusun, dan setiap tindakan di antarmuka diatribusikan ke salah satunya.

| Subsistem | Wujudnya | Tugasnya |
|---|---|---|
| **Brain** (Pikir) | `AutoWork.Agents` | Merencanakan pekerjaan multi-langkah, bernalar, pulih dari kegagalan, meringkas konteksnya sendiri saat jendela hampir penuh, dan memverifikasi hasil sebelum menyatakan berhasil. |
| **Eyes** (Lihat) | `AutoWork.Tools` + model visi | Menangkap layar, membaca dialog, tabel, dan teks kecil yang tidak tersedia sebagai file. |
| **Hands** (Tindak) | `AutoWork.Tools` | File, shell, dokumen, data, gambar, input sintetis, dan jaringan — semua yang menyentuh mesin. |

## Fitur

**Eksekusi otonom.** Berikan tujuan; AutoWork menyusun rencana dengan kriteria keberhasilan
yang bisa diperiksa, mengerjakannya langkah demi langkah, pulih dari kegagalan tool, dan
memverifikasi hasilnya. Progres mengalir langsung ke *Pita Kerja* — linimasa bernomor berisi
segala yang dilakukannya.

![Setiap pemanggilan tool di Pita Kerja, lengkap dengan argumen, hasil, dan waktunya](docs/images/work-tools.png)

Setiap pemanggilan tool ditampilkan beserta argumen yang diberikan, apa yang dikembalikan, dan
berapa lama. Tidak ada yang terjadi tanpa bisa Anda periksa setelahnya.

**Akses file lokal langsung.** Baca, tulis, pindah, salin, ganti nama. Rename massal dengan
regular expression dan penataan folder berdasarkan jenis, ekstensi, atau tanggal — selalu
dengan pratinjau *dry run* sebelum ada yang berpindah.

**Dokumen profesional.** Workbook Excel dengan formula hidup, dokumen Word, deck PowerPoint,
PDF, dan berkas OpenDocument untuk LibreOffice. Dihasilkan lewat OOXML dan ODF sungguhan, bukan
dengan meminta model menuliskan XML, dan dibuka oleh aplikasi aslinya — bukan sekadar diterima
validator. Bisa juga mengubah dokumen apa pun yang terbaca menjadi Markdown, dan mengisi templat
`.docx` ber-`{{placeholder}}` — lengkap dengan laporan placeholder mana yang masih kosong,
alih-alih mengirim dokumen yang berlubang.

Di bawah ini satu pekerjaan — *"riset retrieval-augmented generation di web, lalu tulis laporan
singkat dan deck empat slide"* — dijalankan dengan DeepSeek, beserta dua file hasilnya yang
dibuka di Word dan PowerPoint.

![Laporan Word hasil generate, dibuka di Microsoft Word](docs/images/deepseek-report.png)

![Empat slide hasil generate, dirender oleh PowerPoint](docs/images/deepseek-deck.png)

**Ekstraksi data.** Teks PDF, profiling CSV (tipe kolom, rentang, jumlah kosong) dan
pembersihan. Profiling dulu berarti model bernalar atas *ringkasan* 50.000 baris, bukan
tenggelam di dalamnya.

**Riset web.** Mencari di internet lalu membaca hasilnya. Tavily bila Anda menyediakan kunci,
DuckDuckGo dan Wikipedia bila tidak — jadi riset bukan fitur berbayar.

**Peramban tempat Anda sudah masuk.** Mengambil URL hanya memberi halaman yang dilihat orang
asing; sebagian besar yang ingin diotomatiskan justru ada di balik login. Karena itu AutoWork
bisa menjalankan peramban sungguhan — Edge atau Chrome yang sudah Anda punya, tanpa unduhan —
dengan profil tersendiri yang tetap masuk antar-sesi. Ia membaca halaman, mendaftar apa yang bisa
diklik, mengisi kolom, dan mengeklik berdasarkan teks yang terlihat. Mati secara bawaan, terpisah
dari izin jaringan biasa, dan bertanya sebelum setiap navigasi dan klik — karena di situs itu ia
bertindak sebagai Anda.

**Pekerjaan yang berulang.** Simpan sebuah tugas, lalu jalankan setiap Senin pagi, atau setiap
kali sebuah folder berubah. Run terjadwal berlangsung di halaman Kerja persis seperti Anda
mengetiknya — rencana yang sama, kartu persetujuan yang sama, riwayat yang sama — jadi tugas yang
Anda tinggal berperilaku seperti yang Anda tunggui. Waktu yang terlewat tidak menumpuk: aplikasi
yang ditutup sepanjang akhir pekan bangun tanpa utang run.

**Rapat.** Arahkan AutoWork ke sebuah rekaman, ia mengubahnya jadi teks lalu menarik keputusan
dan siapa bertanggung jawab atas apa. Model suaranya berjalan **di komputer Anda** — AutoWork
tidak membawa model apa pun dan memakai yang sudah Anda punya — jadi rekaman berisi orang lain
tidak meninggalkan mesin Anda kecuali Anda memang memilih memakai layanan.

**Jawaban tampil sambil ditulis.** Penalaran panjang muncul di Pita Kerja saat terbentuk, bukan
sekaligus setelah langkahnya selesai. Penyedia yang tidak mendukung streaming kembali menunggu
seperti biasa.

**Koordinasi sub-agen.** Langkah yang benar-benar independen berjalan paralel, masing-masing
dengan konteksnya sendiri, sehingga pemakaian token tidak menumpuk.

**Auto-compact.** Saat sesi panjang mendekati batas jendela konteks model, giliran percakapan
lama diringkas otomatis — dengan hati-hati, tidak pernah memisahkan pemanggilan tool dari
hasilnya.

**Jeda dan koreksi.** Melihat arahnya sudah melenceng? Jeda, tuliskan maksud Anda yang
sebenarnya, lalu biarkan lanjut — tanpa perlu membatalkan dan mengetik ulang seluruh pekerjaan.

![Run yang ditahan di batas langkah, koreksi sudah diketik dan siap dilanjutkan](docs/images/work-pause-steer.png)

Jeda berlaku di batas langkah berikutnya, bukan di tengah tindakan, jadi tidak ada yang
tertinggal setengah jadi, dan koreksi Anda disampaikan ke model *sebelum* langkah yang hendak
diubahnya. Anda juga boleh mengirim koreksi tanpa menjeda; keduanya sama-sama diterapkan di batas
berikutnya.

**Riwayat run.** Setiap run disimpan: apa yang Anda minta, rencananya, setiap pemanggilan tool
dan hasilnya.

![Daftar run yang sudah lewat, lengkap dengan hasil dan tombol menjalankannya lagi](docs/images/history.png)

Buka kembali salah satunya dan isinya diputar ulang di Pita Kerja persis seperti tampilannya saat
berjalan — tangkapan layar di bawah ini adalah aplikasi yang baru dijalankan lagi, membaca run
yang sudah selesai dari disk — atau jalankan pekerjaan yang sama sekali klik.

![Run selesai yang dibuka kembali: rencana dan Pita Kerja disusun ulang dari transkrip tersimpan](docs/images/history-reopened.png)

Lama penyimpanan Anda yang tentukan, dan fiturnya bisa dimatikan sepenuhnya.

**Lihat perubahannya sebelum terjadi.** Saat AutoWork hendak menimpa berkas yang sudah ada, kartu
persetujuan menampilkan diff-nya — apa yang hilang, apa yang datang — bukan sekadar nama berkas
dan jumlah byte.

![Kartu persetujuan yang menampilkan baris mana yang dihapus dan ditambahkan](docs/images/consent-diff.png)

Berkas baru tidak menampilkan diff, karena "membuat berkas" dan "menulis ulang seluruh isinya"
tidak boleh terlihat sama.

**Jawaban tetap.** "Selalu izinkan menulis di ~/Projects." "Jangan pernah izinkan menghapus."
Aturan, bukan antrean klik.

![Editor jawaban tetap di Pengaturan › Izin](docs/images/settings-rules.png)

Dua hal berlaku untuk setiap aturan: *jangan pernah* selalu mengalahkan *selalu*, dan aturan
hanya mengubah apa yang **ditanyakan** kepada Anda — bukan jangkauan AutoWork. Aturan izin yang
menunjuk folder yang belum Anda berikan tetap ditolak sandbox.

**Berkas terhapus bisa kembali.** Apa pun yang dihapus AutoWork dipindahkan dulu, lengkap dengan
catatan asalnya, dan halaman Pemulihan mengembalikannya.

![Halaman Pemulihan: berkas terhapus lengkap dengan lokasi asalnya](docs/images/recycle.png)

Kalau sudah ada berkas lain di sana, AutoWork memberi tahu dan menunggu, bukan menimpa pekerjaan
Anda.

**Meter yang tidak menebak.** Setiap run melaporkan token sesuai yang disebutkan penyedia. Isi
harga dari penyedia Anda, maka biayanya ikut dilaporkan.

![Run yang sudah lewat, lengkap dengan jumlah token dan biayanya](docs/images/history-meter.png)

AutoWork sengaja tidak membawa tabel harga: harga berubah lebih cepat daripada nama model, dan
angka bawaan yang basi lalu melaporkan biaya lebih rendah dari kenyataan jauh lebih buruk
daripada sekadar menampilkan token. Kalau penyedia tidak melaporkan pemakaian, totalnya ditulis
"minimal", bukan disamarkan jadi angka pasti.

**Basis pengetahuan.** Memori per topik yang bertahan antar sesi, dicari lewat embedding bila
model embedding tersedia, dan lewat kata kunci bila tidak. Tersedia juga indeks lokal yang tidak
butuh model, kunci, maupun jaringan sama sekali — kalah dalam memahami makna dibanding model
daring, tetapi tidak pernah keluar dari komputer Anda. Tambahkan catatan secara manual, atau
impor file Word, PowerPoint, Excel, PDF, CSV, HTML, dan Markdown — nama file menjadi judulnya dan
isinya menjadi catatannya.

![Tampilan Pengetahuan, dengan catatan manual maupun impor dari file](docs/images/knowledge.png)

**Skill.** Instruksi yang diikuti agen untuk jenis pekerjaan tertentu, dipasang dari repositori
GitHub pilihan Anda. Skill terpasang sebagai satu folder utuh — dokumen rujukan, template, dan
skrip, bukan sekadar manifesnya — karena instruksinya kerap berbunyi "lihat REFERENCE.md" atau
"jalankan scripts/fill.py". Hanya nama dan deskripsi satu barisnya yang masuk ke prompt; sisanya
dibuka agen saat pekerjaannya memang membutuhkan, jadi skill yang tak terpakai nyaris tanpa biaya.

Menjalankan skrip bawaannya berarti menjalankan kode dari repositori orang lain, jadi ada sakelar
tersendiri yang mati secara bawaan dan meminta persetujuan sebelum tiap eksekusi. Library yang
dibutuhkan skrip dipasang ke environment milik skill itu sendiri — daftar paketnya ditampilkan
sebelum apa pun diunduh.

![Galeri Skill, menelusuri anthropics/skills dan obra/superpowers](docs/images/skills-gallery.png)

**Server MCP.** Pinjam tool dari server Model Context Protocol mana pun — peramban sungguhan,
indeks dokumentasi, API vendor. Katalog bawaannya mencakup Figma, Canva, Blender, GitHub,
Atlassian, Linear, Asana, Chrome DevTools, Microsoft Learn, Azure DevOps, MarkItDown, dokumentasi
AWS, dan lainnya. Setiap entri dibaca langsung dari halaman resmi vendornya, bukan dari daftar
direktori, dan entri yang bukan terbitan vendor diberi label — "server milik Figma" dan "server
Figma buatan orang" adalah dua hal berbeda untuk dititipi akun Anda.

![Galeri MCP, menguji server sebelum diaktifkan](docs/images/mcp-gallery.png)

Sebagian besar server MCP tidak ada di katalog mana pun, jadi Anda bisa menambahkannya manual:
perintah beserta argumennya, atau sebuah URL, plus environment variable yang diminta halaman
vendor. Token disimpan di secret store, tidak pernah masuk ke `config.json`. Tautan ke koleksi
Microsoft, Google, AWS, dan koleksi resmi tersedia di sebelah formulirnya untuk mencari yang belum
ada di daftar ini.

![Menambahkan server MCP secara manual, dengan tautan ke koleksi vendor](docs/images/mcp-manual.png)

**Model apa pun.** OpenAI, Azure OpenAI, Anthropic, Google Gemini, Ollama, DeepSeek, Qwen,
Moonshot, OpenRouter, LM Studio — atau endpoint apa pun yang kompatibel dengan OpenAI. Diatur
lewat aplikasi, file konfigurasi, atau environment variable.

**Integrasi.** GitHub, Google Drive, Gmail, Notion, Asana, dan PayPal.

**Sandbox sungguhan.** Lihat di bawah — bagian ini yang paling penting.

## Model keamanan

Klaim utama AutoWork: **ia hanya bisa menjangkau folder yang Anda izinkan.**

- **Tolak secara bawaan.** Instalasi baru tidak bisa membaca maupun menulis apa pun. Anda
  memberi izin folder secara eksplisit, sebagai hanya-baca atau baca/tulis.
- **Satu titik pemeriksaan.** Setiap operasi filesystem melewati `PathGuard`, yang
  mengkanonikalisasi path, *menyelesaikan symlink*, menolak direktori sistem dan direktori
  internal AutoWork, mewajibkan path berada di dalam folder yang diizinkan, lalu menerapkan
  daftar pola terlarang.
- **Symlink diselesaikan, bukan dipercaya.** Link di dalam folder yang diizinkan yang menunjuk
  ke `~/.ssh` akan ditolak. Ini diuji.
- **Kredensial diblokir bahkan di dalam folder yang diizinkan** — `.ssh`, `.aws`, `.env`,
  `*.pem`, keychain, dan sejenisnya, secara bawaan.
- **Kemampuan berbahaya bersifat opt-in.** Menghapus, perintah shell, kendali mouse/keyboard,
  server MCP, dan skrip skill semuanya mati sampai Anda menyalakannya, dan meminta persetujuan
  secara bawaan setelah menyala.
- **Persetujuan tampil inline dan spesifik.** Permintaan muncul di alur pekerjaan dengan
  perintah atau daftar file yang sebenarnya ditampilkan — bukan modal yang lama-lama Anda
  tutup tanpa membaca.

![Kartu persetujuan meminta izin sebelum sebuah file ditulis](docs/images/consent-card.png)

- **Semuanya dicatat.** Setiap tindakan masuk ke log JSONL append-only, terlihat di tampilan
  Aktivitas.

**Batasan yang jujur.** Kendali input (mouse dan keyboard sintetis) tidak dapat di-sandbox oleh
proses ini — begitu input dibangkitkan, ia menuju jendela mana pun yang sedang fokus. Perintah
shell berjalan dengan hak akses penuh akun Anda. Server MCP adalah program yang dijalankan
AutoWork dengan hak yang sama, dan `PathGuard` tidak bisa melihat ke dalamnya — karena itu ia
punya sakelar sendiri, dan tiap server tetap mati sampai Anda mengaktifkannya. Skrip bawaan skill
sama ceritanya: kode orang lain, sakelarnya sendiri, persetujuan sebelum tiap eksekusi. Semuanya
mati secara bawaan, dan pengaman di sekitarnya berupa persetujuan dan keterlihatan, bukan
pengurungan. Di Windows penyimpanan
rahasia dienkripsi dengan DPAPI; di Linux dan macOS ia bersandar pada izin file khusus pemilik.
Penjelasan lengkap: [docs/id/keamanan.md](docs/id/keamanan.md).

## Instalasi

**Windows**

```powershell
git clone https://github.com/gravicode/autowork.git
cd autowork\install
.\install.ps1 -Desktop
```

**macOS / Linux**

```bash
git clone https://github.com/gravicode/autowork.git
cd autowork/install
chmod +x install.sh && ./install.sh
```

Membutuhkan [.NET 10 SDK](https://dotnet.microsoft.com/download). Panduan lengkap:
[docs/id/instalasi.md](docs/id/instalasi.md).

## Menjalankan pertama kali

1. **Pengaturan › Model** — tambahkan penyedia dan tempel kunci API (atau arahkan ke Ollama lokal).
2. **Pengaturan › Izin** — beri AutoWork satu folder untuk bekerja.
3. **Kerja** — jelaskan pekerjaannya lalu tekan *Mulai kerja*.

![Pengaturan › Model, dengan satu model Azure OpenAI terkonfigurasi](docs/images/settings-models.png)

Antarmukanya mengikuti tema sistem, dan bisa Anda kunci ke terang atau gelap:

![Tampilan yang sama dalam tema terang](docs/images/work-tape-light.png)

Jika kunci penyedia sudah ada di environment Anda (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`,
`OLLAMA_HOST`, …), AutoWork mendeteksinya saat pertama dijalankan dan langkah 1 sudah selesai.

## Konfigurasi

Tiga cara, dari prioritas terendah ke tertinggi: **file konfigurasi → environment variable →
antarmuka aplikasi.**

```bash
export ANTHROPIC_API_KEY=sk-ant-...      # kunci vendor apa pun terdeteksi otomatis
export AUTOWORK_FOLDERS="$HOME/Documents;$HOME/Downloads"
export AUTOWORK_ALLOW_SHELL=false
```

Konfigurasi tersimpan di `%APPDATA%\AutoWork\config.json` (Windows), `~/.config/AutoWork/`
(Linux), `~/Library/Application Support/AutoWork/` (macOS). Kunci API tidak pernah ditulis ke
sana — hanya referensi ke penyimpanan rahasia atau ke environment variable. Rujukan lengkap:
[docs/id/konfigurasi.md](docs/id/konfigurasi.md).

## Build dari sumber

```bash
dotnet restore AutoWork.slnx
dotnet build AutoWork.slnx
dotnet test tests/AutoWork.Tests/AutoWork.Tests.csproj
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

Menjalankan satu tes saja:

```bash
dotnet test --filter "FullyQualifiedName~SandboxTests.A_symlink_pointing_out_of_the_granted_folder_is_refused"
```

## Struktur proyek

```
src/
  AutoWork.Core           Konfigurasi, rahasia, izin, log tindakan, basis pengetahuan
  AutoWork.Providers      Factory LLM multi-penyedia, klien Anthropic native, embedding
  AutoWork.Tools          File, shell, dokumen, data, gambar, layar, input, web
  AutoWork.Agents         Orkestrator, planner, sub-agen, peringkasan konteks, visi
  AutoWork.Integrations   Kerangka konektor dan konektornya
  AutoWork.Desktop        Antarmuka Avalonia
tests/AutoWork.Tests      xUnit
install/                  Installer dan uninstaller
docs/en · docs/id         Dokumentasi, Inggris dan Bahasa Indonesia
```

## Dokumentasi

| | Bahasa Indonesia | English |
|---|---|---|
| Instalasi | [docs/id/instalasi.md](docs/id/instalasi.md) | [docs/en/installation.md](docs/en/installation.md) |
| Konfigurasi | [docs/id/konfigurasi.md](docs/id/konfigurasi.md) | [docs/en/configuration.md](docs/en/configuration.md) |
| Keamanan | [docs/id/keamanan.md](docs/id/keamanan.md) | [docs/en/security.md](docs/en/security.md) |
| Arsitektur | [docs/id/arsitektur.md](docs/id/arsitektur.md) | [docs/en/architecture.md](docs/en/architecture.md) |
| Integrasi | [docs/id/integrasi.md](docs/id/integrasi.md) | [docs/en/integrations.md](docs/en/integrations.md) |
| Pemecahan masalah | [docs/id/pemecahan-masalah.md](docs/id/pemecahan-masalah.md) | [docs/en/troubleshooting.md](docs/en/troubleshooting.md) |

Roadmap: [PLAN.md](PLAN.md) · Status pengembangan: [Progress.md](Progress.md)

## Kredit

**AutoWork dibuat oleh [Gravicode Studios](https://github.com/gravicode), dipimpin oleh Kang Fadhil.**

Komponen pihak ketiga: [Avalonia](https://avaloniaui.net),
[Microsoft.Extensions.AI](https://github.com/dotnet/extensions),
[ClosedXML](https://github.com/ClosedXML/ClosedXML),
[Open XML SDK](https://github.com/dotnet/Open-XML-SDK),
[QuestPDF](https://www.questpdf.com) (lisensi Community),
[PdfPig](https://github.com/UglyToad/PdfPig),
[ImageSharp](https://sixlabors.com/products/imagesharp/),
[OllamaSharp](https://github.com/awaescher/OllamaSharp),
[CsvHelper](https://joshclose.github.io/CsvHelper/).
