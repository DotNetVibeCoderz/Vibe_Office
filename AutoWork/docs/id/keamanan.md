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
| Hapus lunak | Aktif | File terhapus dipindah ke folder daur ulang di dalam direktori terlindungi AutoWork, sehingga agen tidak bisa membacanya kembali. Sebuah indeks mencatat asal masing-masing, dan halaman Pemulihan mengembalikannya — tetapi tidak pernah ke dalam folder AutoWork sendiri |
| Perintah shell | **Mati** | Daftar putih executable opsional; digerbangi persetujuan |
| Server MCP | **Mati** | Menjalankan program eksternal dengan hak akses Anda. Lihat di bawah |
| Skrip bawaan skill | **Mati** | Menjalankan kode dari repositori; digerbangi persetujuan |
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

Penulisan yang menimpa berkas yang sudah ada juga menampilkan **apa yang akan berubah** — diff
per baris, dibatasi agar kartunya tetap terbaca. Berkas baru sengaja tidak menampilkan diff:
menampilkan berkas baru sebagai dinding tambahan hijau melatih orang untuk melewatinya justru
pada saat yang penting.

## Aturan tetap

Aturan di Pengaturan › Izin menjawab satu golongan permintaan sekali saja, bukan setiap kali:
*selalu izinkan menulis di ~/Projects*, *jangan pernah izinkan menghapus*.

Ini hal pertama di AutoWork yang membuat sebuah keputusan berumur lebih panjang daripada saat
keputusan itu dibuat, jadi ia dibatasi oleh empat aturannya sendiri.

**Penolakan selalu menang.** Semua aturan yang cocok diperiksa, bukan yang pertama ditemukan, dan
penolakan mengalahkan pengizinan apa pun urutan penulisannya. Penolakan tetap juga mengalahkan
"izinkan selama sesi ini" yang diklik lebih dulu di run yang sama — ia pernyataan yang lebih
disengaja.

**Aturan mengubah apa yang ditanyakan, bukan apa yang diizinkan.** Tindakan yang diizinkan tetap
melewati `PathGuard` saat dijalankan. Arahkan aturan izin ke folder yang belum Anda berikan, dan
tindakannya tetap ditolak. Kerusakan terburuk dari aturan izin yang ceroboh hanyalah Anda tidak
ditanyai untuk sesuatu yang memang akan diizinkan sandbox.

**Aturan izin wajib menyebut folder.** "Selalu izinkan menulis" tanpa lokasi berarti menyetujui
penulisan di mana saja — satu-satunya bentuk yang diam-diam meruntuhkan model persetujuan.
Bentuk itu ditolak saat dibuat, dan diabaikan mesin aturan bila lolos lewat jalan lain. Aturan
izin juga wajib menyebut apa yang diizinkan; "izinkan apa saja, di sini" tidak dapat dinyatakan.

**Hanya menulis dan menghapus yang bisa diizinkan lewat aturan.** Menjalankan perintah,
mengendalikan papan ketik, menangkap layar, dan mengakses jaringan tidak bisa dibatasi ke sebuah
folder, jadi aturan yang mengizinkannya sama dengan menyerahkan kemampuan itu sepenuhnya. Semua
itu sudah punya sakelar yang terlihat di halaman Izin, dan tetap ditanyakan per tindakan.
Sebaliknya, penolakan boleh mencakup jenis apa pun, dengan atau tanpa folder.

Setiap keputusan yang diambil sebuah aturan dicatat di log tindakan lengkap dengan kutipan
aturannya. Persetujuan otomatis yang tidak meninggalkan jejak adalah versi buruk dari fitur ini.

## Peramban yang sudah masuk adalah kemampuan terbesar di sini

`PathGuard` membatasi apa yang bisa dijangkau AutoWork di disk. Ia tidak punya kuasa apa pun atas
peramban yang sudah masuk sebagai Anda: di situs-situs itu, agen adalah Anda, dan tidak ada izin
folder yang membatasinya.

Karena itu ia diperlakukan setimpal.

- **Sakelarnya sendiri**, mati secara bawaan, terpisah dari izin jaringan. Mengambil halaman
  publik dan bertindak sebagai pengguna yang sudah masuk bukan izin yang sama, dan keduanya harus
  aktif sebelum tool-nya ada sama sekali.
