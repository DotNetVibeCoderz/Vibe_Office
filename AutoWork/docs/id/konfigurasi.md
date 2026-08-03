# Konfigurasi

*AutoWork — Gravicode Studios, dipimpin oleh Kang Fadhil*

Tiga lapis, dari prioritas terendah:

1. **`config.json`** — yang disimpan aplikasi
2. **Environment variable** — ditumpangkan saat aplikasi dijalankan, tidak pernah ditulis balik
3. **Antarmuka aplikasi** — yang Anda ubah di Pengaturan, lalu disimpan ke `config.json`

Lapisan tengah tidak pernah dipermanenkan. Meng-export `AUTOWORK_ALLOW_SHELL=false` untuk satu
kali jalan tidak diam-diam menimpa kebijakan yang sudah Anda simpan.

## Model

AutoWork berbicara dengan penyedia mana pun yang Anda atur. Hampir semuanya memakai format
OpenAI, sehingga satu jalur kode mencakup mayoritas; Anthropic dan Ollama punya klien sendiri
karena protokolnya memang berbeda.

![Pengaturan › Model](../images/settings-models.png)

### Preset bawaan

| Preset | Endpoint | Environment variable |
|---|---|---|
| `openai` | `https://api.openai.com/v1` | `OPENAI_API_KEY` |
| `azure-openai` | URL resource Anda — lihat di bawah | `AZURE_OPENAI_API_KEY` |
| `anthropic` | `https://api.anthropic.com/v1` | `ANTHROPIC_API_KEY` |
| `gemini` | `https://generativelanguage.googleapis.com/v1beta/openai/` | `GEMINI_API_KEY` |
| `ollama` | `http://localhost:11434` | `OLLAMA_HOST` |
| `deepseek` | `https://api.deepseek.com/v1` | `DEEPSEEK_API_KEY` |
| `qwen` | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1` | `DASHSCOPE_API_KEY` |
| `moonshot` | `https://api.moonshot.ai/v1` | `MOONSHOT_API_KEY` |
| `openrouter` | `https://openrouter.ai/api/v1` | `OPENROUTER_API_KEY` |
| `lmstudio` | `http://localhost:1234/v1` | — |
| `custom` | Anda yang isi | — |

> **ID model berubah lebih cepat daripada rilis aplikasi ini.** ID model bawaan pada tiap preset
> adalah titik awal, bukan daftar model yang didukung. Cocokkan ID-nya dengan katalog terbaru
> penyedia Anda lalu ubah di Pengaturan. Apa pun yang punya endpoint kompatibel-OpenAI tetap
> bekerja lewat `custom` meski tidak tercantum di sini.

### Azure OpenAI

Azure memberi setiap resource hostname sendiri dan membiarkan Anda menamai deployment sendiri,
jadi inilah satu-satunya preset yang tidak bisa diisikan untuk Anda.

Tempel endpoint persis seperti yang ditampilkan portal Azure:

```
https://resource-anda.openai.azure.com/
```

AutoWork melengkapinya menjadi `/openai/v1`, permukaan kompatibel-OpenAI milik Azure. Jika lebih
suka, ketik sendiri path lengkapnya — termasuk bentuk lama `/openai/deployments/…` — dan itu
dibiarkan persis seperti yang Anda ketik.

**ID model adalah nama deployment Anda**, bukan nama model dasarnya. Keduanya sering sama, tapi
tidak harus.

Deployment penalaran — keluarga gpt-5, seri o — menolak `temperature` kustom, menolak
`max_tokens`, dan menghabiskan anggaran keluarannya untuk berpikir sebelum menulis apa pun.
AutoWork mengenali ketiganya dari balasan penyedia itu sendiri lalu menyesuaikan, jadi tidak ada
setelan yang perlu diubah. Setelah uji koneksi, Pengaturan memberi tahu setelan mana yang tidak
diterima model.

### Peran model

Empat tugas, masing-masing bisa memakai model berbeda:

