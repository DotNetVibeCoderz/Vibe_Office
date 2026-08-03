# Integrasi

*AutoWork — Gravicode Studios, dipimpin oleh Kang Fadhil*

Hubungkan layanan yang sudah Anda pakai sehari-hari. Setiap konektor menambahkan tool yang dapat
dipanggil agen.

Aktifkan salah satu di **Pengaturan › Integrasi**, isi kredensialnya, lalu tekan **Uji koneksi**
sebelum mengandalkannya. Konektor yang nonaktif atau kredensialnya belum lengkap tidak
menyumbang tool sama sekali — alih-alih menyumbang tool yang gagal di setiap panggilan.

Kredensial mengikuti aturan yang sama dengan kunci API model: rahasia masuk ke penyimpanan
terenkripsi atau ke `env:VARIABEL`, tidak pernah ke `config.json`.

> Akses jaringan harus aktif (**Pengaturan › Izin**) atau tidak ada konektor yang dimuat.

![Pengaturan › Integrasi](../images/integrations.png)

---

## GitHub

**Kredensial.** Personal access token dari
[github.com/settings/tokens](https://github.com/settings/tokens). Token fine-grained lebih
disarankan; ia perlu akses baca repositori, ditambah tulis issue bila Anda ingin memakai
`github_create_issue`.

**Opsional.** Repositori bawaan dalam format `pemilik/nama`, dipakai bila tool dipanggil tanpa
menyebut repositori.

**Tools**

| Tool | Fungsi |
|---|---|
| `github_search_repos` | Mencari repositori |
| `github_list_issues` | Menampilkan issue dalam sebuah repositori |
| `github_read_file` | Membaca file pada branch, tag, atau commit tertentu |
| `github_create_issue` | Membuka issue baru |

---

## Google Drive dan Gmail

Keduanya berada dalam satu konektor karena berbagi OAuth client dan refresh token yang sama.

**Mengapa Anda menyediakan OAuth client sendiri.** AutoWork adalah aplikasi desktop dan tidak
dapat menjaga kerahasiaan sebuah client secret, jadi ia tidak berpura-pura bisa. Anda membuat
OAuth client di proyek Google Cloud Anda sendiri lalu menyelesaikan proses persetujuan sekali.
Kredensialnya kemudian terikat pada proyek Anda, bukan proyek bersama — pertukaran yang tepat
untuk alat yang bisa membaca surel Anda.

**Penyiapan**

1. Di [Google Cloud Console](https://console.cloud.google.com/apis/credentials), buat proyek.
2. Aktifkan **Google Drive API** dan **Gmail API**.
3. Atur layar persetujuan OAuth. Selama masih berstatus Testing, tambahkan diri Anda sebagai
   test user.
4. Buat kredensial → **OAuth client ID** → **Desktop app**. Catat client id dan secret-nya.
5. Dapatkan refresh token sekali, dengan scope berikut:
   ```
   https://www.googleapis.com/auth/drive.readonly
   https://www.googleapis.com/auth/gmail.readonly
   ```
   [OAuth 2.0 Playground](https://developers.google.com/oauthplayground) adalah jalur tercepat:
   buka pengaturannya, centang *Use your own OAuth credentials*, tempel client id dan secret
   Anda, otorisasi kedua scope tersebut, lalu tukarkan kodenya dengan token dan salin **refresh
   token**-nya.
6. Tempel client id, client secret, dan refresh token ke AutoWork.

Access token ditukar sesuai kebutuhan dan hanya disimpan di memori, tidak pernah ditulis ke disk.

**Tools**

| Tool | Fungsi |
|---|---|
| `drive_search` | Mencari di Drive berdasarkan nama dan isi |
| `drive_download` | Mengunduh file ke folder yang diizinkan. Format asli Google diekspor ke padanan Office. |
| `gmail_search` | Mencari pesan dengan sintaks kueri Gmail (`from:`, `has:attachment`, `newer_than:30d`) |
| `gmail_read` | Membaca teks lengkap sebuah pesan |

`drive_download` menulis ke disk Anda, sehingga ia melewati sandbox persis seperti operasi file
lainnya — tujuannya harus berada di dalam folder baca/tulis yang diizinkan.

Scope di atas bersifat hanya-baca. AutoWork tidak meminta akses tulis ke Drive maupun Gmail.

---

## Notion

**Kredensial.** Token integrasi internal dari
[notion.so/my-integrations](https://www.notion.so/my-integrations).

**Penting.** Model izin Notion berbasis berbagi. Token Anda tidak melihat *apa pun* sampai Anda
membagikan halaman ke integrasi tersebut: buka halaman → **Connections** → tambahkan AutoWork.
Uji koneksi menyatakan hal ini secara eksplisit bila berhasil terhubung tetapi tidak menemukan
halaman, karena inilah yang paling sering membingungkan orang.

**Tools**

| Tool | Fungsi |
|---|---|
| `notion_search` | Mencari halaman dan database yang dibagikan |
| `notion_read_page` | Membaca isi teks sebuah halaman |
| `notion_append` | Menambahkan paragraf ke sebuah halaman |

---

## Asana

**Kredensial.** Personal access token dari **My Settings › Apps › Manage developer apps**.

**Opsional.** ID workspace bawaan, terlihat di URL saat membuka workspace Anda.

**Tools**

| Tool | Fungsi |
|---|---|
| `asana_list_projects` | Menampilkan daftar proyek |
| `asana_list_tasks` | Menampilkan tugas dalam sebuah proyek |
| `asana_create_task` | Membuat tugas, opsional dengan catatan dan tenggat |

---

## PayPal

Untuk kebutuhan pelacakan pengeluaran.

**Kredensial.** Client id dan secret dari aplikasi REST di
[developer dashboard](https://developer.paypal.com/dashboard/applications). Setel environment ke
`live` atau `sandbox`.

**Hanya-baca secara desain.** Tidak ada tool pembayaran, pengembalian dana, atau payout yang
diekspos, terlepas dari apa yang secara teknis diizinkan kredensialnya. LLM di dalam alur kerja
tidak berkepentingan memiliki kemampuan memindahkan uang.

**Tools**

| Tool | Fungsi |
|---|---|
| `paypal_list_transactions` | Transaksi dalam rentang tanggal (PayPal membatasi 31 hari per kueri) |
| `paypal_balances` | Saldo terkini |

---

## Pemecahan masalah

**"The credentials were rejected."** Token salah, kedaluwarsa, atau dicabut. Terbitkan ulang.

**"Access denied — the token is valid but lacks the required scope."** Token terautentikasi
tetapi kurang izin. Untuk GitHub, periksa akses repositori; untuk Google, periksa scope yang
Anda otorisasi.

**Notion terhubung tapi tidak menemukan apa pun.** Halaman belum dibagikan ke integrasi. Lihat
di atas.

**Google berhenti bekerja setelah beberapa waktu.** Refresh token untuk aplikasi yang masih
berstatus *Testing* kedaluwarsa setelah tujuh hari. Publikasikan layar persetujuannya, atau
terbitkan ulang tokennya.

**Tidak ada tool konektor yang muncul.** Pastikan akses jaringan aktif di Pengaturan › Izin,
integrasinya sudah dinyalakan, dan kolom wajibnya sudah terisi.

## Menambahkan konektor sendiri

Implementasikan `IIntegration` (atau turunkan dari `RestConnector` untuk plumbing HTTP-nya), lalu
daftarkan di `IntegrationRegistry`. Sebuah konektor mendeklarasikan kolomnya, menguji koneksinya
sendiri, dan menghasilkan `ToolDescriptor`. Lihat `GitHubIntegration` sebagai contoh lengkap
terkecil.

Pendaftaran bersifat statis alih-alih berbasis plugin secara sengaja: memuat assembly pihak
ketiga ke dalam proses yang menyimpan kunci API pengguna bukan pertukaran yang layak diambil.
Lihat [PLAN.md](../../PLAN.md).
