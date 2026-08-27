# Galeri Tangkapan Layar

Seluruh halaman CUAN dalam tema terang dan gelap. Diambil dari data contoh yang
dibuat `DataSeeder` — 12 bulan transaksi bergulir yang berakhir di bulan berjalan.

Untuk mengambil ulang setelah tampilan berubah:

```bash
dotnet run                        # jendela terminal pertama
node docs/capture-screenshots.js  # jendela terminal kedua
```

Pilihan yang tersedia:

```bash
node docs/capture-screenshots.js --only dashboard   # satu halaman saja
node docs/capture-screenshots.js --base http://localhost:5000
```

Skrip memerlukan [Playwright](https://playwright.dev/) (`npm i playwright`)
dan aplikasi yang sedang berjalan.

---

## Ringkasan

### Dasbor

Empat angka pokok, persamaan akuntansi dalam satu baris, jurnal terakhir, dan
pengingat masa pajak berjalan lengkap dengan batas lapornya.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/dashboard.png) | ![](screenshots/dashboard-dark.png) |

### Dasbor eksekutif

| Terang | Gelap |
|--------|-------|
| ![](screenshots/executive.png) | ![](screenshots/executive-dark.png) |

---

## Transaksi

### Jurnal umum

Kolom debit dan kredit memakai warna tintanya masing-masing. Bilah keseimbangan
di formulir berubah menjadi garis ganda hijau begitu debit sama dengan kredit.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/journal.png) | ![](screenshots/journal-dark.png) |

### Penjualan

PPN dihitung dari tarif di Pengaturan. Menyimpan faktur akan mengurangi stok,
membentuk jurnal piutang–pendapatan–PPN keluaran, dan mencatat harga pokok.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/sales.png) | ![](screenshots/sales-dark.png) |

### Pembelian

| Terang | Gelap |
|--------|-------|
| ![](screenshots/purchases.png) | ![](screenshots/purchases-dark.png) |

### Kas

| Terang | Gelap |
|--------|-------|
| ![](screenshots/cash.png) | ![](screenshots/cash-dark.png) |

### Bank

| Terang | Gelap |
|--------|-------|
| ![](screenshots/bank.png) | ![](screenshots/bank-dark.png) |

### Giro

| Terang | Gelap |
|--------|-------|
| ![](screenshots/giro.png) | ![](screenshots/giro-dark.png) |

### Mutasi stok

| Terang | Gelap |
|--------|-------|
| ![](screenshots/stock.png) | ![](screenshots/stock-dark.png) |

---

## Pajak

### Faktur pajak

| Terang | Gelap |
|--------|-------|
| ![](screenshots/tax-faktur.png) | ![](screenshots/tax-faktur-dark.png) |

### SPT Masa PPN

| Terang | Gelap |
|--------|-------|
| ![](screenshots/tax-spt.png) | ![](screenshots/tax-spt-dark.png) |

### Bukti potong PPh

| Terang | Gelap |
|--------|-------|
| ![](screenshots/tax-withholding.png) | ![](screenshots/tax-withholding-dark.png) |

### Integrasi DJP

Daftar periksa kesiapan, penjelasan dua jalur pelaporan, dan riwayat setiap
percakapan dengan layanan DJP.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/tax-djp.png) | ![](screenshots/tax-djp-dark.png) |

---

## Laporan

### Buku besar

Mutasi satu akun dengan saldo berjalan, dibuka dari saldo awal periode dan
ditutup garis ganda di saldo akhir.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/ledger.png) | ![](screenshots/ledger-dark.png) |

### Neraca saldo

| Terang | Gelap |
|--------|-------|
| ![](screenshots/trial-balance.png) | ![](screenshots/trial-balance-dark.png) |

### Laba rugi

| Terang | Gelap |
|--------|-------|
| ![](screenshots/profit-loss.png) | ![](screenshots/profit-loss-dark.png) |

### Neraca

| Terang | Gelap |
|--------|-------|
| ![](screenshots/balance-sheet.png) | ![](screenshots/balance-sheet-dark.png) |

### Arus kas

