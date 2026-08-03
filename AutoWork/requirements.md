Nama: AutoWork

Deskripsi:
clone dari “Claude Cowork” yang bisa menangani pekerjaan desktop sehari‑hari secara otomatis: mulai dari mengorganisasi file, membuat dokumen profesional, hingga menjalankan workflow multi‑step dengan aman di sandbox. Ia bekerja seperti rekan kerja digital yang bisa melihat layar, memahami konteks, dan bertindak langsung di komputer Anda. dibuat dengan .NET dan memanfaatkan semua library yang ada di .NET seperti Microsoft Agent Framework, Microsoft Extensions AI, Microsoft Extensions Vector Data, Semantic Kernel, OllamaSharp atau ML.Net (local embedding) dengan UI UX yang sama. Hanya support ke berbagai model LLM yang bisa di konfigurasi dari aplikasi atau app.config seperti OpenAI, Anthropic, Gemini, Ollama, DeepSeek, Qwen, Moonshot, dsb (open ai compatible endpoint)

---

 🧠 Arsitektur Utama
- Brain (Think) → Didukung model Claude Opus/Fable dengan konteks hingga 1M token. Bisa merencanakan workflow panjang, melakukan reasoning, dan self‑verification.  
- Eyes (See) → Menggunakan Computer Use API, menangkap screenshot resolusi tinggi, mengenali elemen UI, membaca tabel/teks kecil.  
- Hands (Act) → Mengontrol mouse, keyboard, shell commands, serta akses file lokal dalam VM terisolasi.  

---

 ⚡ Fitur Inti
- Autonomous Task Execution → Jalankan workflow multi‑step tanpa input manual, dengan error recovery dan progress tracking.  
- Direct Local File Access → Membaca, menulis, rename, dan mengorganisasi file/folder langsung di komputer.  
- Professional Document Creation → Membuat Excel dengan formula, Word, PowerPoint, PDF siap pakai.  
- Sub‑Agent Coordination → Pecah tugas kompleks jadi sub‑task paralel untuk efisiensi.  
- Secure Sandbox Environment → Semua operasi berjalan di VM terisolasi dengan izin folder spesifik.  
- App Integrations → Integrasi dengan Google Drive, Gmail, GitHub, Asana, Notion, PayPal, dll.  

---

 📂 Use Cases Nyata
- File Management → Organisasi folder download, batch rename, cleanup.  
- Document Synthesis → Kompilasi riset, ringkasan meeting, laporan dengan sitasi.  
- Data Processing → Ekstraksi data dari PDF/CSV, cleaning dataset, analisis.  
- Creative Workflows → Batch resize gambar, asset management, automasi desain.  
- Finance Tracking → Ekstraksi info belanja dari email/screenshot, tracking pengeluaran.  
- Meeting Intelligence → Proses rekaman meeting, action items, follow‑up tasks lintas zona waktu.  
- Web Automation → Navigasi website, isi form, ekstraksi data via browser extension.  

---

 📊 Tabel Ringkas Fitur

| Kategori | Fitur Utama |
|--------------|-----------------|
| Workflow | Autonomous execution, sub‑agent coordination |
| File Ops | Direct local access, batch operations, folder organization |
| Dokumen | Word, Excel, PowerPoint, PDF generation |
| Data | Extraction, cleaning, analytics |
| Creative | Image processing, asset management |
| Finance | Receipt extraction, expense tracking |
| Meetings | Summarization, action items, sync notes |
| Integrasi | Google Drive, Gmail, GitHub, Asana, Notion, PayPal |

---

Notes:
- Semua aksi dicatat dalam action log untuk transparansi.  
- Permissions bisa dikustomisasi agar hanya folder tertentu yang diakses.  
- Knowledge Bases → memori topik khusus untuk konteks lintas sesi.  [claudecowork.im](https://claudecowork.im/features)  
- buat script installer sehingga user tinggal jalankan dan langsung siap digunakan + lengkapi dengan dokumentasi instalasinya
- terdapat menu untuk mengatur model-model LLM, apikeys, endpoint yang bisa digunakan, otomatis bisa disimpan ke file konfigurasi / env variables.
- Dibuat dengan .NET 10 berupa aplikasi desktop multi-platform dengan Avalonia UI, buat dengan UI UX yang modern dan keren dibantu skill frontend-design dengan dark/light theme
- Konfigurasi integrasi bisa di atur lewat aplikasi, file konfigurasi atau env vars
- Optimasi code agar cepat dan ringan
- Dokumentasi lengkap dengan Bahasa Indonesia dan English
- Readme dengan Bahasa Indonesia dan English
- Tambahkan fitur-fitur claude cowork yang baru yang mungkin belum tertera diatas
- ada fitur auto compact sebelum context window penuh
- tambahkan info di docs dan source/app dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
- PLAN.md untuk roadmap pengembangan dan Progress.md untuk tracking development