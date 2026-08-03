# Pemecahan masalah

*AutoWork — Gravicode Studios, dipimpin oleh Kang Fadhil*

## Mulai dari sini

Dua tempat ini menjawab sebagian besar pertanyaan:

- **Aktivitas** — setiap tindakan yang dilakukan AutoWork, termasuk yang ditolak, beserta
  alasannya.
- `logs/actions.jsonl` di folder data Anda — hal yang sama, bisa di-grep.

Tindakan dengan hasil `Denied` berarti ada izin yang menghentikannya, dan kolom `error`
menyebutkan izin mana.

---

## Aplikasi tidak mau menyala

**Tidak terjadi apa-apa saat dijalankan.** Jalankan dari terminal untuk melihat galatnya:

```bash
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

**"You must install .NET to run this application."** Pasang
[runtime atau SDK .NET 10](https://dotnet.microsoft.com/download).

**Teks tampil sebagai kotak (Linux).** Pasang `fontconfig`.

**Selalu terbuka di halaman Pengaturan.** Itu memang disengaja bila belum ada model yang
dikonfigurasi. Tambahkan satu, dan ia akan terbuka di halaman Kerja.

---

## Model

**"No model is configured yet."** Pengaturan › Model → Tambah model → pilih penyedia → tempel
kunci.

**"The API key was rejected."** Kunci salah, kedaluwarsa, atau milik penyedia lain. Bila kotaknya
menampilkan `••••••••`, artinya ada kunci tersimpan — ketik ulang untuk menggantinya.

**"The endpoint responded 404."** Biasanya ID model, bukan URL-nya. ID model bawaan hanyalah
titik awal dan vendor kerap memensiunkannya; cocokkan ID dengan daftar terbaru penyedia Anda.
Pastikan juga endpoint memuat segmen versi di tempat yang diharapkan vendor, misalnya
`https://api.openai.com/v1`.

**"Could not reach the endpoint."** Untuk penyedia lokal, pastikan servernya berjalan:

```bash
curl http://localhost:11434/api/tags      # Ollama
```

**Ollama terhubung tapi agen tidak melakukan apa-apa.** Model harus mendukung pemanggilan tool.
Banyak model kecil tidak mendukungnya. Coba model yang didokumentasikan mendukung tools, dan
pastikan **Tools** tercentang di bagian Kemampuan.

**"Rate limited by the provider."** Tunggu sebentar, atau turunkan `maxParallelSubAgents` ke 1.

---

## Izin

**Semuanya ditolak.** Belum ada folder yang diizinkan. Pengaturan › Izin → Tambah folder.

**"…is outside every granted folder."** Path berada di luar izin. Perhatikan bahwa izin
dibandingkan per segmen path, sehingga `~/Dokumen` tidak mencakup `~/Dokumen-cadangan`.

**"…matches a blocked pattern."** Daftar terlarang bawaan memblokir file berbentuk kredensial
(`.env`, `*.pem`, `.ssh/**`). Ubah daftarnya di Pengaturan bila ada file sah yang ikut terjaring.

**"…is granted as read-only."** Ubah folder tersebut menjadi baca/tulis.

**"Deleting is turned off."** Aktifkan *Izinkan menghapus file*. Biarkan *hapus lunak* menyala
agar penghapusan tetap bisa dipulihkan.

**Symlink di dalam folder yang diizinkan ditolak.** Ini perilaku yang benar — link diselesaikan
ke targetnya, dan target di luar izin Anda tetap tak terjangkau. Beri izin ke folder targetnya
bila memang itu yang Anda maksud.

**AutoWork tidak bisa melihat folder datanya sendiri.** Juga disengaja. Folder itu menyimpan
penyimpanan rahasia dan permanen tidak dapat dijangkau.

---

## File

**"already exists."** Penulisan file tidak menimpa secara bawaan. Katakan pada agen untuk
menggantinya, dan ia akan mengirim `overwrite=true`.

**Rename massal ditolak dengan pesan tabrakan.** Dua file akan berakhir dengan nama yang sama.
Tidak ada yang dipindahkan — rename massal yang setengah jalan lebih buruk daripada yang ditolak.
Sesuaikan polanya.

**"…is over the read limit."** Bawaannya 32 MB. Naikkan `maxReadBytes` di `config.json`, atau
minta agen memakai tool data, yang membaca secara mengalir alih-alih memuat seluruh file.

