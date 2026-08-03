using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Providers;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace AutoWork.Tests;

/// <summary>
/// The product's headline use case, end to end and for real: research a topic on the internet,
/// then hand back a Word document and a PowerPoint deck.
///
/// This is the test that exercises everything at once — search, fetch, planning across several
/// steps, two different document generators — so it is also the one most likely to catch a
/// regression that unit tests cannot see. It needs the same <c>AUTOWORK_LIVE_*</c> variables as
/// <see cref="LiveProviderTests"/>, plus <c>AUTOWORK_LIVE_TAVILY_KEY</c> for good search
/// results; without the latter it still runs, on the keyless fallback chain.
///
/// Assertions are about artefacts, not prose: the files exist, they pass the real OOXML
/// validator, and they contain the topic. What the model wrote about it is its business.
/// </summary>
public sealed class LiveResearchTests : IDisposable
{
    /// <summary>
    /// A stable copy of the events seen so far. `Progress&lt;T&gt;` delivers on another thread, so a
    /// callback can still arrive while the assertions enumerate — which is exactly how one live
    /// run failed with "Collection was modified".
    /// </summary>
    private static RunEvent[] Snapshot(List<RunEvent> events)
    {
        lock (events) return [.. events];
    }

    private const string Topic = "retrieval augmented generation";

    private readonly string _sandbox;

    public LiveResearchTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "autowork-research", Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        // A structurally valid document can still be a useless one, so leave the artefacts
        // behind when someone wants to open them: AUTOWORK_LIVE_KEEP_OUTPUT=1.
        if (Environment.GetEnvironmentVariable("AUTOWORK_LIVE_KEEP_OUTPUT") is { Length: > 0 })
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Artefacts kept in {_sandbox}");
            return;
        }

        try { Directory.Delete(_sandbox, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Research_from_the_internet_becomes_a_word_document_and_a_powerpoint_deck()
    {
        var (factory, profile) = LiveModel.Require();

        var config = new ConfigStore(Path.Combine(_sandbox, "config.json"));
        config.Save(new AutoWorkConfig
        {
            Models = [profile],
            Permissions = new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _sandbox, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowNetwork = true,
                AllowDelete = false,
                AllowShell = false,
                AllowScreenCapture = false,
                AllowInputControl = false,
            },
            Agent = new AgentOptions
            {
                PlannerModelId = profile.Id,
                ExecutorModelId = profile.Id,
                MaxSteps = 12,
                EnableSubAgents = false,
                ConfirmDestructiveActions = false,
            },
        });

        var knowledge = new JsonKnowledgeStore(directory: Path.Combine(_sandbox, ".knowledge"));

        var orchestrator = new AgentOrchestrator(
            config, factory, new ToolRegistry(factory, knowledge), NullActionLog.Instance,
            new AutoApproveBroker(), knowledge, LiveSecrets.ForSearch());

        var events = new List<RunEvent>();

        var result = await orchestrator.RunAsync(
            $"""
             Research the topic "{Topic}" using web search, then write it up.

             Produce exactly two files in the folder {_sandbox}:
               1. report.docx  — a short Word report. A title, then three or four short sections.
               2. deck.pptx    — a short PowerPoint deck of three or four slides covering the same ground.

             Keep both brief. Mention "{Topic}" by name in each file. Base the content on what the
             search actually returned rather than on memory.
             """,
            new Progress<RunEvent>(e => { lock (events) events.Add(e); }),
            TestContext.Current.CancellationToken);

        var searched = Snapshot(events).OfType<ToolCallEvent>().Any(e => e.Tool == "web_search" && e.Result is not null);
        var toolsUsed = string.Join(", ", Snapshot(events).OfType<ToolCallEvent>().Select(e => e.Tool).Distinct());

        Assert.True(searched, $"The run never searched the web. Tools used: {toolsUsed}. Summary: {result.Summary}");

        AssertWordReport(Path.Combine(_sandbox, "report.docx"), result, toolsUsed);
        AssertPowerPointDeck(Path.Combine(_sandbox, "deck.pptx"), result, toolsUsed);
    }

    private static void AssertWordReport(string path, RunResult result, string toolsUsed)
    {
        Assert.True(File.Exists(path), Explain("report.docx", path, result, toolsUsed));

        using var document = WordprocessingDocument.Open(path, false);

        AssertValid(new OpenXmlValidator().Validate(document, TestContext.Current.CancellationToken));

        var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? "";

        Assert.True(text.Length > 200, $"report.docx holds only {text.Length} characters of text.");
        Assert.Contains(Topic, Normalise(text), StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPowerPointDeck(string path, RunResult result, string toolsUsed)
    {
        Assert.True(File.Exists(path), Explain("deck.pptx", path, result, toolsUsed));

        using var presentation = PresentationDocument.Open(path, false);

        AssertValid(new OpenXmlValidator().Validate(presentation, TestContext.Current.CancellationToken));

        var slides = presentation.PresentationPart?.SlideParts.ToList() ?? [];

        Assert.True(slides.Count >= 3, $"The deck has {slides.Count} slide(s); at least three were asked for.");

        var text = string.Join(" ", slides.Select(s => s.Slide?.InnerText ?? ""));
        Assert.Contains(Topic, Normalise(text), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Retrieval-Augmented Generation" and "retrieval augmented generation" are the same topic.
    /// A deck was once rejected purely for hyphenating its own title, which tested punctuation
    /// rather than whether the research landed.
    /// </summary>
    private static string Normalise(string text) => text.Replace('-', ' ').Replace('‑', ' ');

    /// <summary>A missing file is the interesting failure, so say what the run actually did.</summary>
    private static string Explain(string name, string path, RunResult result, string toolsUsed) =>
        $"""
         {name} was not created at {path}.
         Run status: {result.Status}
         Tools used: {toolsUsed}
         Summary: {result.Summary}
         """;

    private static void AssertValid(IEnumerable<ValidationErrorInfo> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0) return;

        var detail = string.Join("\n", list.Take(10).Select(e => $"{e.Path?.XPath}: {e.Description}"));
        Assert.Fail($"OpenXML validation reported {list.Count} problem(s):\n{detail}");
    }
}

/// <summary>
/// Supplies a Tavily key to a live run without touching the real secret store, so running the
/// tests leaves no credential on disk.
/// </summary>
internal static class LiveSecrets
{
    internal static ISecretStore? ForSearch()
    {
        var key = Environment.GetEnvironmentVariable("AUTOWORK_LIVE_TAVILY_KEY");

        // Null is a valid answer: web_search then runs on its keyless backends, which is exactly
        // what a user who has not signed up for anything gets.
        return string.IsNullOrWhiteSpace(key) ? null : new InlineSecretStore("tavily", key);
    }

    private sealed class InlineSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values;

        public InlineSecretStore(string name, string value) =>
            _values = new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value };

        public string? Resolve(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;

            // The default config points at env:TAVILY_API_KEY; map any reference to the one key
            // we were given so the test does not depend on how the config happens to name it.
            return reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
                ? Environment.GetEnvironmentVariable(reference[4..]) ?? _values.Values.FirstOrDefault()
                : Get(reference) ?? _values.Values.FirstOrDefault();
        }

        public string? Get(string name) => _values.GetValueOrDefault(name);
        public void Set(string name, string value) => _values[name] = value;
        public void Delete(string name) => _values.Remove(name);
        public IReadOnlyCollection<string> Names => _values.Keys;
    }
}
