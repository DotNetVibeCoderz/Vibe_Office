# Model keamanan

*AutoWork — Gravicode Studios, dipimpin oleh Kang Fadhil*

AutoWork menjalankan agen AI yang punya akses ke filesystem Anda. Itu berguna justru karena ia
berkuasa, dan karena itu batasannya harus eksplisit, bisa diuji, dan dijelaskan apa adanya.

Dokumen ini menyatakan apa yang dijamin, apa yang hanya digerbangi persetujuan, dan apa yang
sama sekali tidak terlindungi.

---

## Klaimnya

**AutoWork hanya bisa menjangkau folder yang Anda izinkan.**

Semua di bawah ini ada untuk membuat klaim itu benar dan dapat diverifikasi.

## Menolak secara bawaan

Instalasi baru hanya punya satu izin folder: `Documents\AutoWork`. Setiap operasi file di luar
folder itu ditolak sampai Anda menambahkan folder di **Pengaturan › Izin**.

![Pengaturan › Izin: folder yang diberikan dan sakelar kemampuan](../images/settings-permissions.png)

Ini memang sengaja dibuat sedikit merepotkan. Asisten yang bisa membaca seluruh direktori rumah
Anda begitu dipasang adalah pertukaran yang lebih buruk daripada asisten yang memaksa Anda
berpikir sepuluh detik.

## Satu titik pemeriksaan: `PathGuard`

Setiap tool filesystem melewati `PathGuard` sebelum menyentuh apa pun. Pemeriksaan berjalan
berurutan, dan semuanya harus lolos:

**1. Kanonikalisasi, sekaligus menyelesaikan symlink.**
Path diekspansi, dijadikan absolut, lalu ditelusuri komponen demi komponen dengan setiap symbolic
link diselesaikan hingga target akhirnya. Tanpa langkah ini, sebuah link di dalam folder yang
diizinkan yang menunjuk ke `~/.ssh` menjadi jalan bebas ke mana saja di disk. Link rusak atau
melingkar ditolak.

**2. Menolak lokasi terlindungi.**
Direktori internal sistem operasi (`C:\Windows`, `/etc`, `/usr/bin`, `/System`, …) dan folder
data AutoWork sendiri tidak pernah bisa dijangkau, bahkan bila Anda memberinya izin secara
eksplisit. Folder AutoWork menyimpan penyimpanan rahasia, sehingga aturan ini menutup celah nyata
di mana agen membaca kuncinya sendiri.

**3. Mewajibkan berada di dalam folder yang diizinkan.**
Path kanonik harus berada di dalam folder yang Anda izinkan, dibandingkan **per segmen path** —
sehingga izin pada `~/Dokumen` tidak tanpa sengaja mencakup `~/Dokumen-cadangan`. Izin paling
spesifik yang menang, sehingga sub-folder hanya-baca bisa mempersempit induk yang baca/tulis.

**4. Menerapkan pola terlarang.**
Pola glob yang ditolak bahkan di dalam folder yang diizinkan. Bawaannya:

```
**/.ssh/**    **/.aws/**    **/.gnupg/**    **/.git/config
**/*.pem      **/*.key      **/*.pfx        **/id_rsa*
**/.env       **/.env.*     **/secrets.json
**/AppData/Local/Microsoft/Credentials/**   **/Library/Keychains/**
```

Tambahkan pola Anda sendiri di Pengaturan.

### Diuji, bukan sekadar diklaim

`SandboxTests` ditulis sebagai rangkaian percobaan pelolosan:

- penelusuran relatif (`granted/../private/secrets.txt`)
- path absolut di luar semua folder yang diizinkan
- folder bersaudara yang namanya berawalan sama dengan folder yang diizinkan
- **symlink di dalam folder yang diizinkan yang menunjuk ke luar**
- menulis ke izin hanya-baca
- membaca pola terlarang di dalam folder yang diizinkan
- menjangkau folder data AutoWork sendiri setelah diberi izin eksplisit
- beroperasi tanpa izin sama sekali

Semuanya diharapkan gagal dengan kode alasan yang spesifik. Bila Anda mengubah `PathGuard`,
tes-tes inilah yang memberi tahu apakah Anda merusak janji utama produk ini.

## Kemampuan bersifat opt-in

| Kemampuan | Bawaan | Catatan |
|---|---|---|
| Membaca folder yang diizinkan | Aktif setelah diizinkan | — |
| Menulis folder yang diizinkan | Per izin | Tiap folder hanya-baca atau baca/tulis |
| Menghapus | **Mati** | Saklar terpisah dari menulis |
| Hapus lunak | Aktif | File terhapus dipindah ke folder daur ulang di dalam direktori terlindungi AutoWork, sehingga agen tidak bisa membacanya kembali |
| Perintah shell | **Mati** | Daftar putih executable opsional; digerbangi persetujuan |
| Penangkapan layar | Aktif | Digerbangi persetujuan |
| Kendali mouse dan keyboard | **Mati** | Digerbangi persetujuan |
| Jaringan | Aktif | Daftar putih host opsional |

Kemampuan yang mati bukan sekadar ditolak — tool yang memakainya **tidak pernah dijelaskan ke
model sama sekali**. Ia tidak bisa mencoba sesuatu yang keberadaannya tidak ia ketahui.

## Persetujuan

Tindakan yang menulis, menghapus, menjalankan perintah, atau mengendalikan input akan memunculkan
permintaan persetujuan. Permintaan itu tampil **inline di Pita Kerja**, bukan sebagai dialog
modal, dan menampilkan baris perintah atau daftar file yang sesungguhnya, bukan ringkasannya.

