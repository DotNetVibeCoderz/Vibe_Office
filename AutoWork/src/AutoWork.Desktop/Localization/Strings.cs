using System.ComponentModel;
using AutoWork.Core.Configuration;

namespace AutoWork.Desktop.Localization;

/// <summary>
/// Interface text in English and Bahasa Indonesia.
///
/// Kept as a keyed table rather than generated resources so both languages sit side by side in
/// one file — when a label changes, the translation is right there and cannot silently drift.
/// Views bind through the indexer, and switching language raises a change for every key at once.
/// </summary>
public sealed class Strings : INotifyPropertyChanged
{
    private UiLanguage _language = UiLanguage.System;

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;

            // "Item[]" is the conventional name for "every indexer value changed", which is
            // what tells every {Binding L[...]} in the tree to re-read.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIndonesian)));
        }
    }

    public bool IsIndonesian => Resolve() == UiLanguage.Indonesian;

    public string this[string key] =>
        (Resolve() == UiLanguage.Indonesian ? Indonesian : English).GetValueOrDefault(key)
        ?? English.GetValueOrDefault(key)
        ?? key;

    private UiLanguage Resolve()
    {
        if (_language != UiLanguage.System) return _language;

        // "System" follows the OS, and anything that is not Indonesian falls back to English.
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(culture, "id", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Indonesian
            : UiLanguage.English;
    }

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["app.tagline"] = "Your digital coworker",
        ["app.credit"] = "Built by Gravicode Studios · led by Kang Fadhil",

        ["nav.work"] = "Work",
        ["nav.activity"] = "Activity",
        ["nav.knowledge"] = "Knowledge",
        ["nav.skills"] = "Skills",
        ["nav.mcp"] = "MCP",
        ["nav.integrations"] = "Integrations",
        ["nav.settings"] = "Settings",

        ["organ.think"] = "Think",
        ["organ.see"] = "See",
        ["organ.act"] = "Act",

        ["work.prompt"] = "What should I take care of?",
        ["work.placeholder"] = "Organise everything in my Downloads folder by type, then list what moved.",
        ["work.start"] = "Start work",
        ["work.stop"] = "Stop",
        ["work.clear"] = "Clear",
        ["work.tape"] = "WORK TAPE",
        ["work.plan"] = "PLAN",
        ["work.idle"] = "Nothing running.",
        ["work.idle.hint"] = "Describe a job above and AutoWork will plan it, do it, and check its own work.",
        ["work.summary"] = "RESULT",
        ["work.model"] = "Model",
        ["work.elapsed"] = "Elapsed",
        ["work.steps"] = "Steps",
        ["work.compacted"] = "Context compacted",
        ["work.verified"] = "Verified",
        ["work.unverified"] = "Not verified",
        ["work.nomodel"] = "No model is configured yet. Open Settings › Models to add one.",
        ["work.examples"] = "TRY SOMETHING LIKE",

        ["approval.title"] = "AutoWork needs your permission",
        ["approval.allow"] = "Allow once",
        ["approval.allowrun"] = "Allow for this run",
        ["approval.deny"] = "Deny",
        ["approval.affected"] = "Affects",

        ["activity.title"] = "Activity",
        ["activity.subtitle"] = "Every action AutoWork has taken on this computer.",
        ["activity.empty"] = "No actions recorded yet.",
        ["activity.refresh"] = "Refresh",
        ["activity.open"] = "Open log folder",

        ["knowledge.title"] = "Knowledge",
        ["knowledge.subtitle"] = "Notes AutoWork keeps between sessions.",
        ["knowledge.new"] = "New knowledge base",
        ["knowledge.empty"] = "No knowledge bases yet.",
        ["knowledge.empty.hint"] = "Create one, and AutoWork will remember what it learns about your work.",
        ["knowledge.entries"] = "notes",
        ["knowledge.delete"] = "Delete",
        ["knowledge.autoattach"] = "Use automatically",
        ["knowledge.name"] = "Name",
        ["knowledge.add"] = "Add note",
        ["knowledge.addfile"] = "Add from file",
        ["knowledge.addfile.hint"] = "Word, PowerPoint, Excel, PDF, CSV, HTML, text or Markdown. The file name becomes the title.",
        ["knowledge.imported"] = "Imported {0} file(s).",
        ["knowledge.entry.title"] = "Title",
        ["knowledge.entry.text"] = "What to remember",

        ["skills.title"] = "Skills",
        ["skills.subtitle"] = "Instructions the agent can follow for particular kinds of work. It reads a skill only when the job calls for it.",
        ["skills.installed"] = "INSTALLED",
        ["skills.installed.empty"] = "No skills installed. Browse a repository below and add what fits your work.",
        ["skills.installed.badge"] = "Installed",
        ["skills.repos"] = "REPOSITORIES",
        ["skills.repos.hint"] = "Skills are read from these GitHub repositories. Add your own — a company's internal skills repository works the same way.",
        ["skills.repo.invalid"] = "Enter a repository as owner/name, or paste its GitHub URL.",
        ["skills.search"] = "Filter by name or description",
        ["skills.browse"] = "Browse",
        ["skills.browsing"] = "Reading repositories…",
        ["skills.install"] = "Add",
        ["skills.found"] = "Found {0} skills across {1} repositories.",
        ["skills.none"] = "No skills found. Check the repository names and your network.",
        ["skills.installed.msg"] = "Installed {0}.",
        ["skills.failed"] = "Could not download {0}.",
        ["skills.removed"] = "Removed {0}.",

        ["mcp.title"] = "MCP servers",
        ["mcp.subtitle"] = "Model Context Protocol servers lend the agent their own tools — a browser, a database, a vendor's API. Add one, test it, then enable it.",
        ["mcp.disabled"] = "MCP is turned off. A server is a program AutoWork starts with your full rights, so it needs an explicit switch: Settings › Permissions › Allow MCP servers.",
        ["mcp.search"] = "Search the catalogue",
        ["mcp.add"] = "Add",
        ["mcp.added.badge"] = "Added",
        ["mcp.added"] = "Added {0}. Enable it on the right when you are ready.",
        ["mcp.removed"] = "Removed {0}.",
        ["mcp.needs"] = "{0} is required.",
        ["mcp.test"] = "Test",
        ["mcp.testing"] = "Starting the server…",
        ["mcp.yours"] = "YOUR SERVERS",
        ["mcp.yours.empty"] = "Nothing added yet. Pick one from the catalogue.",

        ["integrations.title"] = "Integrations",
        ["integrations.subtitle"] = "Connect the services you already work in.",
        ["integrations.enable"] = "Enabled",
        ["integrations.test"] = "Test connection",
        ["integrations.save"] = "Save",
        ["integrations.docs"] = "Get credentials",

        ["settings.title"] = "Settings",
        ["settings.models"] = "Models",
        ["settings.permissions"] = "Permissions",
        ["settings.agent"] = "Agent",
        ["settings.appearance"] = "Appearance",
        ["settings.about"] = "About",

        ["models.subtitle"] = "AutoWork works with any provider you configure. Keys stay on this computer.",
        ["models.add"] = "Add model",
        ["models.empty"] = "No models configured.",
        ["models.test"] = "Test",
        ["models.remove"] = "Remove",
        ["models.provider"] = "Provider",
        ["models.name"] = "Display name",
        ["models.endpoint"] = "Endpoint",
        ["models.modelid"] = "Model id",
        ["models.apikey"] = "API key",
        ["models.apikey.hint"] = "Stored encrypted on this computer. Type env:NAME to read an environment variable instead.",
        ["models.context"] = "Context window (tokens)",
        ["models.roles"] = "ROLES",
        ["models.role.planner"] = "Planning and reasoning",
        ["models.role.executor"] = "Doing the work",
        ["models.role.vision"] = "Reading the screen",
        ["models.role.embedding"] = "Knowledge search",
        ["models.capabilities"] = "Capabilities",
        ["models.cap.tools"] = "Tools",
        ["models.cap.vision"] = "Vision",
        ["models.cap.embeddings"] = "Embeddings",
        ["models.cap.reasoning"] = "Reasoning",

        ["perm.subtitle"] = "AutoWork can only reach the folders you grant here. Nothing else on this computer is visible to it.",
        ["perm.folders"] = "FOLDERS",
        ["perm.add"] = "Add folder",
        ["perm.none"] = "No folders granted. AutoWork cannot read or write anything yet.",
        ["perm.readonly"] = "Read only",
        ["perm.readwrite"] = "Read and write",
        ["perm.remove"] = "Remove",
        ["perm.capabilities"] = "CAPABILITIES",
        ["perm.delete"] = "Allow deleting files",
        ["perm.softdelete"] = "Move deleted files to a recycle folder instead of erasing them",
        ["perm.shell"] = "Allow running shell commands",
        ["perm.shellapproval"] = "Ask before every command",
        ["perm.screen"] = "Allow screen capture",
        ["perm.input"] = "Allow controlling mouse and keyboard",
        ["perm.inputapproval"] = "Ask before every input action",
        ["perm.mcp"] = "Allow MCP servers",
        ["perm.mcp.hint"] = "MCP servers are programs AutoWork starts with your full rights. PathGuard cannot see inside them. Leave this off unless you need one.",
        ["perm.network"] = "Allow network access",
        ["perm.searchkey"] = "Tavily API key for web search",
        ["perm.searchkeyhint"] = "Optional. Without one, search still works using DuckDuckGo and Wikipedia. The key is stored encrypted, never in config.json.",
        ["perm.confirm"] = "Ask before writing, deleting or running anything",

        ["agent.maxsteps"] = "Maximum steps per run",
        ["agent.subagents"] = "Split independent work across parallel sub-agents",
        ["agent.parallel"] = "Maximum parallel sub-agents",
        ["agent.verify"] = "Check my own work before reporting success",
        ["agent.compact"] = "Summarise older context automatically as the window fills",
        ["agent.threshold"] = "Compact when this full",

        ["appearance.theme"] = "Theme",
        ["appearance.theme.system"] = "Match system",
        ["appearance.theme.light"] = "Light",
        ["appearance.theme.dark"] = "Dark",
        ["appearance.language"] = "Language",
        ["appearance.language.system"] = "Match system",
        ["appearance.motion"] = "Reduce motion",

        ["about.version"] = "Version",
        ["about.datafolder"] = "Data folder",
        ["about.open"] = "Open",

        ["common.save"] = "Save",
        ["common.cancel"] = "Cancel",
        ["common.close"] = "Close",
        ["common.saved"] = "Saved.",
        ["common.testing"] = "Testing…",
    };

    private static readonly Dictionary<string, string> Indonesian = new(StringComparer.Ordinal)
    {
        ["app.tagline"] = "Rekan kerja digital Anda",
        ["app.credit"] = "Dibuat oleh Gravicode Studios · dipimpin Kang Fadhil",

        ["nav.work"] = "Kerja",
        ["nav.activity"] = "Aktivitas",
        ["nav.knowledge"] = "Pengetahuan",
        ["nav.skills"] = "Skill",
        ["nav.mcp"] = "MCP",
        ["nav.integrations"] = "Integrasi",
        ["nav.settings"] = "Pengaturan",

        ["organ.think"] = "Pikir",
        ["organ.see"] = "Lihat",
        ["organ.act"] = "Tindak",

        ["work.prompt"] = "Apa yang perlu saya kerjakan?",
        ["work.placeholder"] = "Rapikan folder Downloads saya berdasarkan jenis file, lalu buat daftar yang dipindahkan.",
        ["work.start"] = "Mulai kerja",
        ["work.stop"] = "Hentikan",
        ["work.clear"] = "Bersihkan",
        ["work.tape"] = "PITA KERJA",
        ["work.plan"] = "RENCANA",
        ["work.idle"] = "Tidak ada yang berjalan.",
        ["work.idle.hint"] = "Tuliskan pekerjaan di atas — AutoWork akan merencanakan, mengerjakan, lalu memeriksa hasilnya sendiri.",
        ["work.summary"] = "HASIL",
        ["work.model"] = "Model",
        ["work.elapsed"] = "Waktu",
        ["work.steps"] = "Langkah",
        ["work.compacted"] = "Konteks diringkas",
        ["work.verified"] = "Terverifikasi",
        ["work.unverified"] = "Belum terverifikasi",
        ["work.nomodel"] = "Belum ada model yang dikonfigurasi. Buka Pengaturan › Model untuk menambahkan.",
        ["work.examples"] = "COBA SESUATU SEPERTI",

        ["approval.title"] = "AutoWork memerlukan izin Anda",
        ["approval.allow"] = "Izinkan sekali",
        ["approval.allowrun"] = "Izinkan selama sesi ini",
        ["approval.deny"] = "Tolak",
        ["approval.affected"] = "Berdampak pada",

        ["activity.title"] = "Aktivitas",
        ["activity.subtitle"] = "Semua tindakan yang dilakukan AutoWork di komputer ini.",
        ["activity.empty"] = "Belum ada tindakan tercatat.",
        ["activity.refresh"] = "Muat ulang",
        ["activity.open"] = "Buka folder log",

        ["knowledge.title"] = "Pengetahuan",
        ["knowledge.subtitle"] = "Catatan yang disimpan AutoWork antar sesi.",
        ["knowledge.new"] = "Basis pengetahuan baru",
        ["knowledge.empty"] = "Belum ada basis pengetahuan.",
        ["knowledge.empty.hint"] = "Buat satu, dan AutoWork akan mengingat apa yang dipelajarinya tentang pekerjaan Anda.",
        ["knowledge.entries"] = "catatan",
        ["knowledge.delete"] = "Hapus",
        ["knowledge.autoattach"] = "Gunakan otomatis",
        ["knowledge.name"] = "Nama",
        ["knowledge.add"] = "Tambah catatan",
        ["knowledge.addfile"] = "Tambah dari file",
        ["knowledge.addfile.hint"] = "Word, PowerPoint, Excel, PDF, CSV, HTML, teks, atau Markdown. Nama file dipakai sebagai judul.",
        ["knowledge.imported"] = "{0} file diimpor.",
        ["knowledge.entry.title"] = "Judul",
        ["knowledge.entry.text"] = "Yang perlu diingat",

        ["skills.title"] = "Skill",
        ["skills.subtitle"] = "Instruksi yang bisa diikuti agen untuk jenis pekerjaan tertentu. Skill hanya dibaca saat pekerjaannya memang membutuhkan.",
        ["skills.installed"] = "TERPASANG",
        ["skills.installed.empty"] = "Belum ada skill terpasang. Telusuri repositori di bawah lalu tambahkan yang sesuai pekerjaan Anda.",
        ["skills.installed.badge"] = "Terpasang",
        ["skills.repos"] = "REPOSITORI",
        ["skills.repos.hint"] = "Skill dibaca dari repositori GitHub berikut. Tambahkan milik Anda sendiri — repositori skill internal perusahaan bekerja dengan cara yang sama.",
        ["skills.repo.invalid"] = "Isi repositori sebagai owner/nama, atau tempel URL GitHub-nya.",
        ["skills.search"] = "Saring berdasarkan nama atau deskripsi",
        ["skills.browse"] = "Telusuri",
        ["skills.browsing"] = "Membaca repositori…",
        ["skills.install"] = "Tambah",
        ["skills.found"] = "Ditemukan {0} skill dari {1} repositori.",
        ["skills.none"] = "Tidak ada skill ditemukan. Periksa nama repositori dan jaringan Anda.",
        ["skills.installed.msg"] = "{0} dipasang.",
        ["skills.failed"] = "Gagal mengunduh {0}.",
        ["skills.removed"] = "{0} dihapus.",

        ["mcp.title"] = "Server MCP",
        ["mcp.subtitle"] = "Server Model Context Protocol meminjamkan tool miliknya ke agen — peramban, basis data, API vendor. Tambahkan, uji, lalu aktifkan.",
        ["mcp.disabled"] = "MCP sedang mati. Server adalah program yang dijalankan AutoWork dengan hak akses penuh Anda, jadi perlu sakelar eksplisit: Pengaturan › Izin › Izinkan server MCP.",
        ["mcp.search"] = "Cari di katalog",
        ["mcp.add"] = "Tambah",
        ["mcp.added.badge"] = "Ditambahkan",
        ["mcp.added"] = "{0} ditambahkan. Aktifkan di panel kanan bila sudah siap.",
        ["mcp.removed"] = "{0} dihapus.",
        ["mcp.needs"] = "{0} wajib diisi.",
        ["mcp.test"] = "Uji",
        ["mcp.testing"] = "Menjalankan server…",
        ["mcp.yours"] = "SERVER ANDA",
        ["mcp.yours.empty"] = "Belum ada yang ditambahkan. Pilih satu dari katalog.",

        ["integrations.title"] = "Integrasi",
        ["integrations.subtitle"] = "Hubungkan layanan yang sudah Anda pakai sehari-hari.",
        ["integrations.enable"] = "Aktif",
        ["integrations.test"] = "Uji koneksi",
        ["integrations.save"] = "Simpan",
        ["integrations.docs"] = "Dapatkan kredensial",

        ["settings.title"] = "Pengaturan",
        ["settings.models"] = "Model",
        ["settings.permissions"] = "Izin",
        ["settings.agent"] = "Agen",
        ["settings.appearance"] = "Tampilan",
        ["settings.about"] = "Tentang",

        ["models.subtitle"] = "AutoWork bekerja dengan penyedia mana pun yang Anda atur. Kunci API tetap di komputer ini.",
        ["models.add"] = "Tambah model",
        ["models.empty"] = "Belum ada model.",
        ["models.test"] = "Uji",
        ["models.remove"] = "Hapus",
        ["models.provider"] = "Penyedia",
        ["models.name"] = "Nama tampilan",
        ["models.endpoint"] = "Endpoint",
        ["models.modelid"] = "ID model",
        ["models.apikey"] = "Kunci API",
        ["models.apikey.hint"] = "Disimpan terenkripsi di komputer ini. Ketik env:NAMA untuk membaca variabel lingkungan.",
        ["models.context"] = "Jendela konteks (token)",
        ["models.roles"] = "PERAN",
        ["models.role.planner"] = "Perencanaan dan penalaran",
        ["models.role.executor"] = "Mengerjakan tugas",
        ["models.role.vision"] = "Membaca layar",
        ["models.role.embedding"] = "Pencarian pengetahuan",
        ["models.capabilities"] = "Kemampuan",
        ["models.cap.tools"] = "Tools",
        ["models.cap.vision"] = "Visi",
        ["models.cap.embeddings"] = "Embedding",
        ["models.cap.reasoning"] = "Penalaran",

        ["perm.subtitle"] = "AutoWork hanya bisa menjangkau folder yang Anda izinkan di sini. Selain itu tidak terlihat olehnya.",
        ["perm.folders"] = "FOLDER",
        ["perm.add"] = "Tambah folder",
        ["perm.none"] = "Belum ada folder yang diizinkan. AutoWork belum bisa membaca atau menulis apa pun.",
        ["perm.readonly"] = "Hanya baca",
        ["perm.readwrite"] = "Baca dan tulis",
        ["perm.remove"] = "Hapus",
        ["perm.capabilities"] = "KEMAMPUAN",
        ["perm.delete"] = "Izinkan menghapus file",
        ["perm.softdelete"] = "Pindahkan file terhapus ke folder daur ulang, bukan menghapus permanen",
        ["perm.shell"] = "Izinkan menjalankan perintah shell",
        ["perm.shellapproval"] = "Tanya sebelum setiap perintah",
        ["perm.screen"] = "Izinkan menangkap layar",
        ["perm.input"] = "Izinkan mengendalikan mouse dan keyboard",
        ["perm.inputapproval"] = "Tanya sebelum setiap tindakan input",
        ["perm.mcp"] = "Izinkan server MCP",
        ["perm.mcp.hint"] = "Server MCP adalah program yang dijalankan AutoWork dengan hak akses penuh Anda. PathGuard tidak bisa melihat ke dalamnya. Biarkan mati kecuali Anda memang membutuhkannya.",
        ["perm.network"] = "Izinkan akses jaringan",
        ["perm.searchkey"] = "Kunci API Tavily untuk pencarian web",
        ["perm.searchkeyhint"] = "Opsional. Tanpa kunci, pencarian tetap jalan lewat DuckDuckGo dan Wikipedia. Kunci disimpan terenkripsi, tidak pernah di config.json.",
        ["perm.confirm"] = "Tanya sebelum menulis, menghapus, atau menjalankan apa pun",

        ["agent.maxsteps"] = "Maksimum langkah per sesi",
        ["agent.subagents"] = "Bagi pekerjaan independen ke sub-agen paralel",
        ["agent.parallel"] = "Maksimum sub-agen paralel",
        ["agent.verify"] = "Periksa hasil kerja sendiri sebelum menyatakan selesai",
        ["agent.compact"] = "Ringkas konteks lama otomatis saat jendela hampir penuh",
        ["agent.threshold"] = "Ringkas saat terisi sebanyak ini",

        ["appearance.theme"] = "Tema",
        ["appearance.theme.system"] = "Ikuti sistem",
        ["appearance.theme.light"] = "Terang",
        ["appearance.theme.dark"] = "Gelap",
        ["appearance.language"] = "Bahasa",
        ["appearance.language.system"] = "Ikuti sistem",
        ["appearance.motion"] = "Kurangi animasi",

        ["about.version"] = "Versi",
        ["about.datafolder"] = "Folder data",
        ["about.open"] = "Buka",

        ["common.save"] = "Simpan",
        ["common.cancel"] = "Batal",
        ["common.close"] = "Tutup",
        ["common.saved"] = "Tersimpan.",
        ["common.testing"] = "Menguji…",
    };
}
