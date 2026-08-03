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
dan PDF. Dihasilkan lewat OOXML sungguhan, bukan dengan meminta model menuliskan XML, dan
dibuka oleh aplikasi aslinya — bukan sekadar diterima validator.

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

**Koordinasi sub-agen.** Langkah yang benar-benar independen berjalan paralel, masing-masing
dengan konteksnya sendiri, sehingga pemakaian token tidak menumpuk.

**Auto-compact.** Saat sesi panjang mendekati batas jendela konteks model, giliran percakapan
lama diringkas otomatis — dengan hati-hati, tidak pernah memisahkan pemanggilan tool dari
hasilnya.

**Basis pengetahuan.** Memori per topik yang bertahan antar sesi, dicari lewat embedding bila
model embedding tersedia, dan lewat kata kunci bila tidak.

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
- **Kemampuan berbahaya bersifat opt-in.** Menghapus, perintah shell, dan kendali
  mouse/keyboard semuanya mati sampai Anda menyalakannya, dan meminta persetujuan secara
  bawaan setelah menyala.
- **Persetujuan tampil inline dan spesifik.** Permintaan muncul di alur pekerjaan dengan
  perintah atau daftar file yang sebenarnya ditampilkan — bukan modal yang lama-lama Anda
  tutup tanpa membaca.

![Kartu persetujuan meminta izin sebelum sebuah file ditulis](docs/images/consent-card.png)

- **Semuanya dicatat.** Setiap tindakan masuk ke log JSONL append-only, terlihat di tampilan
  Aktivitas.

**Batasan yang jujur.** Kendali input (mouse dan keyboard sintetis) tidak dapat di-sandbox oleh
proses ini — begitu input dibangkitkan, ia menuju jendela mana pun yang sedang fokus. Perintah
shell berjalan dengan hak akses penuh akun Anda. Keduanya mati secara bawaan, dan pengaman di
sekitarnya berupa persetujuan dan keterlihatan, bukan pengurungan. Di Windows penyimpanan
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
