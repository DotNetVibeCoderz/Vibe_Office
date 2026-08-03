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
        ["knowledge.entry.title"] = "Title",
        ["knowledge.entry.text"] = "What to remember",

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
        ["knowledge.entry.title"] = "Judul",
        ["knowledge.entry.text"] = "Yang perlu diingat",

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