| Peran | Dipakai untuk | Perlu |
|---|---|---|
| Perencanaan dan penalaran | Menyusun rencana, meringkas, memverifikasi | Dukungan tools |
| Mengerjakan tugas | Menjalankan langkah dan memanggil tools | Dukungan tools |
| Membaca layar | `screen_look`, `image_describe` | Visi |
| Pencarian pengetahuan | Meng-embed entri dan kueri pengetahuan | Embedding |

Peran yang dibiarkan kosong akan memakai model aktif pertama yang punya kemampuan sesuai.
Kombinasi yang umum: model kuat untuk perencanaan, model murah dan cepat untuk eksekusi.

### Kunci API

Kunci **tidak pernah** ditulis ke `config.json`. File itu hanya menyimpan referensi:

- nama entri di penyimpanan rahasia terenkripsi, atau
- `env:NAMA_VARIABEL`, dibaca dari environment saat dipanggil dan tidak pernah dipermanenkan

Artinya `config.json` aman disalin antar komputer atau di-commit ke repo privat.

Untuk memakai pengelola rahasia eksternal, ketik `env:VAR_SAYA` di kotak kunci API lalu suntikkan
`VAR_SAYA` dengan cara apa pun yang Anda pakai.

## Skill

Skill adalah file `SKILL.md` — front matter YAML berisi nama dan deskripsi kapan skill itu
berlaku, diikuti instruksi dalam Markdown. AutoWork membacanya dari repositori GitHub yang Anda
daftarkan di **Skill › Repositori**, dengan bawaan `anthropics/skills` dan `obra/superpowers`.

Tambahkan repositori publik mana pun sebagai `owner/nama` atau URL GitHub; repositori skill
internal perusahaan bekerja sama saja. Daftarnya tersimpan di `config.json` pada
`skillRepositories`, dan skill yang terpasang berada di `skills/` dalam folder data.

Skill terpasang sebagai satu folder utuh, bukan hanya manifesnya: dokumen rujukan, template,
skema, dan skrip ikut serta, hingga 250 berkas dan 40 MB. Apa pun yang dilewati karena ukuran
dilaporkan, bukan dibuang diam-diam.

Hanya nama dan deskripsi skill yang ikut di setiap permintaan ke model. Isinya diambil
`skill_open` saat model menilainya relevan, berkas bundelnya oleh `skill_file`, jadi memasang
selusin skill tidak membuat setiap permintaan jadi dua belas kali lebih mahal.

Bila skrip membutuhkan library, AutoWork memasangnya ke virtual environment di dalam folder skill
itu sendiri: apa pun yang dideklarasikan `requirements.txt`, ditambah import yang bisa
diterjemahkan lewat tabel nama tetap. Import yang tidak dikenali dilaporkan, bukan ditebak. Daftar
paketnya muncul di kartu persetujuan sebelum apa pun diunduh, dan pemasangan terjadi sekali per
skill.