Metode langsung: setiap jurnal yang menyentuh akun kas atau bank dikelompokkan
menurut jenis akun lawannya menjadi arus operasi, investasi, dan pendanaan.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/cash-flow.png) | ![](screenshots/cash-flow-dark.png) |

### Laporan pajak

| Terang | Gelap |
|--------|-------|
| ![](screenshots/tax-report.png) | ![](screenshots/tax-report-dark.png) |

### Laporan penjualan

| Terang | Gelap |
|--------|-------|
| ![](screenshots/sales-report.png) | ![](screenshots/sales-report-dark.png) |

### Laporan stok

| Terang | Gelap |
|--------|-------|
| ![](screenshots/stock-report.png) | ![](screenshots/stock-report-dark.png) |

---

## Data induk

### Daftar akun

| Terang | Gelap |
|--------|-------|
| ![](screenshots/coa.png) | ![](screenshots/coa-dark.png) |

### Barang

| Terang | Gelap |
|--------|-------|
| ![](screenshots/items.png) | ![](screenshots/items-dark.png) |

### Pelanggan

| Terang | Gelap |
|--------|-------|
| ![](screenshots/customers.png) | ![](screenshots/customers-dark.png) |

### Impor Excel

Tersedia di seluruh halaman data induk, dengan tiga tahap berurutan.

**Tahap 1 — unduh template.** Tombol "Template .xlsx" ada di langkah pertama,
sebelum berkas dipilih. Panel "Kolom yang dibaca" merinci setiap kolom beserta
contoh dan keterangannya tanpa perlu membuka berkasnya dulu.

| Tahap awal | Rincian kolom |
|------------|---------------|
| ![](screenshots/import-template.png) | ![](screenshots/import-columns.png) |

**Tahap 2 — pratinjau.** Setelah berkas diunggah, tampilan langkah 1 dan 2
digantikan hasil pemeriksaan: baris yang lolos dipisahkan dari yang bermasalah
beserta alasannya. Belum ada yang tersimpan pada tahap ini.

**Tahap 3 — selesai.** Ringkasan berapa yang ditambahkan, diperbarui, dan dilewati.

| Pratinjau | Selesai |
|-----------|---------|
| ![](screenshots/import-preview.png) | ![](screenshots/import-done.png) |

Alur ini diuji otomatis oleh `docs/test-import.js` — mengunduh template,
mengisinya dengan dua baris benar dan satu baris salah, mengunggahnya, lalu
memastikan yang benar masuk dan yang salah dilewati:

```bash
dotnet run                # jendela terminal pertama
node docs/test-import.js  # jendela terminal kedua
```

---

## Administrasi

### Pengaturan

95 parameter dalam sepuluh kelompok. Perubahan ditandai sebelum disimpan dan
berlaku seketika sesudahnya.

| Terang | Gelap |
|--------|-------|
| ![](screenshots/settings.png) | ![](screenshots/settings-dark.png) |

### Pengguna & peran

| Terang | Gelap |
|--------|-------|
| ![](screenshots/users.png) | ![](screenshots/users-dark.png) |

### Jejak audit

| Terang | Gelap |
|--------|-------|
| ![](screenshots/audit.png) | ![](screenshots/audit-dark.png) |

---

## Masuk

| Terang | Gelap |
|--------|-------|
| ![](screenshots/login.png) | ![](screenshots/login-dark.png) |

---

## Catatan desain

Bahasa visual CUAN diambil dari buku besar akuntansi:

- **Kertas greenbar.** Baris tabel berselang-seling dengan semburat hijau pucat,
  seperti kertas buku besar bergaris.
- **Tinta dua warna.** Debit biru, kredit merah — dipakai konsisten di jurnal,
  buku besar, neraca saldo, sampai rekap pajak.
- **Garis ganda akuntan.** Total akhir selalu ditutup garis ganda, konvensi yang
  sudah dipakai jauh sebelum ada komputer. Garis yang sama dipakai sebagai
  penanda status di bilah keseimbangan: putus-putus merah saat belum seimbang,
  garis ganda hijau saat sudah.
- **Angka bermuka monospace.** Semua nilai uang memakai IBM Plex Mono supaya
  digitnya lurus ke bawah dalam kolom, seperti pita mesin hitung. Teks lain
  memakai Archivo.