- **Profilnya sendiri**, bukan profil asli Anda. Anda masuk ke sana secara sengaja, sekali, jadi
  ia hanya masuk ke akun yang Anda taruh di sana — bukan mewarisi seluruh sesi peramban harian.
- **Terlihat secara bawaan.** Sesuatu yang bertindak sebagai Anda seharusnya bisa Anda tonton.
- **Daftar izin keluar yang sama** dengan tool web, dicocokkan pada batas label, sehingga aturan
  untuk `example.com` tidak mencakup `example.com.evil.net`.
- **Setiap navigasi dan klik bertanya.** Membaca halaman yang sedang terbuka bersifat `Safe`;
  membuka halaman bersifat `System` dengan kartu jaringan; mengeklik dan mengetik bersifat
  `System` dengan kartu input, karena memang itulah yang terjadi.

Yang **tidak** dilindungi: halaman yang berhasil membujuk model untuk mengeklik sesuatu yang
merugikan. Prompt injection dari konten web itu nyata, dan mitigasinya di sini adalah bahwa setiap
tindakan disetujui satu per satu, bukan bahwa kontennya dipercaya. Perlakukan run peramban seperti
Anda menyerahkan laptop dalam keadaan sudah login kepada orang lain.

## Rekaman

Transkripsi mati sampai dikonfigurasi, dan secara bawaan menjalankan model suara **di komputer
Anda**. Opsi jarak jauh mengunggah rekaman dan merupakan pilihan terpisah yang disengaja, dengan
kartu persetujuannya sendiri.

Ini dinyatakan terus terang karena rekaman rapat berbeda dari kebanyakan hal yang disentuh
AutoWork: di dalamnya ada orang lain, yang tidak pernah menyetujui apa pun.

## Server MCP berada di luar sandbox

Server MCP adalah program yang dijalankan AutoWork — biasanya `npx sesuatu` — dan ia berjalan
dengan hak akses penuh akun Anda. `PathGuard` mengatur tool file *milik AutoWork*; ia tidak bisa
menjangkau ke dalam proses lain. Server MCP filesystem bisa membaca apa pun yang diizinkan sistem
operasi, terlepas dari apa yang Anda beri izin di Pengaturan.

Ini kelas kekuatan yang sama dengan tool shell, jadi perlakuannya pun sama:

- **Sakelar kemampuan.** `Pengaturan › Izin › Izinkan server MCP`, mati secara bawaan. Saat mati,
  tidak ada server yang dijalankan dan tidak ada tool MCP yang dijelaskan ke model, sekalipun ada
  server yang sudah dikonfigurasi.
- **Sakelar per server.** Menambahkan server dari galeri menuliskan satu baris perintah ke
  `config.json`. Itu tidak menjalankan apa pun. Mengaktifkan adalah tindakan kedua yang disengaja.
- **Baris perintahnya selalu ditampilkan**, jadi apa yang akan dijalankan tidak pernah misterius.
- **Tool-nya ditandai sebagai menulis**, bukan aman — AutoWork tidak bisa melihat apa yang
  dilakukan tool eksternal, dan mengklaim sebaliknya adalah jaminan yang tak bisa ia penuhi.

Kunci yang dibutuhkan server MCP masuk ke penyimpanan rahasia terenkripsi seperti kunci lainnya,
dan `config.json` hanya menyimpan referensinya.

Katalog bawaan memuat server yang paketnya sudah diperiksa ke registry-nya dan tidak usang. Itu
titik awal, bukan pengesahan: Anda tetap menjalankan kode orang lain.

## Skill: instruksi, dan kadang kode

Skill terpasang sebagai satu folder: `SKILL.md` plus apa pun yang menyertainya. Untuk kebanyakan
skill itu berupa dokumen rujukan dan template. Untuk sebagian — `anthropics/skills/pdf` dan `docx`
di antaranya — termasuk juga skrip Python.

**Instruksinya pasif.** Markdown yang dibaca model tidak bisa memberi kemampuan baru atau
memanggil tool yang dilarang kebijakan izin. Hal terburuk yang bisa dilakukan skill yang ditulis
buruk adalah memberi model saran buruk, tetap dibatasi sandbox yang sama.

