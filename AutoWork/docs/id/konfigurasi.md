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
      "enabled": true
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
    "maxBatchSize": 500
  },
  "appearance": { "theme": "System", "language": "System", "reduceMotion": false }
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
