# Fitur Lengkap GemBox.Document, GemBox.Spreadsheet, GemBox.Presentation, dan GemBox.Pdf

## 1. GemBox.Document

Library untuk memproses dokumen Word tanpa membutuhkan Microsoft Office terinstal.

### Format yang Didukung

**Input**
- DOCX
- DOC
- RTF
- ODT
- HTML
- TXT
- PDF
- Markdown

**Output**
- DOCX
- PDF
- HTML
- XPS
- TXT
- Image (PNG, JPG, TIFF)

### Fitur Dokumen
- Membuat dokumen dari nol
- Membaca dokumen existing
- Edit isi dokumen
- Merge dokumen
- Split dokumen
- Template engine
- Mail merge
- Nested mail merge
- Find & Replace
- Generate surat, kontrak, invoice

### Struktur Dokumen
- Paragraph
- Section
- Table
- Header/Footer
- Bookmark
- Hyperlink
- Footnote
- Endnote
- Table of Contents (TOC)
- List & Numbering

### Formatting
- Font formatting
- Paragraph style
- Themes
- Border
- Shading
- Page setup
- Column layout

### Objek
- Images
- Shapes
- Text Box
- Drawing
- Watermark
- Chart
- SmartArt (preserve)

### Review & Collaboration
- Track Changes
- Comments
- Revision
- Protected Range

### PDF Export
- Word → PDF
- PDF/A
- Font embedding
- Digital signing
- High-quality rendering

---

## 2. GemBox.Spreadsheet

Komponen untuk mengelola file Excel dan spreadsheet tanpa memerlukan Microsoft Excel.

### Format yang Didukung

**Input**
- XLSX
- XLS
- XLSM
- ODS
- CSV
- TXT
- HTML

**Output**
- XLSX
- XLS
- PDF
- CSV
- ODS
- HTML
- XPS
- PNG/JPG

### Worksheet Management
- Create workbook
- Create worksheet
- Copy worksheet
- Move worksheet
- Hide worksheet
- Protect worksheet

### Cell Features
- Read/write cell
- Data type handling
- Formula
- Rich text
- Hyperlink
- Comments
- Named ranges

### Formula Engine
- SUM
- ROUND
- MOD
- ABS
- AVERAGE
- COUNT
- MEDIAN
- PMT
- NPV
- IRR
- DATE
- YEAR
- TODAY
- VLOOKUP
- HLOOKUP
- INDEX
- MATCH
- IF
- AND
- OR

### Data Features
- Sort
- Filter
- AutoFilter
- Validation
- Drop-down list
- Duplicate detection

### Table & Pivot
- Excel Table
- Pivot Table
- Pivot Cache

### Charts
- Bar Chart
- Line Chart
- Pie Chart
- Area Chart
- Scatter Plot
- Combo Chart

### Security
- Password protection
- Encryption
- Digital signature

---

## 3. GemBox.Presentation

Library untuk membuat dan mengelola PowerPoint secara programatik tanpa Microsoft PowerPoint.

### Format yang Didukung

**Input**
- PPTX
- PPT

**Output**
- PPTX
- PDF
- XPS
- PNG
- JPG
- TIFF

### Slide Management
- Create presentation
- Create slide
- Duplicate slide
- Delete slide
- Reorder slide

### Layout & Master
- Slide Layout
- Master Slide
- Theme Handling

### Shapes
- Rectangle
- Circle
- Line
- Arrow
- Custom Shape

### Text
- Text Box
- Rich Text
- Bullet List
- Numbering

### Media
- Images
- Audio
- Video
- Embedded Object

### Tables & Charts
- Table editing
- Pie Chart
- Bar Chart
- Line Chart
- Scatter Chart
- Doughnut Chart

### Conversion
- PPTX → PDF
- PPTX → Image
- PPTX → XPS

---

## 4. GemBox.Pdf

Library khusus PDF untuk membuat, membaca, dan memodifikasi PDF secara native di .NET.

### PDF Creation
- Generate PDF
- Draw text
- Draw image
- Draw graphics
- Vector graphics

### PDF Reading
- Read PDF
- Extract pages
- Extract text
- Extract metadata

### PDF Editing
- Add pages
- Delete pages
- Move pages
- Merge PDF
- Split PDF

### Forms (AcroForm)
- Text fields
- Checkbox
- Radio buttons
- Combo box
- Button

### Annotation
- Highlights
- Notes
- Comments
- Links

### Security
- Password protection
- Encryption
- Permissions

### Digital Signature
- Certificate signing
- Signature validation
- Timestamp support

### Extraction
- Text extraction
- Metadata extraction
- Attachment extraction

---

## Perbandingan Cepat

| Fitur | Document | Spreadsheet | Presentation | Pdf |
|---------|---------|---------|---------|---------|
| Word Processing | ✅ | ❌ | ❌ | ❌ |
| Excel Processing | ❌ | ✅ | ❌ | ❌ |
| PowerPoint Processing | ❌ | ❌ | ✅ | ❌ |
| PDF Creation | ✅ | ✅ | ✅ | ✅ |
| PDF Edit Native | Terbatas | ❌ | ❌ | ✅ |
| Mail Merge | ✅ | ❌ | ❌ | ❌ |
| Formula Engine | ❌ | ✅ | ❌ | ❌ |
| Charts | ✅ | ✅ | ✅ | ❌ |
| Digital Signature | ✅ | ✅ | Sebagian | ✅ |
| Office Required | ❌ | ❌ | ❌ | ❌ |

## Rekomendasi untuk Workflow Automation

- GemBox.Spreadsheet → laporan, dashboard, ETL.
- GemBox.Document → kontrak, surat, laporan.
- GemBox.Presentation → slide KPI dan presentasi otomatis.
- GemBox.Pdf → digital signing, merge, archive PDF.
