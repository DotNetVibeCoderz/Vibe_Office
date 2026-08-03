# Instalasi

*AutoWork — Gravicode Studios, dipimpin oleh Kang Fadhil*

## Kebutuhan

| | Minimum |
|---|---|
| Sistem operasi | Windows 10 1809+, macOS 12+, atau desktop Linux dengan X11 atau Wayland |
| .NET | [.NET 10 SDK](https://dotnet.microsoft.com/download) |
| Disk | ±250 MB untuk aplikasi, ditambah ukuran basis pengetahuan Anda |
| RAM | 4 GB. Menjalankan model lokal dengan Ollama butuh jauh lebih besar. |
| Jaringan | Hanya untuk penyedia hosted dan integrasi. Dengan Ollama, AutoWork berjalan sepenuhnya offline. |

Periksa SDK Anda:

```bash
dotnet --list-sdks
```

Harus ada baris yang diawali `10.`.

## Windows

```powershell
git clone https://github.com/gravicode/autowork.git
cd autowork\install
.\install.ps1 -Desktop
```

Opsi:

| Flag | Efek |
|---|---|
| `-Desktop` | Sekaligus membuat pintasan di Desktop |
| `-InstallDir <path>` | Pasang di lokasi selain `%LOCALAPPDATA%\Programs\AutoWork` |
| `-Runtime win-arm64` | Build untuk ARM64 |
| `-SkipShortcuts` | Tidak membuat pintasan |

Jika PowerShell memblokir skrip:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

Perintah itu hanya melonggarkan kebijakan untuk sesi ini saja.

## macOS dan Linux

```bash
git clone https://github.com/gravicode/autowork.git
cd autowork/install
chmod +x install.sh
./install.sh
```

Opsi:

| Flag | Efek |
|---|---|
| `--prefix <dir>` | Pasang di lokasi selain `~/.local/share/autowork` |
| `--bin-dir <dir>` | Letakkan launcher di lokasi selain `~/.local/bin` |
| `--skip-launcher` | Tidak membuat launcher atau desktop entry |

**Tambahan untuk Linux.** Pasang `fontconfig` bila teks tampil sebagai kotak. Untuk penangkapan
layar, pasang salah satu dari `grim` (Wayland), `gnome-screenshot`, `spectacle`, `imagemagick`,
atau `scrot`. Untuk kendali input, pasang `xdotool` (X11).

**Tambahan untuk macOS.** Penangkapan layar dan kendali input keduanya memerlukan izin di
**System Settings › Privacy & Security**: masing-masing *Screen Recording* dan *Accessibility*.
Untuk kendali input, pasang juga `cliclick` (`brew install cliclick`).

## Menjalankan tanpa memasang

```bash
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

## Lokasi berkas

| | Windows | macOS | Linux |
|---|---|---|---|
| Program | `%LOCALAPPDATA%\Programs\AutoWork` | `~/.local/share/autowork` | `~/.local/share/autowork` |
| Data Anda | `%APPDATA%\AutoWork` | `~/Library/Application Support/AutoWork` | `~/.config/AutoWork` |

Isi folder data:

```
config.json        Pengaturan. Tidak berisi kunci API — hanya referensi ke kunci.
secrets.json       Kunci API terenkripsi, beserta secrets.json.key.
logs/              Log tindakan berformat JSON Lines, dirotasi pada 8 MB.
knowledge/         Satu file JSON per basis pengetahuan.
screenshots/       Tangkapan layar dari subsistem Eyes.
recycle/           File terhapus lunak, masih bisa dipulihkan.
```

Setel `AUTOWORK_HOME` untuk memindahkan semuanya — berguna untuk instalasi portabel di flashdisk.

**Folder kerja Anda terpisah**, di `Documents\AutoWork` (`~/Documents/AutoWork` di macOS dan
Linux). Itulah yang diberikan kebijakan izin awal dan tempat agen menaruh file bila pekerjaan
tidak menyebut path. Letaknya sengaja di luar folder data: semua yang ada di folder data adalah
lokasi internal AutoWork yang ditolak `PathGuard`, dan proses uninstall tidak boleh ikut membawa
hasil kerja Anda. Pindahkan dengan `AUTOWORK_WORKSPACE`.

## Memperbarui

Jalankan ulang installer. Ia akan menutup aplikasi yang sedang berjalan, mengganti berkas
program, dan membiarkan folder data Anda tidak tersentuh.

## Menghapus instalasi

```powershell
.\install\uninstall.ps1              # data tetap disimpan
.\install\uninstall.ps1 -PurgeData   # data ikut dihapus
```

```bash
./install/uninstall.sh
./install/uninstall.sh --purge-data
```

Kunci API Anda berada di folder data, jadi perilaku bawaannya mempertahankannya. Gunakan flag
penghapusan hanya bila memang itu yang Anda maksud.

## Setelah memasang

1. **Pengaturan › Model** — tambahkan penyedia dan tempel kunci. Lihat
   [konfigurasi.md](konfigurasi.md).
2. **Pengaturan › Izin** — beri satu folder. Sebelum itu AutoWork tidak bisa menjangkau apa pun;
   ini disengaja, dan dijelaskan di [keamanan.md](keamanan.md).
3. **Kerja** — jelaskan pekerjaannya.

Jika kunci vendor sudah tersedia di environment Anda, AutoWork mendeteksinya saat pertama
dijalankan dan langkah 1 sudah selesai dengan sendirinya.