**"looks like a binary file."** Gunakan `data_extract_pdf`, `data_read_csv`, `doc_read_excel`,
atau tool gambar, bukan `files_read`.

**Ke mana file yang saya hapus?** Ke `recycle/<tanggal>/` di folder data Anda, bila hapus lunak
menyala.

---

## Dokumen

**Formula Excel tampil sebagai teks.** Nilai sel yang diawali `=` otomatis menjadi formula. Bila
sebuah nilai memang dimaksudkan sebagai teks harfiah, ia tidak boleh diawali `=`.

**File yang dihasilkan tidak bisa dibuka.** Mohon laporkan — test suite menjalankan validator
OpenXML resmi atas file Word dan PowerPoint yang dihasilkan, sehingga file yang cacat adalah bug
sungguhan.

**Ekstraksi PDF hampir tidak menghasilkan apa-apa.** PDF tersebut hasil pindaian tanpa lapisan
teks. AutoWork menyatakannya secara eksplisit. Tangkap dengan tool layar lalu baca dengan model
visi.

---

## Layar dan input

**"Screen capture is turned off."** Aktifkan di Pengaturan › Izin.

**Tangkapan layar gagal di Linux.** Pasang salah satu pembantu: `grim` (Wayland),
`gnome-screenshot`, `spectacle`, `imagemagick`, atau `scrot`. Di Wayland, compositor mungkin juga
perlu memberi izin.

**Tangkapan layar gagal di macOS.** Berikan izin **Screen Recording** di System Settings ›
Privacy & Security, lalu jalankan ulang AutoWork.

**"No vision-capable model is configured."** `screen_look` memerlukan model visi. Tambahkan satu,
centang **Visi** pada kemampuannya, lalu pilih sebagai model visi.

**Kendali input tidak berefek.** Fitur ini mati secara bawaan. Di macOS, pasang `cliclick` dan
berikan izin **Accessibility**. Di Linux, pasang `xdotool` (X11). Di Windows, perlu dicatat bahwa
input sintetis tidak dapat menjangkau jendela yang berjalan dengan hak akses lebih tinggi
daripada AutoWork.

---

## Perilaku agen

**Berhenti lebih awal.** Entah `maxSteps` tercapai atau tiga langkah gagal berturut-turut. Log
Aktivitas menunjukkan yang mana.

**Ia mengaku melakukan sesuatu yang tidak dilakukannya.** Aktifkan *Periksa hasil kerja sendiri
sebelum menyatakan selesai* di Pengaturan › Agen. Tahap verifikasi menilai berdasarkan hasil tool,
bukan pengakuan model.

**Ia terus meminta persetujuan.** Gunakan *Izinkan selama sesi ini* bila tersedia. Opsi itu
sengaja tidak ditawarkan untuk penghapusan dan perintah shell.

**Muncul "Konteks diringkas" di tengah sesi.** Normal — percakapan mendekati batas jendela dan
giliran lama diringkas. Bila setelahnya agen kehilangan arah, naikkan `compactKeepRecentTurns`,
atau turunkan `autoCompactThreshold` agar peringkasan terjadi lebih awal dan lebih landai.

**Sesi terasa lambat.** Aktifkan sub-agen, pakai model lebih cepat untuk peran eksekutor sambil
mempertahankan model kuat untuk perencanaan, atau jalankan secara lokal dengan Ollama.

---

## Data dan pemulihan

**Mengulang dari nol.** Tutup AutoWork lalu hapus folder datanya — `%APPDATA%\AutoWork`,
`~/.config/AutoWork`, atau `~/Library/Application Support/AutoWork`. Kunci API Anda ikut terhapus.

**Pengaturan tidak mau tersimpan.** Pastikan folder data dapat ditulisi dan ruangnya cukup.

**Konfigurasi rusak.** AutoWork memindahkannya menjadi `config.json.broken-<timestamp>` lalu
menyala dengan nilai bawaan, bukan menolak berjalan.

**Memindahkan semuanya.** Setel `AUTOWORK_HOME` ke lokasi baru.

---

## Melaporkan bug

Sertakan:

1. Apa yang Anda minta dikerjakan AutoWork
2. Apa yang terjadi
3. Baris terkait dari Aktivitas atau `logs/actions.jsonl`
4. Sistem operasi Anda, serta penyedia dan ID model
5. `dotnet --list-sdks`

Mohon jangan menempelkan kunci API. Entri log tidak memuatnya, tetapi path konfigurasi bisa saja
ikut muncul.
