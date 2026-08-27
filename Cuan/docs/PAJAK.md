# Perpajakan & Integrasi DJP

Panduan alur perpajakan di CUAN: dari faktur penjualan sampai SPT Masa PPN
terlapor, beserta dua jalur pelaporan ke Direktorat Jenderal Pajak.

> **Sebelum dipakai melapor.** Tata letak berkas, tarif, dan alamat layanan DJP
> berubah dari waktu ke waktu. Cocokkan keluaran aplikasi dengan ketentuan
> terbaru yang berlaku untuk perusahaan Anda, dan konsultasikan dengan konsultan
> pajak Anda. CUAN menyiapkan berkas dan perhitungannya; tanggung jawab atas isi
> laporan tetap ada pada wajib pajak.

---

## Persiapan sekali di awal

Buka **Pengaturan** (`/admin/settings`) dan lengkapi tiga kelompok berikut.

### Perusahaan

| Parameter | Keterangan |
|-----------|------------|
| NPWP | Dipakai sebagai identitas pengirim di semua berkas |
| NITKU | Nomor Identitas Tempat Kegiatan Usaha — wajib pada Coretax |
| Berstatus PKP | Kalau dimatikan, PPN tidak dipungut dan faktur pajak dinonaktifkan |
| No. & tanggal pengukuhan PKP | Tercetak di dokumen |
| Penanda tangan & jabatan | Nama di kolom tanda tangan faktur pajak |

### Pajak

| Parameter | Bawaan | Keterangan |
|-----------|--------|------------|
| `PpnEnabled` | aktif | Saklar utama pemungutan PPN |
| `PpnRate` | 11 | Tarif PPN dalam persen |
| `PpnPriceInclusive` | nonaktif | Kalau aktif, PPN dihitung mundur dari harga jual |
| `PpnDppFactor` | 1 | Isi `0.9166667` bila memakai DPP nilai lain 11/12 |
| `Pph21Rate` / `Pph23Rate` / `Pph42Rate` | 5 / 2 / 10 | Tarif pemotongan PPh |
| `FakturTransactionCode` | 01 | Kode transaksi faktur pajak |
| `NsfpPrefix` | 000 | Kode cabang pada nomor seri |
| `NsfpRangeStart` / `NsfpRangeEnd` | — | Jatah NSFP dari DJP |
| `NsfpNext` | — | Nomor berikutnya, naik otomatis tiap penerbitan |

Isi rentang NSFP sesuai jatah yang Anda peroleh. Halaman Faktur Pajak
memperingatkan saat sisa jatah tinggal sedikit, dan menolak menerbitkan faktur
begitu jatahnya habis.

### Pemetaan akun

PPN keluaran dan PPN masukan harus menunjuk ke akun yang benar, karena ke sanalah
jurnal faktur mengalir:

| Parameter | Akun contoh |
|-----------|-------------|
| `AccountPpnKeluaran` | 2-1200 PPN Keluaran |
| `AccountPpnMasukan` | 1-1600 PPN Masukan |

---

## Alur bulanan

### 1 · Rekam transaksi seperti biasa

Faktur penjualan menghitung PPN dari tarif di Pengaturan, lalu membentuk jurnal:

```
Piutang Usaha            xxx
    Penjualan                    xxx
    PPN Keluaran                 xxx
Harga Pokok Penjualan    xxx
    Persediaan Barang            xxx
```

Faktur pembelian mencatat PPN masukan dengan cara yang sama:

```
Persediaan Barang        xxx
PPN Masukan              xxx
    Hutang Usaha                 xxx
```

### 2 · Terbitkan faktur pajak

Buka **Pajak → Faktur Pajak** (`/tax/faktur`), pilih masa pajaknya. Panel
"Menunggu diterbitkan" memuat setiap faktur penjualan ber-PPN yang belum punya
faktur pajak. Terbitkan satu per satu, atau seluruhnya sekaligus dengan tombol
di kanan atas.

Setiap penerbitan mengambil satu nomor dari jatah NSFP dan menyimpan DPP, PPN,
identitas pembeli, serta rujukan ke faktur penjualannya.

Faktur yang keliru bisa dibatalkan — nomor serinya tidak dipakai ulang, sesuai
ketentuan.

### 3 · Catat bukti potong PPh

**Pajak → Bukti Potong PPh** (`/tax/withholding`). Pilih jenisnya, dan tarifnya
terisi otomatis dari Pengaturan. Nilai PPh dihitung dari DPP dikali tarif, jadi
tidak ada peluang salah ketik.

### 4 · Susun SPT Masa

**Pajak → SPT Masa** (`/tax/spt`) menampilkan dua belas masa sekaligus: DPP dan
PPN keluaran, PPN masukan, posisi kurang atau lebih bayar, batas lapor, dan
status SPT-nya.

