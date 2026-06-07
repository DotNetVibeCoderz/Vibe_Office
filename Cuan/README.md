# 💰 CUAN - Sistem Akuntansi Berbasis Web

**CUAN** adalah sistem akuntansi modern berbasis **Blazor Server** yang menggabungkan fitur lengkap ala Accurate dengan kesederhanaan ala Zahir. Cocok untuk UMKM kecil hingga bisnis menengah/kompleks.

---

## 🚀 Fitur Utama

| Fitur | Deskripsi |
|-------|-----------|
| 📒 **Chart of Accounts** | COA bertingkat dengan saldo real-time |
| 📦 **Manajemen Barang** | Multi satuan, multi kategori, kontrol stok min/max |
| 👥 **Customer & Supplier** | Manajemen piutang/hutang, credit limit, NPWP |
| 🏗️ **Multi Gudang & Cabang** | Stok per lokasi, mutasi antar gudang |
| 💱 **Multi Mata Uang** | Kurs otomatis, transaksi internasional |
| 🧾 **Integrasi Pajak** | PPN, PPh 21/23/25, perhitungan otomatis |
| 📝 **Jurnal Umum** | Double-entry accounting, auto-posting |
| 🛒 **Penjualan & Pembelian** | Faktur, PO, tracking pembayaran |
| 💵 **Kas & Bank** | Kas masuk/keluar, bank in/out, transfer |
| 📜 **Giro** | Cek/giro masuk & keluar, tracking status |
| 📊 **Dashboard** | Stat cards, tren, info stok rendah |
| 📈 **Laporan Keuangan** | Laba Rugi, Neraca, Arus Kas, Pajak |
| 🔑 **API REST** | 14+ endpoint dengan Swagger + ApiKey auth |
| 👤 **Role-Based Access** | 7 role: Admin, Akuntan, Kasir, StafGudang, Sales, Purchasing, Viewer |
| 🌓 **Dark/Light Theme** | UI ala Claude AI, modern dan clean |
| 📋 **Audit Trail** | Catatan perubahan transaksi |
| ⚙️ **System Settings** | Konfigurasi dari UI & appsettings |

---

## 🛠️ Tech Stack

- **.NET 10** (Blazor Server, ASP.NET Core)
- **Entity Framework Core** (SQLite)
- **ASP.NET Identity** (Auth & Role)
- **Swagger** (API Documentation)
- **SignalR** (Real-time notifications)
- **ClosedXML** (Excel export)
- **CsvHelper** (CSV export)
- **Blazor-ApexCharts** (Dashboard charts)

---

## 📦 Instalasi & Menjalankan

### Prasyarat
- .NET 10 SDK
- Visual Studio 2022+ / VS Code / Rider

### Langkah-langkah
```bash
# Clone / buka folder project
cd Cuan

# Restore packages
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

Buka browser ke `https://localhost:5001` atau `http://localhost:5000`

Swagger UI: `https://localhost:5001/swagger`

---

## 👤 Sample Accounts

| Email | Password | Role |
|-------|----------|------|
| admin@cuan.id | Cuan@123 | Admin (Full Access) |
| akuntan@cuan.id | Cuan@123 | Akuntan |
| kasir@cuan.id | Cuan@123 | Kasir |
| gudang@cuan.id | Cuan@123 | Staf Gudang |
| sales@cuan.id | Cuan@123 | Sales |
| purchasing@cuan.id | Cuan@123 | Purchasing |
| viewer@cuan.id | Cuan@123 | Viewer (Read-only) |

---

## 🔑 API Access

Default API Key: `cu4n-4p1-k3y-2024-d3f4ult`

Gunakan header `X-Api-Key` untuk mengakses REST API.

### Contoh:
```bash
curl -H "X-Api-Key: cu4n-4p1-k3y-2024-d3f4ult" https://localhost:5001/api/v1/items
```

### Endpoints:
- `GET /api/v1/coa` - Chart of Accounts
- `GET /api/v1/items` - Items (with pagination)
- `GET /api/v1/customers` - Customers
- `GET /api/v1/suppliers` - Suppliers
- `GET /api/v1/journals` - Journal Entries
- `GET /api/v1/sales-invoices` - Sales Invoices
- `GET /api/v1/purchase-invoices` - Purchase Invoices
- `GET /api/v1/cash-transactions` - Cash Transactions
- `GET /api/v1/bank-transactions` - Bank Transactions
- `GET /api/v1/stock-movements` - Stock Movements
- `GET /api/v1/dashboard/summary` - Dashboard Summary
- `GET /api/v1/export/coa/csv` - Export COA to CSV
- `GET /api/v1/export/items/csv` - Export Items to CSV
- `POST /api/v1/journals` - Create Journal Entry

---

## 📁 Struktur Proyek

```
Cuan/
├── Models/              # Domain Entities (15+ models)
│   ├── ChartOfAccount.cs
│   ├── Item.cs
│   ├── CustomerSupplier.cs
│   ├── MasterData.cs
│   ├── JournalEntry.cs
│   ├── SalesPurchaseInvoice.cs
│   ├── Transactions.cs
│   └── SystemModels.cs
├── Data/                # EF Core DbContext & Seeder
│   ├── AppDbContext.cs
│   └── DataSeeder.cs
├── Api/                 # REST API Controllers
│   └── ApiController.cs
├── Components/
│   ├── Layout/          # MainLayout (Claude AI Style)
│   ├── Pages/
│   │   ├── Master/      # COA, Items, Customers, etc.
│   │   ├── Transactions/# Journal, Sales, Purchases, etc.
│   │   ├── Reports/     # Profit/Loss, Balance Sheet, etc.
│   │   └── Admin/       # Users, Settings, Audit, API Keys
│   └── _Imports.razor
├── wwwroot/             # CSS, JS, assets
│   └── app.css          # Claude-style UI theme
├── Program.cs           # App configuration
├── appsettings.json     # Connection strings & settings
└── PLAN.md              # Development plan checklist
```

---

## ⚙️ Konfigurasi

Semua pengaturan dapat diubah melalui:
1. **File `appsettings.json`** - Connection string, company info, feature flags
2. **Halaman UI** (`/admin/settings`) - Konfigurasi real-time dari web

---

## 🏗️ Dibuat Oleh

**Gravicode Studios** — dipimpin oleh kang Fadhil  
https://studios.gravicode.com

---

*CUAN - Akuntansi Jadi Gampang! 💰*