**Skripnya tidak pasif.** Menjalankan satu skrip berarti menjalankan kode dari repositori orang
lain dengan hak akses penuh Anda, dan `PathGuard` tidak bisa melihat ke dalam proses lain — sama
seperti tool shell dan server MCP. Jadi digerbangi dengan cara yang sama:

- **`Pengaturan › Izin › Izinkan menjalankan skrip bawaan skill`**, mati secara bawaan. Saat mati,
  `skill_run` tidak dijelaskan ke model sama sekali.
- **Sakelarnya sendiri**, sengaja terpisah dari shell. Menyetujui perintah Anda sendiri bukan hal
  yang sama dengan menyetujui skrip orang asing.
- **Persetujuan sebelum tiap eksekusi**, menyala secara bawaan, menampilkan interpreter, path
  skrip hasil resolusi, dan setiap argumennya — perintah yang sebenarnya, bukan ringkasannya.
- **Hanya interpreter yang dikenal.** `.py`, `.js`, `.mjs`, `.sh`, `.ps1`. Selain itu ditolak
  dengan menyebut nama, bukan diserahkan ke shell untuk ditebak.
- **Argumen dikirim sebagai daftar**, tidak pernah lewat shell, sehingga nama berkas berisi spasi
  atau tanda kutip tetap menjadi argumen dan bukan injeksi. Itu penting karena argumennya berasal
  dari model.

**Dependensi dipasang, dan itu risiko tersendiri.** Kebanyakan skill yang dipublikasikan tidak
mendeklarasikan kebutuhannya — dua dari delapan belas di `anthropics/skills` membawa
`requirements.txt`, sisanya menyebut library-nya hanya dalam prosa dan di baris import skripnya.
Jadi AutoWork membaca keduanya:

- **`requirements.txt` dipasang apa adanya.**
- **Import diterjemahkan lewat tabel tetap** — `fitz` adalah PyMuPDF, `PIL` adalah Pillow, `cv2`
  adalah opencv-python. Import yang tidak ada di tabel **dilaporkan, tidak pernah ditebak**.
  Mengarang nama paket yang terlihat masuk akal justru mekanisme typosquatting, jadi tabel itulah
  satu-satunya sumber nama, dan library yang hilang lebih baik daripada library yang salah.
- **Daftar paketnya ditampilkan di kartu persetujuan** sebelum apa pun diunduh. Daftar itulah
  bagian yang layak dibaca: `pip install` menjalankan `setup.py` milik paketnya, jadi memasang
  itu sendiri sudah eksekusi kode — dan terjadi *sebelum* skrip yang Anda setujui.
- **Semuanya masuk ke virtual environment di dalam folder skill.** Python Anda sendiri tidak
  pernah diubah, dan menghapus skill ikut menghapus library-nya.

Perlu jelas soal apa yang tidak diselesaikan ini: `requirements.txt` di repositori sebuah skill
ditulis oleh siapa pun yang menulis skill itu. Memeriksa daftar itu saat kartu muncul adalah
pengamannya, dan itu pengaman yang nyata — tapi satu-satunya. Sebagian paket juga butuh program
yang tidak bisa dipasang pip (`pdf2image` butuh poppler, `pytesseract` butuh tesseract); itu
disebutkan di muka, bukan muncul belakangan sebagai traceback yang membingungkan.

**Semuanya tetap di dalam folder skill.** Baik `skill_file` maupun `skill_run` menyelesaikan path
yang diberikan lalu memastikan hasilnya berada di dalam skill tersebut sebelum melakukan apa pun —
berkas bundel bernama `../../../etc/passwd` dibuang saat pemasangan, dan path skrip yang memanjat
keluar ditolak saat dipanggil.

Skill berada di direktori data milik AutoWork sendiri, yang dilindungi `PathGuard`, jadi agen
tidak bisa menulis ulang instruksinya sendiri atau menaruh skrip baru di sana lewat tool file.
Memasang dan menghapus tetap tindakan yang disengaja di galeri Skill.

Repositori asal tiap skill ditampilkan di sebelahnya, karena kode siapa yang akan Anda jalankan
adalah bagian yang layak diketahui.

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