Tombol **Susun SPT masa berjalan** menghitung ulang dari transaksi, memperhitungkan
kompensasi kelebihan pajak masa sebelumnya, dan menyimpannya sebagai draf.

Masa yang sudah diterima tidak ditimpa. Untuk mengubahnya, buat **pembetulan** —
tersimpan sebagai revisi baru dengan nomor urut pembetulan tersendiri.

### 5 · Lapor

Dua jalur, keduanya siap dipakai.

---

## Jalur berkas — selalu tersedia

Tidak memerlukan kredensial apa pun. Dari halaman Faktur Pajak:

| Tombol | Keluaran | Tujuan |
|--------|----------|--------|
| **CSV e-Faktur** | Baris `FK` / `LT` / `OF` | Impor ke aplikasi e-Faktur desktop |
| **XML Coretax** | `TaxInvoiceBulk` | Unggah massal ke Coretax |

Dari halaman Bukti Potong PPh, tombol **Rekap CSV** menghasilkan rekap untuk
unggahan e-Bupot.

Format bawaan diatur di **Pengaturan → Integrasi DJP → Format ekspor faktur pajak**.

---

## Jalur host-to-host — perlu pendaftaran

Mengirim faktur pajak dan SPT langsung dari aplikasi ke layanan DJP.

### Yang perlu disiapkan

DJP hanya membuka akses ini untuk wajib pajak yang sudah mendaftar dan memperoleh:

1. **Client ID** dan **client secret**
2. **Sertifikat elektronik** (berkas `.p12`) beserta passphrase-nya
3. **NPWP penandatangan** yang terdaftar di akun DJP

Isi semuanya di **Pengaturan → Integrasi DJP**, lalu aktifkan saklar
**Aktifkan integrasi DJP**.

### Uji dulu di Sandbox

Halaman **Pajak → Integrasi DJP** (`/tax/djp`) menampilkan daftar periksa
kesiapan — sembilan butir, masing-masing bertanda centang atau silang, sehingga
terlihat persis apa yang masih kurang.

Tekan **Uji koneksi** untuk memastikan kredensialnya diterima. Jalankan di
lingkungan `Sandbox` lebih dulu; pindah ke `Production` hanya setelah
pengujiannya bersih.

### Riwayat percakapan

Setiap uji koneksi, pengiriman faktur, dan pelaporan SPT dicatat di tabel
`DjpSubmission` lengkap dengan payload yang dikirim, jawaban yang diterima, kode
HTTP, dan waktunya. Riwayat ini tampil di bagian bawah halaman Integrasi DJP —
berguna saat menelusuri penolakan.

### Kalau kredensial belum ada

Aplikasi menolak dengan pesan yang menyebut persis apa yang kurang, bukan galat
teknis. Pelaporan tetap bisa ditempuh lewat jalur berkas di atas.

---

## Batas waktu

Ditampilkan di halaman SPT Masa dan diingatkan di dasbor:

| Kewajiban | Batas |
|-----------|-------|
| Lapor SPT Masa PPN | Akhir bulan berikutnya |
| Setor PPN kurang bayar | Sebelum SPT dilaporkan |
| Setor PPh 21 / 23 / 4(2) | Tanggal 10 bulan berikutnya |
| Setor PPh 25 | Tanggal 15 bulan berikutnya |

Masa yang melewati batas dan belum dilaporkan ditandai merah di kolom
"Batas lapor".

---

## Bukti pelaporan

Setelah SPT diterima, simpan **NTTE** (Nomor Tanda Terima Elektronik) dan **NTPN**
(Nomor Transaksi Penerimaan Negara) atas setorannya. Keduanya tampil di panel
"Bukti pelaporan" pada halaman SPT Masa. Kalau pelaporan dilakukan lewat jalur
host-to-host, NTTE terisi otomatis dari jawaban DJP.

---

## Di mana kodenya

| Berkas | Isi |
|--------|-----|
| `Services/TaxService.cs` | Perhitungan DPP & PPN, ringkasan masa, penerbitan faktur pajak, penyusunan SPT |
| `Services/EfakturExportService.cs` | Berkas CSV e-Faktur, XML Coretax, rekap bukti potong |
| `Services/DjpClient.cs` | Klien host-to-host: token, kirim faktur, lapor SPT, pencatatan riwayat |
| `Services/DocumentNumberService.cs` | Penomoran NSFP dan nomor dokumen lain |
| `Models/TaxModels.cs` | `TaxInvoice`, `WithholdingTax`, `TaxReturn`, `DjpSubmission` |
| `Components/Pages/Tax/` | Keempat halaman pajak |

Seluruh tarif dan kredensial dibaca lewat `SettingsService`; tidak ada nilai
perpajakan yang ditanam di dalam kode.