Skrip dijalankan `skill_run`, yang hanya ada bila
**Pengaturan › Izin › Izinkan menjalankan skrip bawaan skill** menyala. Bawaannya mati dan ia
meminta persetujuan sebelum tiap eksekusi — lihat
[keamanan.md](keamanan.md#skill-instruksi-dan-kadang-kode).

## Server MCP

**Pengaturan › Izin › Izinkan server MCP** harus menyala; bawaannya mati karena server stdio
adalah program yang dijalankan dengan hak akses penuh Anda. Lihat
[keamanan.md](keamanan.md#server-mcp-berada-di-luar-sandbox).

Server tersimpan di `config.json` pada `mcpServers`, masing-masing dengan transport, baris
perintah atau URL, dan peta environment yang isinya referensi rahasia, bukan rahasianya:

```jsonc
{
  "id": "a1b2c3d4",
  "name": "Tavily Search",
  "catalogId": "tavily",
  "transport": "Stdio",
  "command": "npx",
  "arguments": ["-y", "tavily-mcp"],
  "environment": { "TAVILY_API_KEY": "mcp.a1b2c3d4.TAVILY_API_KEY" },
  "enabled": false
}
```

Katalog bawaan mencakup filesystem, memory, sequential thinking, Playwright, Context7, Tavily,
Firecrawl, Notion, server rujukan protokolnya, dan `mcp-remote` untuk server terkelola. Selain itu
bisa ditambahkan manual. Sebagian besar butuh Node.js di PATH, dan galeri menyebutkannya per entri.

Server disambungkan sekali di awal sesi, bukan saat aplikasi dijalankan — menjalankan `npx`
memakan beberapa detik dan jendela tidak boleh menunggunya. Server yang tak terjangkau dicatat
lalu dilewati; sesi berlanjut tanpa tool-nya.

## Environment variable

### Kunci penyedia

Export kunci vendor apa pun, dan AutoWork membuat model yang sesuai saat pertama dijalankan:

```bash
export ANTHROPIC_API_KEY=sk-ant-...
export OPENAI_API_KEY=sk-...
export OLLAMA_HOST=http://localhost:11434
```

Model yang sudah Anda atur sendiri untuk penyedia itu tidak akan pernah ditimpa.

Azure butuh tiga, karena kunci saja tidak memberitahu ke mana harus dikirim atau apa yang diminta:

```bash
export AZURE_OPENAI_API_KEY=...
export AZURE_OPENAI_ENDPOINT=https://resource-anda.openai.azure.com/
export AZURE_OPENAI_DEPLOYMENT=gpt-5-mini
```

Bila endpoint-nya tidak ada, tidak ada model Azure yang dibuat sama sekali — model yang tidak
bisa dipanggil akan terpilih sebagai planner bawaan dan menggagalkan setiap run.

### Pencarian web

```bash
export TAVILY_API_KEY=tvly-...
```

Opsional. Pencarian tetap bekerja tanpanya, jatuh ke DuckDuckGo lalu Wikipedia; kunci memberi
hasil yang lebih baik plus jawaban ringkas. Atur di sini, atau di Pengaturan › Izin, tempat kunci
disimpan terenkripsi seperti kunci model mana pun.

![Kunci pencarian web berada di bawah sakelar jaringan di Pengaturan › Izin](../images/settings-search.png)

### Mendefinisikan satu model secara langsung

Untuk kontainer dan CI:

```bash
export AUTOWORK_MODEL=llama3.3:70b
export AUTOWORK_ENDPOINT=http://gpu-box.internal:11434/v1
export AUTOWORK_PROVIDER=custom          # ID preset, atau "custom"
export AUTOWORK_API_KEY=...              # opsional
export AUTOWORK_CONTEXT_WINDOW=131072    # opsional
```

Model ini dibuat lalu dipilih sebagai planner, menimpa apa pun yang tersimpan.

### Izin

```bash
export AUTOWORK_FOLDERS="$HOME/Dokumen;$HOME/Unduhan"      # izin baca/tulis
export AUTOWORK_FOLDERS_READONLY="$HOME/Referensi"         # izin hanya-baca
export AUTOWORK_ALLOW_SHELL=false
export AUTOWORK_ALLOW_DELETE=true
export AUTOWORK_ALLOW_INPUT=false
export AUTOWORK_ALLOW_SCREEN=true
```

Pisahkan beberapa folder dengan `;` atau `,`.

### Perilaku agen

Setelan yang sama ada di **Pengaturan › Agen**:

![Pengaturan › Agen](../images/settings-agent.png)

```bash
export AUTOWORK_MAX_STEPS=40
export AUTOWORK_AUTO_COMPACT=true
export AUTOWORK_COMPACT_THRESHOLD=0.75   # ringkas saat 75% jendela konteks terisi
```

### Tampilan dan lokasi

```bash
export AUTOWORK_THEME=Dark               # System | Light | Dark
export AUTOWORK_LANGUAGE=Indonesian      # System | English | Indonesian
export AUTOWORK_HOME=/media/usb/autowork # pindahkan seluruh folder data
```

## config.json

Cuplikan beranotasi:

```jsonc
{
  "schemaVersion": 1,
  "models": [
    {
      "id": "a1b2c3d4",
      "displayName": "Anthropic — claude-sonnet-5",
      "preset": "anthropic",
      "kind": "Anthropic",
      "endpoint": "https://api.anthropic.com/v1",
      "modelId": "claude-sonnet-5",
      "apiKeyRef": "env:ANTHROPIC_API_KEY",   // referensi, bukan kuncinya
      "contextWindow": 200000,
      "maxOutputTokens": 8192,
      "temperature": 0.2,
      "capabilities": "Tools, Vision, Reasoning",
      "enabled": true,

      // Opsional, kosong secara bawaan. Isi keduanya dari halaman harga penyedia Anda, maka
      // setiap run melaporkan biayanya; dikosongkan berarti hanya token yang dilaporkan.
      "inputPricePerMillion": 3.00,
      "outputPricePerMillion": 15.00,
      "currency": "USD"
    }
  ],
  "agent": {
    "plannerModelId": "a1b2c3d4",
    "maxSteps": 40,
    "maxConsecutiveFailures": 3,
    "maxParallelSubAgents": 3,
    "enableSubAgents": true,
    "enableSelfVerification": true,
    "enableAutoCompact": true,
    "autoCompactThreshold": 0.75,
    "compactKeepRecentTurns": 6,
    "toolTimeoutSeconds": 120
  },
  "permissions": {
    "roots": [
      { "path": "/home/fadhil/Dokumen", "access": "ReadWrite", "includeSubfolders": true }
    ],
    "deniedPatterns": ["**/.ssh/**", "**/*.pem", "**/.env"],
    "allowDelete": true,
    "softDelete": true,
    "allowShell": false,
    "allowNetwork": true,
    "allowScreenCapture": true,
    "allowInputControl": false,
    "maxReadBytes": 33554432,
    "maxBatchSize": 500,

    // Jawaban tetap untuk permintaan persetujuan. Kosong secara bawaan.
    "approvalRules": [
      { "effect": "Allow", "kind": "WriteFiles", "path": "/home/fadhil/Projects" },
      { "effect": "Deny",  "kind": "DeleteFiles" }
    ]
  },
  "appearance": { "theme": "System", "language": "System", "reduceMotion": false },

  "keepRunHistory": true,          // catat setiap run agar bisa dibuka lagi nanti
  "runHistoryRetentionDays": 30    // run yang lebih lama dihapus setiap kali sebuah run selesai
}
```

Menyunting file ini secara manual tidak masalah — AutoWork membacanya saat dijalankan. File yang
rusak dipindahkan menjadi `config.json.broken-<timestamp>` dan aplikasi memakai nilai bawaan,
bukan menolak menyala.

## Penyetelan agen

| Pengaturan | Fungsinya | Kapan diubah |
|---|---|---|
| `maxSteps` | Batas keras jumlah langkah per sesi | Naikkan untuk pekerjaan sangat panjang; turunkan untuk membatasi biaya |
| `maxConsecutiveFailures` | Kegagalan beruntun sebelum menyerah | Turunkan bila Anda lebih suka ia berhenti lebih awal |
| `enableSubAgents` | Menjalankan langkah independen secara paralel | Matikan bila penyedia membatasi laju permintaan |
| `enableSelfVerification` | Memeriksa hasil sebelum menyatakan berhasil | Menambah satu panggilan; layak dipertahankan |
| `autoCompactThreshold` | Ambang pemicu peringkasan konteks | Turunkan untuk model yang memburuk saat konteks hampir penuh |
| `compactKeepRecentTurns` | Giliran terakhir yang dipertahankan utuh | Naikkan bila agen kehilangan arah setelah peringkasan |
| `toolTimeoutSeconds` | Batas waktu per pemanggilan tool | Naikkan untuk perintah shell yang lambat |

## Riwayat run

Setiap run ditulis ke `runs/` di dalam folder data AutoWork sebagai dua berkas: ringkasan kecil
yang dibaca daftar Riwayat, dan transkrip lengkap yang baru dimuat saat Anda membuka run
tersebut. Membuka run lama akan memutar ulang isinya di Pita Kerja, persis seperti tampilannya
saat run itu berjalan.

| Pengaturan | Fungsinya | Kapan diubah |
|---|---|---|
| `keepRunHistory` | Mencatat setiap run | Matikan bila Anda tidak ingin ada yang dicatat |
| `runHistoryRetentionDays` | Berapa lama run disimpan | Perpendek di komputer bersama; perpanjang bila sering dirujuk |

Transkrip berisi apa pun yang dilihat run itu — isi berkas yang dibaca tool, hasil pencarian,
jawaban model. Semuanya disimpan sebagai JSON biasa bersama data AutoWork lainnya, dilindungi
oleh izin berkas dan tidak lebih dari itu. Setiap hasil tool dipotong di sekitar 4.000 karakter
agar satu run yang membaca folder besar tidak meninggalkan berkas berukuran megabita.

Pembersihan berjalan saat sebuah run selesai, jadi memperpendek masa simpan baru berlaku pada
run berikutnya, bukan seketika. Menghapus run dari halaman Riwayat membuang kedua berkasnya
sekaligus.

## Aturan persetujuan

Aturan menjawab satu golongan permintaan persetujuan sekali saja, bukan setiap kali. Tiap aturan
punya `effect` (`Allow` atau `Deny`), `kind` yang opsional, dan `path`.

```jsonc
"approvalRules": [
  // Berhenti bertanya soal penulisan di dalam satu pohon proyek.
  { "effect": "Allow", "kind": "WriteFiles", "path": "/home/fadhil/Projects" },

  // Tolak semua penghapusan, di mana pun, tanpa bertanya.
  { "effect": "Deny", "kind": "DeleteFiles" },

  // Tolak apa pun di dalam satu folder.
  { "effect": "Deny", "path": "/home/fadhil/Arsip" }
]
```

Empat batasan berlaku, dan semuanya ditegakkan, bukan sekadar imbauan:

- **Penolakan menang** atas pengizinan, dalam urutan apa pun, dan atas "izinkan selama sesi ini"
  yang diklik lebih dulu.
- **Aturan tidak pernah memperluas apa yang diizinkan.** Tindakan yang diizinkan tetap melewati
  sandbox, jadi aturan izin di luar folder yang Anda berikan tidak mengubah apa pun.
- **Aturan izin wajib punya `path` sekaligus `kind`.** Tanpa keduanya, aturan itu diabaikan.
- **Hanya `WriteFiles` dan `DeleteFiles` yang bisa diizinkan.** `RunCommand`, `ControlInput`,
  `CaptureScreen`, dan `NetworkAccess` tidak punya folder untuk dibatasi, jadi tetap ditanyakan
  per tindakan — sakelar kemampuannya di halaman Izin adalah tempat keputusan itu berada.
  Penolakan boleh memakai jenis apa pun.

Path dicocokkan pada batas folder, jadi aturan untuk `/home/fadhil/Proj` tidak mencakup
`/home/fadhil/Proj-private`.

## Harga model

`inputPricePerMillion` dan `outputPricePerMillion` kosong sampai Anda mengisinya, dan AutoWork
tidak membawa tabel harga apa pun. Ini disengaja: harga berubah lebih cepat daripada id model,
dan angka bawaan yang basi lalu diam-diam melaporkan biaya lebih rendah dari kenyataan lebih
buruk daripada tidak melaporkan biaya sama sekali.

Bila keduanya diisi, setiap run melaporkan biaya dalam `currency` di samping jumlah tokennya.
Bila salah satu kosong, yang dilaporkan hanya token. Kalau penyedia menjawab sebagian panggilan
tanpa menyebut pemakaiannya, totalnya ditulis "minimal *n*", bukan sebagai angka pasti.

## Tugas tersimpan

Tugas disimpan di `jobs.json` di samping `config.json`. Masing-masing punya tujuan, pemicu, dan
sakelar aktif yang bermula mati.

```jsonc
[
  {
    "name": "Faktur Senin",
    "goal": "Ringkas faktur minggu lalu jadi dokumen Word",
    "trigger": "Schedule",
    "enabled": true,
    "period": "Weekly",
    "dayOfWeek": "Monday",
    "timeOfDay": "08:30:00"
  },
  {
    "name": "Rapikan hasil pindai",
    "goal": "Urutkan apa pun yang baru di folder Pindai berdasarkan tanggal",
    "trigger": "FolderChange",
    "enabled": true,
    "watchFolder": "/home/fadhil/Pindai",
    "watchFilter": "*.pdf",
    "quietSeconds": 20
  }
]
```

Tugas berjalan di halaman Kerja persis seperti Anda mengetiknya, jadi izin yang diminta pun sama.
Tiga hal yang perlu diketahui:

- **Pemicu folder hanya bisa memantau folder yang Anda izinkan.** Yang menunjuk ke tempat lain
  dilaporkan dan diabaikan, bukan dipantau.
- **Tugas tidak diantrikan.** Yang jatuh tempo saat run lain berjalan dilewati, dan itu disebutkan.
- **Waktu yang terlewat tidak menumpuk.** Waktu jatuh tempo dihitung dari jam, jadi aplikasi yang
  ditutup sepanjang akhir pekan berjalan sekali saat dibuka lagi, bukan tiga kali.

## Rapat dan rekaman

Mati sampai dikonfigurasi, dan lokal kecuali Anda menentukan lain. AutoWork tidak membawa model
suara; arahkan ke model yang Anda punya.

```jsonc
"transcription": {
  "mode": "Local",                 // Off | Local | Remote
  "command": "whisper-cli",
  "arguments": "-m {model} -f {audio} --output-txt --no-prints",
  "modelPath": "C:/models/ggml-base.en.bin",
  "language": ""
}
```

`{audio}`, `{model}`, dan `{language}` akan diisi. Templatnya dipecah menjadi argumen *sebelum*
substitusi, sehingga rekaman yang path-nya mengandung spasi tetap menjadi satu argumen.
whisper.cpp, faster-whisper, dan openai-whisper sama-sama bisa; masing-masing punya argumennya
sendiri. Hasilnya dibaca dari keluaran perintah itu sendiri atau dari berkas `.txt`/`.srt` yang
ditulis di samping rekaman.

`"mode": "Remote"` mengunggah rekaman ke endpoint yang kompatibel dengan Whisper. Itu tidak pernah
menjadi bawaan, punya kartu persetujuannya sendiri, dan perlu diingat bahwa rekaman rapat berisi
orang-orang yang tidak pernah menyetujui apa pun.

## Peramban tempat Anda sudah masuk

```jsonc
"browser": {
  "enabled": false,
  "executablePath": "",     // kosong berarti mencari Edge atau Chrome
  "headless": false
}
```

Mati secara bawaan dan **terpisah dari `permissions.allowNetwork`** — mengambil halaman publik dan
bertindak sebagai pengguna yang sudah masuk bukan izin yang sama. Keduanya harus aktif agar tool
peramban muncul sama sekali.

Profilnya berada di `browser-profile/` di dalam folder data AutoWork, bukan profil peramban Anda
yang asli: menempel ke peramban yang sedang Anda buka akan berebut kunci profil. Profil ini
bertahan, jadi Anda cukup masuk sekali. Navigasi dan klik mengikuti `permissions.networkAllowList`
yang sama dengan tool web, dan masing-masing bertanya lebih dulu.

## Berkas terhapus

Dengan `permissions.softDelete` menyala (bawaan), penghapusan dipindahkan ke `recycle/` di dalam
folder data AutoWork beserta indeks yang mencatat asal masing-masing. Halaman Pemulihan
menampilkannya dan mengembalikannya. Tidak ada yang bisa dipulihkan ke dalam folder AutoWork
sendiri, dan memulihkan di atas berkas yang sudah ada memerlukan konfirmasi kedua yang eksplisit.

Item yang direcycle oleh build sebelum indeks ini ada ditampilkan sebagai tidak bisa dipulihkan,
bukan disembunyikan — item itu tetap memakan ruang, dan Anda mungkin masih ingin membersihkannya.
