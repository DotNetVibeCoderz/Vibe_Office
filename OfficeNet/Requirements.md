Nama: OfficeNet
Deskripsi:berisi kumpulan library .NET 10 multiplatform hasil rewrite dari library Python (python-pptx, python-docx, openpyxl+pandas, PyPDF2), fitur-fiturnya modular per komponen sebagai berikut:

---

 🔹 WordNet (rewrite python-docx)
- Manipulasi Dokumen Word: buat, buka, edit `.docx`
- Formatting: heading, paragraf, font, style, tabel
- Insert Media: gambar, hyperlink, shape
- Section & Page Layout: margin, orientasi, header/footer
- Metadata: properties dokumen, custom fields
- Export: ke PDF atau image (via integrasi PdfNet)

---

 🔹 ExcelNet (rewrite openpyxl + pandas -> gunakan library yang sudah pernah dibuat dari https://github.com/DotNetVibeCoderz/Vibe_ML/tree/main/GravicodeScience atau di folder local 'C:\Users\mifma\Documents\CodeSandbox\GravicodeScience')
- Spreadsheet Manipulation: buat, edit, baca `.xlsx`
- Cell Operations: formula, style, merge, conditional formatting
- Worksheet Management: tambah/hapus sheet, rename
- DataFrame Integration: dukungan mirip Pandas (GraviFrame) untuk analisis data 
- Chart & Pivot: generate chart, pivot table
- Import/Export: CSV, JSON, SQL, PDF

---

 🔹 PowerPointNet (rewrite python-pptx)
- Slide Creation: tambah, hapus, reorder slide
- Layout & Themes: template, background, master slide
- Content Insertion: teks, gambar, tabel, grafik
- Animations & Transitions: basic effect API
- Export: ke PDF, image, atau video (opsional integrasi FFMPEG)
- Metadata: properties presentasi

---

 🔹 PdfNet (rewrite PyPDF2)
- Read & Write PDF: buka, gabung, split, rotate halaman
- Text Extraction: parsing teks, metadata
- Form Handling: isi form fields, extract data
- Encryption/Decryption: proteksi password
- Annotation Support: highlight, comment, stamp
- Conversion: PDF → image, Word, Excel

---

 🔹 Fitur Global OfficeNet
- Multiplatform: .NET 10 (Windows, Linux, macOS)
- Polyglot Notebook Integration: dukungan Jupyter/Polyglot Notebook
- Unified API: konsisten antar library (WordNet, ExcelNet, PowerPointNet, PdfNet)
- Extensible Plugins: bisa tambah modul lain (misalnya VisioNet, OneNoteNet)
- Documentation Lengkap: API reference + contoh aplikasi (console, Avalonia, Blazor Server/Web)

---

 🎯 Contoh Aplikasi
- Console: batch convert Word → PDF
- Desktop (Avalonia UI): editor Word/Excel ringan
- Web (Blazor Server): dashboard upload & preview dokumen
- Automation: ETL pipeline dengan ExcelNet + PdfNet
# Semua contoh aplikasi buatkan UI UX dengan bagus dibantu skill frontend-design

---
Notes:
- cek dulu nama-nama library diatas, jika sudah ada di nuget sebelumnya, semuanya kasih prefix 'Gravicode.'
- pastikan semua fitur dibuat sesuai referensi librarynya, tambahkan fitur dan modul yang diperlukan lainnya
- optimisasi code agar performance tinggi dan efisien memory, jika diperlukan menuliskan low level library-nya dengan rust silakan
- tambahkan readme (English dan Bahasa Indonesia) dan dokumentasi lengkap di folder docs
- Buatkan aplikasi contoh: OfficeNet Gallery yaitu playground untuk menguji berbagai fitur yang dimiliki, setiap demo disertakan contoh kode-nya, dan ada halaman chatbot yang bisa membuat code / project .NET yang memanfaatkan library OfficeNet dengan semantic kernel + LLM dengan support model dari OpenAI, Anthropic, Gemini, Deepseek + Tools (kernel functions) yang diperlukan termasuk search internet (tavily), scrap web, get date time, math, dan lainnya. Buat aplikasi pakai Avalonia dengan UI UX yang keren dibantu skill frontend-design
- tambahkan info pada source dan dokumentasi dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil
- tambahkan screenshot pada readme dan dokumentasi
- lakukan benchmark
- publish nuget package OfficeNet dengan projectUrl dan repository ke url https://github.com/DotNetVibeCoderz/Vibe_Office/tree/main/OfficeNet
- buatkan CI workflow juga
- Plan.md untuk roadmap pengembangan, dan Progress.md untuk tracking development checklist
---
