using System.Diagnostics;
using System.Text.RegularExpressions;
using AutoWork.Core.Skills;

namespace AutoWork.Agents;

/// <summary>What a skill's scripts need before they can run, and where that was worked out from.</summary>
public sealed record DependencyPlan
{
    /// <summary>Packages to install, already resolved to installable names.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>Imports that could not be resolved, and system tools a package will still need.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>"requirements.txt", "imports", or both — shown so consent is informed.</summary>
    public string Source { get; init; } = "";

    public bool IsEmpty => Packages.Count == 0;
}

/// <summary>
/// Works out and installs what a skill's Python scripts import.
///
/// Real skills mostly do not declare their dependencies: two of the eighteen in
/// `anthropics/skills` ship a `requirements.txt`, and the rest name their libraries only in prose
/// and in the scripts' own import lines. So there are two sources, and they are treated very
/// differently.
///
/// **Declared** dependencies are read from `requirements.txt` and installed as written.
///
/// **Undeclared** ones are resolved through <see cref="ImportToPackage"/> — a fixed table, not a
/// heuristic. An import name is not a package name (`fitz` is PyMuPDF, `cv2` is opencv-python),
/// and inventing a plausible package name is precisely how typosquatting works. So an import that
/// is not in the table is reported to the model rather than guessed at: a wrong install is worse
/// than a missing one, because it succeeds.
///
/// Everything lands in a virtual environment inside the skill's own folder. The user's Python is
/// never touched, and removing the skill removes its libraries with it.
/// </summary>
public static partial class SkillDependencies
{
    private const string VirtualEnvironment = ".venv";
    private const string MarkerFile = ".venv/.autowork-installed";

    /// <summary>
    /// Import name → the package that provides it. Only entries that are unambiguous and
    /// checkable belong here; the point of the table is that nothing is invented.
    /// </summary>
    private static readonly Dictionary<string, (string Package, string? NeedsSystemTool)> ImportToPackage =
        new(StringComparer.Ordinal)
        {
            // Documents — what the published skills actually reach for.
            ["pypdf"] = ("pypdf", null),
            ["PyPDF2"] = ("PyPDF2", null),
            ["pdfplumber"] = ("pdfplumber", null),
            ["fitz"] = ("PyMuPDF", null),
            ["reportlab"] = ("reportlab", null),
            ["docx"] = ("python-docx", null),
            ["pptx"] = ("python-pptx", null),
            ["openpyxl"] = ("openpyxl", null),
            ["xlsxwriter"] = ("XlsxWriter", null),

            // These two are pip packages that wrap a program which pip cannot install.
            ["pdf2image"] = ("pdf2image", "poppler"),
            ["pytesseract"] = ("pytesseract", "tesseract"),

            // Data and imaging.
            ["pandas"] = ("pandas", null),
            ["numpy"] = ("numpy", null),
            ["matplotlib"] = ("matplotlib", null),
            ["PIL"] = ("Pillow", null),
            ["cv2"] = ("opencv-python", "OpenCV's system libraries on Linux"),

            // Text and web.
            ["bs4"] = ("beautifulsoup4", null),
            ["lxml"] = ("lxml", null),
            ["yaml"] = ("PyYAML", null),
            ["requests"] = ("requests", null),
            ["httpx"] = ("httpx", null),
            ["markdown"] = ("Markdown", null),
            ["jinja2"] = ("Jinja2", null),
            ["dateutil"] = ("python-dateutil", null),

            // Agent tooling, used by the mcp-builder and api skills.
            ["anthropic"] = ("anthropic", null),
            ["openai"] = ("openai", null),
            ["mcp"] = ("mcp", null),
        };

