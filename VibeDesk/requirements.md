Nama: VibeDesk

Deskripsi: aplikasi perkantoran berbasis web seperti Google Docs, Google Sheets, Google Slides, Google Drive dan Google Calendar, fiturnya:

 📄 Dokumentasi & Kolaborasi
- Real-time Editing: Semua pengguna bisa mengedit dokumen secara bersamaan.  
- Comment & Suggestion: Memberi masukan tanpa mengubah isi utama.  
- Version History: Lihat dan kembalikan ke versi sebelumnya.  
- Offline Mode: Edit dokumen tanpa koneksi internet.  

 📊 Spreadsheet & Analitik
- Formula & Functions: Dukungan rumus matematis, statistik, dan logika.  
- Pivot Table: Analisis data dengan ringkasan dinamis.  
- Conditional Formatting: Highlight data sesuai aturan.  
- Chart & Graphs: Visualisasi data interaktif.  

 🎨 Presentasi & Desain
- Slide Templates: Tema siap pakai untuk presentasi.  
- Animations & Transitions: Efek visual antar slide.  
- Multimedia Embedding: Sisipkan video, audio, gambar.  
- Presenter View: Mode khusus untuk pembicara.  

 📅 Kalender & Organisasi
- Event Scheduling: Buat dan kelola acara.  
- Reminders & Notifications: Ingatkan pengguna sebelum acara.  
- Shared Calendars: Kalender tim untuk sinkronisasi jadwal.  
- Integration with Email: Sinkronisasi dengan Gmail/Outlook.  

 📦 Drive (Storage & File Management)
- Cloud Storage: Simpan file secara online dengan kapasitas besar.  
- File Sharing: Bagikan file/folder dengan kontrol akses (viewer, editor, commenter).  
- Collaboration Integration: Terhubung langsung dengan Docs, Sheets, Slides untuk kolaborasi.  
- File Sync: Sinkronisasi otomatis antara perangkat (desktop, mobile, web).  
- Search & Filters: Cari file dengan keyword, tipe, atau tanggal.  
- Backup & Restore: Simpan salinan file penting dan pulihkan jika terhapus.  
- Offline Access: Akses file tanpa koneksi internet.  
- Third-party Integration: Hubungkan dengan aplikasi eksternal (misalnya Slack, Trello).  
- Security & Encryption: Proteksi data dengan enkripsi dan kontrol akses.  
- Version Control: Lihat riwayat perubahan file.  

Chat Bot Office Assistant
  - Nama 'Mr Clippy'
  - Fungsinya membantu user dalam menyelesaikan pekerjaan sesuai aplikasi yang sedang aktif, menambah/menghapus/mengedit dokumen di setiap aplikasi, menambahkan asset, menuliskan script, dsb. 
  - Chat Panel yang ada disemua aplikasi diatas (word/dokumen, spreadsheet, presentasi, calendar, drive) dengan tampilan yang keren, multi session (create/delete), reset session, bisa attach gambar (diupload lalu url-nya di jadikan image content) dan dokumen (di upload dan disertakan linknya ke text message). Bisa di hide/show.
  - System Prompt (persona), temperature, model dan setting lainnya di simpan di appsetting
  - Menggunakan Semantic Kernel Library dengan dukungan model: Open AI, Anthropic, Gemini, Ollama (bisa pilih)
  - Tambahkan beberapa common functions (kernel functions) yang diperlukan termasuk query ke tavily (search internet), scrap page url, baca file dari url, cek tanggal, Waktu, math calculation, dan beberapa function yang diperlukan lainnya
  - Tambahkan functions untuk query data ke drive, isi dokumen untuk mengetahui berbagai informasi dan fungsi-fungsi yang dimiliki aplikasi
  - Bisa render chat thread dengan mark down dengan baik ke html (baik table, media (image, video, audio), code, dan lainnya dengan baik)

 🔒 Keamanan & Akses
- Role-based Permissions: Atur hak akses (viewer, editor, commenter).  
- Two-Factor Authentication: Proteksi login tambahan.  
- Data Encryption: Keamanan data saat transfer dan penyimpanan.  

 🌐 Integrasi & Ekstensi
- Add-ons & Plugins: Tambahan fitur dari pihak ketiga.  
- Cloud Storage Integration: Simpan otomatis ke Google Drive/OneDrive.  
- API Access: Integrasi dengan aplikasi lain.  
- Cross-Platform Support: Bisa diakses dari web, mobile, desktop.  

---

 🔗 Integrasi Lintas Aplikasi

- Docs + Drive  
  Dokumen otomatis tersimpan di Drive, bisa dibagikan dengan kontrol akses, dan mudah dicari lewat fitur pencarian Drive.  

- Sheets + Drive  
  Spreadsheet tersimpan di Drive, mendukung backup, restore, dan version control. Data bisa dihubungkan dengan aplikasi eksternal via API.  

- Slides + Drive  
  Presentasi tersimpan di Drive, bisa langsung dibagikan ke tim, dan diakses dari berbagai perangkat.  

- Calendar + Drive  
  Lampiran file dari Drive bisa ditambahkan ke event Calendar, sehingga peserta meeting langsung punya akses ke dokumen terkait.  

- Docs + Sheets + Slides  
  Semua mendukung kolaborasi real-time, komentar, dan suggestion. Format file bisa saling di-embed (misalnya grafik dari Sheets ke Slides).  

- Sheets + Calendar  
  Data jadwal atau timeline dari Sheets bisa dihubungkan dengan event di Calendar untuk manajemen proyek.  

- Drive + Semua Aplikasi  
  Drive berfungsi sebagai pusat penyimpanan dan distribusi, menghubungkan Docs, Sheets, Slides, dan Calendar dalam satu ekosistem.  

---

Notes:
- Dibuat dengan .NET 10, Web dengan Blazor Server, Mobile dengan MAUI Blazor, Desktop dengan Wpf Blazor Hybrid, Web Api dengan ASP.Net Core Min API mendukung berbagai protocol: SignalR, Rest (default), Grpc 
- Desain UI UX modern dan keren dengan micro-interaction dibantu dengan skill frontend-design dengan dukungan dark/light theme
- Tambahkan readme.md (English dan Bahasa Indonesia), sertakan screenshot
- Database support SQLite (dev), SQLServer, MySQL, Postgre 
- Cache: MemoryCache (dev), Redis (production) 
- Storage Support: FileSystem (dev), AzureBlob, S3, MinIO
- Tambahkan dokumentasi lengkap di folder docs (sertakan screenshot)
- Buatkan banyak sample data, dan user
- optimasi kode agar aplikasi cepat dan ringan
- REST API untuk Web: Integrasi dengan aplikasi eksternal dengan Min API dan swagger
- Tambahkan info di dokumentasi dan app: dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
- Plan.md untuk roadmap pengembangan, Progress.md untuk tracking development
