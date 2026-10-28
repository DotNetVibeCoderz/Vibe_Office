# Server MCP OfficeNet

`officenet-mcp` memungkinkan asisten AI — Claude, GitHub Copilot di VS Code, Cursor, atau klien apa
pun yang berbicara [Model Context Protocol](https://modelcontextprotocol.io) — membuat, menyunting,
mengonversi, dan meninjau berkas Word, Excel, PowerPoint, dan PDF. Isinya OfficeNet di balik 23
tool, berjalan lokal lewat stdio, terkurung di satu direktori.

*English: [../Mcp.md](../Mcp.md)*

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

---

## Yang dihasilkannya

Setiap gambar di bawah adalah dokumen yang dibuat **lewat protokol**: `tools/ScreenshotGen`
menjalankan server sebagai proses terpisah, memanggil tool-nya lewat stdio persis seperti asisten,
lalu merender berkas yang ditulisnya. Tidak ada yang digambar tangan atau ditangkap dari Office.

![Dokumen yang dibuat server MCP: laporan, workbook dengan chart, deck, dan invoice](../screenshots/mcp-showcase.png)

| | |
|---|---|
| `word_create` — heading, `**tebal**` dari teks bergaya Markdown, daftar, tabel | ![Laporan Word](../screenshots/mcp-word-report.png) |
| `excel_create` — formula dihitung, format angka, chart native | ![Workbook Excel](../screenshots/mcp-excel-workbook.png) |
| `pptx_create` — slide chart yang bisa disunting di PowerPoint | ![Slide chart](../screenshots/mcp-deck-03.png) |
| `word_fill_template` — baris invoice diulang dari JSON, format Rupiah | ![Invoice terisi](../screenshots/mcp-invoice.png) |

## Instalasi

```bash
dotnet tool install -g Gravicode.OfficeNet.Mcp
officenet-mcp --help
```

Atau dari clone, tanpa instalasi:

```bash
dotnet run --project src/OfficeNet.Mcp -- --root ~/Documents/officenet
```

## Menghubungkan klien

**Claude Code**

```bash
claude mcp add officenet -- officenet-mcp --root ~/Documents/officenet
```

**Claude Desktop** — `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "officenet": {
      "command": "officenet-mcp",
      "args": ["--root", "C:\\Users\\anda\\Documents\\officenet"]
    }
  }
}
```

**VS Code** — `.vscode/mcp.json`:

```json
{
  "servers": {
    "officenet": {
      "type": "stdio",
      "command": "officenet-mcp",
      "args": ["--root", "${workspaceFolder}/documents"]
    }
  }
}
```

Lalu minta dengan bahasa biasa — *"Buat laporan kuartal dalam Word dengan tabel pendapatan per
wilayah, lalu ekspor ke PDF dan tunjukkan halaman pertamanya."* Asisten yang memilih tool-nya.

## Model keamanan

Server MCP melakukan apa yang diminta model, dan model bisa dibujuk meminta apa saja — oleh
pengguna, atau oleh teks tersembunyi di dalam dokumen yang disuruh dibacanya. Jadi batasnya
ditegakkan di kode, bukan diserahkan pada pertimbangan model:

- **Satu workspace.** Setiap path di-resolve terhadap `--root` dan ditolak bila keluar darinya —
  `..`, path absolut di tempat lain, folder saudara yang hanya berbagi awalan (`C:\work` tidak
  pernah mengizinkan `C:\workshop`), atau symbolic link di dalam root yang menunjuk keluar. Hal yang
  sama berlaku untuk gambar *yang disebut di dalam* dokumen: `img` HTML atau gambar Markdown juga
  dibaca lewat workspace, jadi `../../rahasia.png` atau path absolut di tempat lain dilewati, tidak
  disematkan.
- **`--read-only`** menolak setiap tool yang akan membuat atau mengubah berkas; membaca,
  mengekstrak, dan meninjau tetap bisa. Pakai bila asisten hanya boleh *melihat* dokumen.
- **Error dijelaskan, bukan dibuang mentah.** Kesalahan yang bisa diperbaiki model — jenis blok tak
  dikenal, sheet yang tidak ada, konversi yang tidak didukung — kembali sebagai error yang terbaca
  sehingga panggilan berikutnya bisa membetulkannya. Kegagalan lain dilaporkan generik, tanpa stack
  trace.
- **Tanpa jaringan.** Tidak ada tool yang mengambil URL; gambar HTML dengan `src` jarak jauh
  dilewati.
- **Petunjuk untuk klien.** Tool membawa `readOnlyHint` atau `destructiveHint`, yang dipakai klien
  untuk memutuskan apa yang perlu dikonfirmasi ke pengguna lebih dulu.

Root bawaannya `$OFFICENET_MCP_ROOT`, lalu direktori saat ini. Arahkan ke folder khusus, bukan ke
direktori home.

## Daftar tool

| Tool | Fungsinya |
|---|---|
| **Word** | |
| `word_create` | `.docx` dari blok: `heading`, `paragraph`, `quote`, `bullets`, `numbered`, `table`, `image`, `chart`, `markdown`, `pagebreak`, `toc`. Teks boleh memakai `**tebal**` dan `*miring*`; blok `markdown` menerima satu dokumen Markdown utuh (dialek GitHub: tabel, kode, kutipan, tautan, gambar lewat path workspace). |
| `word_append` | Menambahkan blok ke dokumen yang sudah ada. |
| `word_fill_template` | Mengisi `{{field}}`, `{{a.b}}`, `{{total:N0}}`, `{{#items}}…{{/items}}` berulang, dan `{{^x}}…{{/x}}` terbalik dari JSON, dalam culture pilihan. |
| `word_replace_text` | Cari dan ganti, termasuk frasa yang dipecah Word lintas run. |
| `word_track_changes` | Mendaftar tracked changes, atau menerima/menolak semuanya. |
| `word_protect` | Membatasi penyuntingan (`readOnly`, `comments`, `trackedChanges`, `forms`, `none`). |
| `word_watermark` | Memasang atau menghapus watermark diagonal. |
| `word_merge` / `word_split` | Menggabungkan dokumen; memecah per section, heading, atau page break. |
| **Excel** | |
| `excel_create` | Sheet dari baris; string berawalan `=` adalah formula, dihitung sebelum disimpan; tanggal ISO jadi tanggal; format angka per kolom; chart opsional. |
| `excel_write_cells` | Menulis sel menurut alamat lalu menghitung ulang. |
| `excel_read` | Sheet atau range sebagai baris nilai hasil hitung (maksimal 200 baris). |
| `excel_add_chart` | Chart native dari kategori dan seri. |
| **PowerPoint** | |
| `pptx_create` | Slide bertipe `title`, `bullets`, `section`, `table`, `chart`, `image`, `blank`, dengan catatan pembicara. |
| `pptx_from_html` | HTML menjadi deck berhalaman. |
| **PDF** | |
| `pdf_merge` / `pdf_split` | Menggabungkan; memecah per N halaman. |
| `pdf_protect` | Enkripsi AES-256 dengan password. |
| **Semua format** | |
| `convert` | `docx/xlsx/pptx → pdf`, `docx → html/md`, `xlsx/pptx → html`, `html → docx/pptx/xlsx`, `md/txt → docx`, `pdf → docx/xlsx`, `docx/xlsx/pptx/pdf → tif`. |
| `extract_text` | Teks polos, maksimal 20 000 karakter. |
| `document_info` | Format, metadata, dan struktur — heading, sheet, slide, halaman. |
| `render_page` | Satu halaman sebagai **gambar PNG yang bisa dilihat model**, dan opsional disimpan. |
| `list_files` | Dokumen di dalam workspace. |

`render_page` yang mengubah server ini dari sekadar pembuat berkas menjadi sesuatu yang bisa
dipakai asisten untuk memeriksa hasil kerjanya sendiri: membuat dokumen, melihatnya, lalu
membetulkan yang salah.

## Resource dan prompt

Dokumen di workspace juga tersedia sebagai **resource**, sehingga klien bisa mendaftarnya dan
melampirkan salah satunya ke percakapan tanpa panggilan tool. Masing-masing dibaca sebagai teks yang
bisa dipakai model:

| Dokumen | Dibaca sebagai |
| --- | --- |
| `.docx` | Markdown — heading, daftar, tabel, tautan |
| `.xlsx` | setiap sheet sebagai CSV, masing-masing di bawah `# NamaSheet` |
| `.pptx`, `.pdf` | teks biasa |

URI-nya `officenet://workspace/` ditambah path-nya, tiap segmen di-percent-encode
(`officenet://workspace/laporan%202026/ringkasan.docx`); template-nya
`officenet://workspace/{+path}`. Pembacaan melewati batas workspace yang sama dengan setiap tool,
sehingga URI yang keluar — `../`, di-encode atau tidak — ditolak. Paling banyak 500 dokumen didaftar
dan 200.000 karakter dikembalikan.

Tiga **prompt** adalah rencana siap pakai yang ditulis dengan tool-tool ini, yang bisa ditawarkan
klien sebagai perintah:

| Prompt | Argumen | Fungsinya |
| --- | --- | --- |
| `report_from_spreadsheet` | `spreadsheet`, `output?` | membaca workbook, menulis laporan Word berisi temuan, tabel, dan chart, lalu memeriksanya |
| `summarize_document` | `path`, `length?` | membaca dokumen dan meringkasnya, angka sesuai yang tertulis |
| `deck_from_document` | `source`, `output?`, `slides?` | mengubah dokumen menjadi deck singkat dan memeriksa slide yang meluap |

Prompt hanya menghasilkan teks; tidak ada yang dijalankan sampai model memanggil tool-nya.

## Memanggil dari kode

Ini server MCP biasa, jadi pustaka klien MCP apa pun bisa mengendalikannya. Dengan SDK C# resmi:

```csharp
using ModelContextProtocol.Client;

await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
{
    Name = "officenet",
    Command = "officenet-mcp",
    Arguments = ["--root", "documents"],
}));

var result = await client.CallToolAsync("word_create", new Dictionary<string, object?>
{
    ["path"] = "surat.docx",
    ["blocks"] = new object[]
    {
        new { type = "heading", text = "Surat Pemberitahuan" },
        new { type = "paragraph", text = "Dengan hormat, kami sampaikan **jadwal baru**." },
    },
});
```

## Cara pengujiannya

`tests/OfficeNet.Mcp.Tests` menjalankan server sebagai proses terpisah dan berbicara dengannya lewat
stdio — jalur yang sama dengan setiap klien. Memanggil kelas tool secara langsung akan melewatkan
bagian yang benar-benar ditemui klien: skema JSON yang dibangkitkan dari tipe parameter, pengikatan
argumen, bagaimana eksepsi menjadi hasil error, dan apakah ada keluaran nyasar ke stdout yang merusak
alirannya. Setiap jenis dokumen dibuat lewat protokol, lalu dibuka kembali oleh pustakanya dan
diperiksa validator OPC independen.

## Lihat juga

- [API terpadu](OfficeNet.md) — `Office.Open`, `ConvertToPdf`, `ExtractText`, yang dibungkus beberapa tool
- [WordNet](WordNet.md), [ExcelNet](ExcelNet.md), [PowerPointNet](PowerPointNet.md), [PdfNet](PdfNet.md), [Rendering](Rendering.md)