    /// <summary>
    /// Modules that ship with Python. Without this list every script would appear to need `os`
    /// and `json` installed from PyPI, which is both wrong and alarming to read in a consent card.
    /// </summary>
    private static readonly HashSet<string> Standard = new(StringComparer.Ordinal)
    {
        "abc", "argparse", "array", "ast", "asyncio", "base64", "binascii", "bisect", "builtins",
        "calendar", "codecs", "collections", "colorsys", "concurrent", "configparser", "contextlib",
        "copy", "csv", "ctypes", "dataclasses", "datetime", "decimal", "difflib", "dis", "email",
        "enum", "errno", "fcntl", "filecmp", "fileinput", "fnmatch", "fractions", "ftplib",
        "functools", "gc", "getopt", "getpass", "gettext", "glob", "gzip", "hashlib", "heapq",
        "hmac", "html", "http", "imaplib", "importlib", "inspect", "io", "ipaddress", "itertools",
        "json", "keyword", "linecache", "locale", "logging", "lzma", "mailbox", "math", "mimetypes",
        "multiprocessing", "operator", "os", "pathlib", "pickle", "pkgutil", "platform", "plistlib",
        "pprint", "queue", "random", "re", "readline", "secrets", "select", "shelve", "shlex",
        "shutil", "signal", "site", "smtplib", "socket", "sqlite3", "ssl", "stat", "statistics",
        "string", "struct", "subprocess", "sys", "tarfile", "tempfile", "textwrap", "threading",
        "time", "timeit", "tkinter", "token", "tokenize", "traceback", "types", "typing",
        "unicodedata", "unittest", "urllib", "uuid", "venv", "warnings", "wave", "weakref",
        "webbrowser", "xml", "xmlrpc", "zipfile", "zlib", "zoneinfo",
    };

    /// <summary>Reads a skill and decides what would have to be installed to run its scripts.</summary>
    public static DependencyPlan Plan(Skill skill)
    {
        var packages = new List<string>();
        var notes = new List<string>();
        var sources = new List<string>();

        // ── Declared ──────────────────────────────────────────────────────────────────────
        foreach (var manifest in skill.Files.Where(f => f.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase)))
        {
            var path = FileSkillStore.Contain(skill.Folder, manifest);
            if (path is null || !File.Exists(path)) continue;

            foreach (var line in File.ReadAllLines(path))
            {
                var requirement = line.Split('#')[0].Trim();
                if (requirement.Length == 0) continue;

                // "-r other.txt" and "-e ." point outside this file; leave them to pip's own
                // handling rather than half-interpreting them here.
                if (requirement.StartsWith('-')) continue;

                packages.Add(requirement);
            }

            if (!sources.Contains("requirements.txt")) sources.Add("requirements.txt");
        }

