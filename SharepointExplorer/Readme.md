# SharePoint Explorer

A modern File Explorer application for SharePoint Online, built with C# and Avalonia UI.

## Features
- **Login**: Connect securely to your SharePoint Site using credentials or **Modern Authentication**.
- **Custom Auth**: Ability to specify custom Client ID and Redirect URI for Modern Auth.
- **Navigation**: Browse Subsites, Document Libraries, and Folders via a Tree View.
- **File Management**: View files and folders in a tabular grid with metadata.
- **Operations**:
  - Create New Folders.
  - Create New Documents (Word, Excel, PowerPoint, Text).
  - Download Files (Single or Multiple as Zip).
  - Delete and Rename items.
  - Navigate recursively.

## Requirements
- .NET 8.0 SDK
- Windows OS (recommended for CSOM Authentication compatibility)
- SharePoint Online Account.

## How to Run
1. Open the project folder.
2. Run command: `dotnet run`
3. Enter your Site URL (e.g., `https://yourtenant.sharepoint.com/sites/mysite`).
4. Choose **Credentials** tab for legacy auth or **Modern Auth** tab for OAuth.

## Troubleshooting

### Error: AADSTS700016 (Application not installed in tenant)
This error means the default **PnP Management Shell** application ID (`31359c7f-bd7e-475c-86db-fdb8c937548e`) has not been registered or consented to in your tenant.

**How to Fix:**
1.  **Option A (Register PnP Default)**:
    Ask your admin to run `Register-PnPManagementShellAccess` in PowerShell.
2.  **Option B (Use Custom App Registration - Recommended)**:
    1.  Go to **Azure Portal > App Registrations**.
    2.  Create a new App (Select "Mobile & Desktop").
    3.  Add a Redirect URI: `http://localhost`.
    4.  In **API Permissions**, add `SharePoint > AllSites.Write` (Delegated) and grant admin consent.
    5.  Copy the **Application (client) ID**.
    6.  Paste that ID into the **Client ID** box in the **Modern Auth** tab of this application.

---

## Bahasa Indonesia

# SharePoint Explorer

Aplikasi File Explorer modern untuk SharePoint Online, dibuat menggunakan C# dan Avalonia UI.

## Fitur
- **Login**: Masuk dengan aman ke Situs SharePoint Anda menggunakan kredensial atau **Modern Authentication**.
- **Custom Auth**: Kemampuan untuk menentukan Client ID dan Redirect URI kustom untuk Modern Auth.
- **Navigasi**: Jelajahi Subsitus, Pustaka Dokumen, dan Folder melalui Tampilan Pohon.
- **Manajemen File**: Lihat file dan folder dalam grid tabel dengan metadata.
- **Operasi**:
  - Buat Folder Baru.
  - Buat Dokumen Baru (Word, Excel, PowerPoint, Teks).
  - Unduh File (Tunggal atau Banyak sebagai Zip).
  - Hapus dan Ganti Nama item.
  - Navigasi secara rekursif.

## Persyaratan
- .NET 8.0 SDK
- Windows OS (disarankan untuk kompatibilitas Autentikasi CSOM)
- Akun SharePoint Online.

## Cara Menjalankan
1. Buka folder proyek.
2. Jalankan perintah: `dotnet run`
3. Masukkan URL Situs Anda (contoh: `https://yourtenant.sharepoint.com/sites/mysite`).
4. Pilih tab **Credentials** untuk auth lama atau tab **Modern Auth** untuk OAuth.

## Pemecahan Masalah (Troubleshooting)

### Error: AADSTS700016 (Application not installed in tenant)
Error ini menunjukkan bahwa ID aplikasi default **PnP Management Shell** (`31359c7f-bd7e-475c-86db-fdb8c937548e`) belum terdaftar atau disetujui di tenant Anda.

**Cara Memperbaiki:**
1.  **Opsi A (Daftarkan PnP Default)**:
    Minta admin Anda menjalankan `Register-PnPManagementShellAccess` di PowerShell.
2.  **Opsi B (Gunakan Registrasi Aplikasi Kustom - Disarankan)**:
    1.  Buka **Azure Portal > App Registrations**.
    2.  Buat Aplikasi baru (Pilih "Mobile & Desktop").
    3.  Tambahkan Redirect URI: `http://localhost`.
    4.  Di **API Permissions**, tambahkan `SharePoint > AllSites.Write` (Delegated) dan berikan Admin Consent.
    5.  Salin **Application (client) ID**.
    6.  Tempel ID tersebut ke kotak **Client ID** di tab **Modern Auth** aplikasi ini.