Dialog modal melatih orang untuk menutupnya tanpa membaca. Menjaga permintaan tetap menempel pada
konteksnya adalah satu-satunya cara agar persetujuan itu bermakna.

"Izinkan selama sesi ini" tersedia untuk jenis tindakan yang berulang dan dapat dibalik — menulis,
jaringan, tangkapan layar — sehingga proses 200 file cukup bertanya sekali. Opsi itu **sengaja
tidak ditawarkan untuk penghapusan dan perintah shell**: izin berdiri adalah afordansi yang salah
untuk tindakan yang tidak dapat dibatalkan.

## Log tindakan

Setiap tindakan tercatat dalam file JSON Lines append-only di `logs/actions.jsonl`, berisi:

```
waktu · id sesi · subsistem · tool · ringkasan satu baris · hasil · path · durasi · galat
```

Bersifat append-only sehingga kegagalan di tengah penulisan tidak merusak entri sebelumnya, dan
dirotasi pada 8 MB. Tampilan Aktivitas mengalirkannya secara langsung selama sesi berjalan.

Hasil mencakup `Denied`, sehingga percobaan yang ditolak pun tercatat. Bila agen mencoba sesuatu
yang tidak diizinkan, Anda akan melihatnya.

## Rahasia

Kunci API disimpan terpisah dari konfigurasi dan dienkripsi dengan **AES-GCM**. Kunci
enkripsinya disimpan di file pendamping.

- **Windows** — kunci AES dibungkus dengan **DPAPI** (lingkup pengguna saat ini). Penyimpanan
  tidak dapat dibaca oleh akun lain atau dipindahkan ke komputer lain.
- **Linux dan macOS** — tidak ada padanan DPAPI yang dipakai di sini. File kunci dibuat dengan
  izin khusus pemilik (`0600`) sebelum satu byte pun ditulis. Perlindungannya karena itu setara
  kunci privat SSH: siapa pun yang bisa membaca direktori rumah Anda sebagai Anda bisa membaca
  kunci Anda.

Itu perbedaan nyata dan lebih baik dinyatakan terang-terangan daripada dipoles. Bila Anda butuh
jaminan lebih kuat, rujuk kunci sebagai `env:NAMA_VARIABEL` dan biarkan pengelola rahasia
sungguhan yang menyuntikkannya — AutoWork lalu tidak pernah menyimpannya sama sekali.

`config.json` hanya berisi referensi. Aman disalin atau diversikan.

## Yang *tidak* terlindungi

Berterus terang soal ini lebih berguna daripada daftar fitur yang lebih panjang.

**Input sintetis tidak dapat di-sandbox.** Begitu sebuah ketukan tombol atau klik dibangkitkan,
ia menuju jendela mana pun yang sedang fokus. Tidak ada model izin di dalam proses ini yang bisa
membatasinya. Kemampuan ini mati secara bawaan dan digerbangi persetujuan; keduanya adalah
kendali persetujuan, bukan pengurungan.

**Perintah shell berjalan sebagai Anda.** Direktori kerjanya dipatok di dalam folder yang
diizinkan, dan daftar putih opsional membatasi executable mana yang boleh berjalan — tetapi
perintah itu sendiri memiliki hak akses penuh akun Anda. Bila Anda mengaktifkan akses shell,
Anda mempercayai penilaian model atas perintah. Biarkan mati kecuali memang perlu, dan gunakan
daftar putih saat mengaktifkannya.

**Pembatasan jaringan bersifat kasar.** Daftar putih host berlaku untuk tool web milik AutoWork.
Perintah shell tetap bisa menjangkau jaringan.

**Prompt injection adalah risiko nyata.** File atau halaman web yang dibaca agen dapat memuat
teks yang berusaha mengalihkannya. Sandbox adalah mitigasinya — instruksi tidak dapat memberi
izin baru, dan apa pun di luar folder yang Anda izinkan tetap tak terjangkau, apa pun yang
berhasil dibujukkan kepada model. Gerbang persetujuan pada tindakan destruktif adalah lapis
kedua. Keduanya tidak membuat injection mustahil; keduanya membatasi kerusakannya.

**Penyedia model melihat apa yang Anda kirim.** Isi file, tangkapan layar, dan data hasil
ekstraksi dikirim ke penyedia yang Anda konfigurasikan. Untuk pekerjaan yang tidak boleh keluar
dari komputer, gunakan Ollama atau LM Studio dan AutoWork beroperasi sepenuhnya luring.

## Rekomendasi penyiapan

**Hati-hati** — bawaan. Beri satu folder proyek sebagai baca/tulis. Biarkan penghapusan, shell,
dan kendali input tetap mati.

**Sehari-hari** — beri folder kerja Anda. Aktifkan penghapusan dengan hapus lunak menyala.
Biarkan shell dan input tetap mati.

**Pengguna mahir** — aktifkan shell dengan daftar putih executable (`git`, `python`, `ffmpeg`).
Biarkan "tanya sebelum setiap perintah" tetap menyala. Aktifkan kendali input hanya untuk sesi
yang memang membutuhkannya.

**Terisolasi jaringan** — Ollama sebagai penyedia, jaringan mati, tanpa integrasi. Tidak ada apa
pun yang meninggalkan komputer.

## Melaporkan kerentanan

Mohon laporkan masalah keamanan secara privat ke Gravicode Studios, bukan lewat issue publik.