        // ── Undeclared, resolved through the table ────────────────────────────────────────
        var declared = packages
            .Select(p => PackageName(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        var systemTools = new SortedSet<string>(StringComparer.Ordinal);
        var local = LocalModules(skill);

        foreach (var import in ScanImports(skill).OrderBy(i => i, StringComparer.Ordinal))
        {
            if (Standard.Contains(import) || local.Contains(import)) continue;

            if (!ImportToPackage.TryGetValue(import, out var mapped))
            {
                unresolved.Add(import);
                continue;
            }

            if (mapped.NeedsSystemTool is { } tool) systemTools.Add($"{mapped.Package} also needs {tool} installed separately");

            if (declared.Contains(mapped.Package)) continue;
            if (packages.Contains(mapped.Package, StringComparer.OrdinalIgnoreCase)) continue;

            packages.Add(mapped.Package);
            if (!sources.Contains("imports")) sources.Add("imports");
        }

        if (unresolved.Count > 0)
        {
            notes.Add($"not installed because the package name is not known: {string.Join(", ", unresolved)}");
        }

        notes.AddRange(systemTools);

        return new DependencyPlan
        {
            Packages = packages,
            Notes = notes,
            Source = string.Join(" + ", sources),
        };
    }

    /// <summary>Python files that live in the skill, so a sibling import is not mistaken for a package.</summary>
    private static HashSet<string> LocalModules(Skill skill) =>
        skill.Files
            .Where(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Top-level module names imported by the skill's Python files.</summary>
    private static IEnumerable<string> ScanImports(Skill skill)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in skill.Files.Where(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase)))
        {
            var path = FileSkillStore.Contain(skill.Folder, file);
            if (path is null || !File.Exists(path)) continue;

            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { continue; }

            foreach (Match match in ImportStatement().Matches(text))
            {
                var name = match.Groups["module"].Value.Split('.')[0];

                // A leading dot is a relative import — always local.
                if (name.Length == 0) continue;

                if (seen.Add(name)) yield return name;
            }
        }
    }

    /// <summary>"pandas>=2.0" → "pandas".</summary>
    private static string PackageName(string requirement)
    {
        var cut = requirement.IndexOfAny(['=', '>', '<', '!', '~', '[', ';', ' ']);
        return cut < 0 ? requirement : requirement[..cut];
    }

    // ── Provisioning ──────────────────────────────────────────────────────────────────────

    /// <summary>True when the skill's environment already has this exact set installed.</summary>
    public static bool IsProvisioned(Skill skill, DependencyPlan plan)
    {
        if (plan.IsEmpty) return true;

        var marker = Path.Combine(skill.Folder, MarkerFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(marker)) return false;

        try
        {
            return File.ReadAllText(marker).Trim() == Fingerprint(plan);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The interpreter to run a script with: the skill's own if it has one.</summary>
    public static string? InterpreterFor(Skill skill)
    {
        var python = Path.Combine(skill.Folder, VirtualEnvironment,
            OperatingSystem.IsWindows() ? "Scripts" : "bin",
            OperatingSystem.IsWindows() ? "python.exe" : "python");

        return File.Exists(python) ? python : null;
    }

    /// <summary>
    /// Creates the skill's environment and installs the plan into it. Returns an error string on
    /// failure, or null on success — the caller turns that into the tool's vocabulary.
    /// </summary>
    public static async Task<string?> ProvisionAsync(
        Skill skill, DependencyPlan plan, string basePython, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (plan.IsEmpty) return null;

        var venv = Path.Combine(skill.Folder, VirtualEnvironment);

        if (InterpreterFor(skill) is null)
        {
            var created = await RunAsync(basePython, ["-m", "venv", venv], skill.Folder, timeout, cancellationToken)
                .ConfigureAwait(false);

            if (created is not null) return $"could not create an environment for {skill.Name}: {created}";
        }

        var python = InterpreterFor(skill);
        if (python is null) return $"the environment for {skill.Name} was created but has no interpreter.";

        var arguments = new List<string> { "-m", "pip", "install", "--disable-pip-version-check", "--no-input" };
        arguments.AddRange(plan.Packages);

        var installed = await RunAsync(python, arguments, skill.Folder, timeout, cancellationToken).ConfigureAwait(false);
        if (installed is not null) return $"installing {string.Join(", ", plan.Packages)} failed: {installed}";

        try
        {
            File.WriteAllText(Path.Combine(skill.Folder, MarkerFile.Replace('/', Path.DirectorySeparatorChar)), Fingerprint(plan));
        }
        catch (IOException)
        {
            // Losing the marker only costs a repeated install next time.
        }

        return null;
    }

    private static string Fingerprint(DependencyPlan plan) =>
        string.Join("\n", plan.Packages.OrderBy(p => p, StringComparer.Ordinal));

    /// <summary>Runs a process, returning null on success or a short failure description.</summary>
    private static async Task<string?> RunAsync(
        string executable, IReadOnlyList<string> arguments, string workingDirectory,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start()) return $"{executable} could not be started.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return $"\"{executable}\" is not installed or not on PATH.";
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
            return $"still running after {timeout.TotalSeconds:0} seconds.";
        }

        if (process.ExitCode == 0) return null;

        var error = (await stderr.ConfigureAwait(false)).Trim();
        if (error.Length == 0) error = (await stdout.ConfigureAwait(false)).Trim();

        return error.Length <= 600 ? error : error[..600] + "…";
    }

    [GeneratedRegex(@"^\s*(?:import\s+(?<module>[A-Za-z_][\w.]*)|from\s+(?<module>[A-Za-z_][\w.]*)\s+import)",
        RegexOptions.Multiline)]
    private static partial Regex ImportStatement();
}
