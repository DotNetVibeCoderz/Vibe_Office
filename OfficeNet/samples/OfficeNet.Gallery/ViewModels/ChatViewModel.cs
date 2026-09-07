// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OfficeNet.Gallery.Ai;

namespace OfficeNet.Gallery.ViewModels;

/// <summary>One turn in a conversation.</summary>
internal sealed partial class ChatTurn(bool fromUser, string text) : ObservableObject
{
    public bool FromUser { get; } = fromUser;

    public string Who => FromUser ? "You" : "Assistant";

    [ObservableProperty]
    public partial string Text { get; set; } = text;
}

/// <summary>
/// One conversation: its transcript, and the history the model actually sees.
/// </summary>
/// <remarks>
/// The two are kept apart because they are not the same thing. <see cref="Turns"/> is what the
/// window shows; <see cref="History"/> additionally carries the system prompt and is what gets
/// sent. Resetting a session clears the transcript but must not lose the system prompt.
/// </remarks>
internal sealed partial class ChatSession : ObservableObject
{
    private static int _counter;

    public ChatSession()
    {
        Title = $"Session {++_counter}";
        History = new ChatHistory(ChatProviders.SystemPrompt);
    }

    [ObservableProperty]
    public partial string Title { get; set; }

    public ObservableCollection<ChatTurn> Turns { get; } = [];

    public ChatHistory History { get; private set; }

    /// <summary>A short description for the session list.</summary>
    public string Subtitle
    {
        get
        {
            var asked = Turns.Count(t => t.FromUser);
            return asked == 0 ? "Empty" : $"{asked} question{(asked == 1 ? "" : "s")}";
        }
    }

    /// <summary>Empties the conversation, keeping the system prompt.</summary>
    public void Reset()
    {
        Turns.Clear();
        History = new ChatHistory(ChatProviders.SystemPrompt);
        Touch();
    }

    /// <summary>Names the session after its first question, so the list reads at a glance.</summary>
    public void TitleFrom(string question)
    {
        if (Turns.Count(t => t.FromUser) > 1)
        {
            return;
        }

        var text = question.Trim().ReplaceLineEndings(" ");
        Title = text.Length <= 42 ? text : text[..42].TrimEnd() + "…";
    }

    public void Touch() => OnPropertyChanged(nameof(Subtitle));

    public override string ToString() => Title;
}

/// <summary>An example question, offered as something to click rather than something to type.</summary>
internal sealed record PromptExample(string Group, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// The chatbot page: ask for OfficeNet code, in as many parallel conversations as you like.
/// </summary>
internal sealed partial class ChatViewModel : ObservableObject
{
    private Kernel? _kernel;
    private ProviderInfo? _built;
    private string? _builtModel;

    public ChatViewModel()
    {
        Providers = [.. ChatProviders.All];

        // Open on a provider the machine can actually use, so the first thing a person sees is a
        // usable state rather than a warning.
        SelectedProvider = Providers.FirstOrDefault(p => p.IsConfigured) ?? Providers[0];

        Sessions.Add(new ChatSession());
        SelectedSession = Sessions[0];
    }

    public ObservableCollection<ProviderInfo> Providers { get; }

    public ObservableCollection<ChatSession> Sessions { get; } = [];

    [ObservableProperty]
    public partial ChatSession? SelectedSession { get; set; }

    [ObservableProperty]
    public partial ProviderInfo SelectedProvider { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> Models { get; set; } = [];

    [ObservableProperty]
    public partial string? SelectedModel { get; set; }

    [ObservableProperty]
    public partial string Prompt { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>
    /// Example questions, grouped by what they exercise.
    /// </summary>
    /// <remarks>
    /// Deliberately concrete rather than generic prompts: each is a question someone actually
    /// arrives with, and between them they cover every library plus each of the four tools, so
    /// clicking through them is also a tour of what the assistant can reach.
    /// </remarks>
    public IReadOnlyList<PromptExample> Examples { get; } =
    [
        new("Word", "Build a report with a heading, a justified paragraph and a shaded table."),
        new("Word", "How do I put page numbers in the footer, and why do they all say 1?"),
        new("Word", "My find-and-replace does nothing. What am I missing?"),
        new("Word", "Fill a template with {{tokens}} from a dictionary."),

        new("Excel", "Why are my formula results zero everywhere except in Excel?"),
        new("Excel", "Write a sheet with Rupiah formatting, a SUM, and a colour scale."),
        new("Excel", "Read an .xlsx into a DataFrame and describe it."),
        new("Excel", "Export a worksheet to Indonesian-format CSV."),

        new("PowerPoint", "Add a native column chart to a slide and read its data back."),
        new("PowerPoint", "Turn HTML into a deck, splitting on every h2."),
        new("PowerPoint", "Why does my embedded video not play?"),
        new("PowerPoint", "Draw a rounded rectangle with a gradient fill and a shadow."),

        new("PDF", "Merge two PDFs, then split the result into single pages."),
        new("PDF", "Extract text from a scanned PDF — why is it empty?"),
        new("PDF", "Encrypt a PDF with AES-256 and allow printing only."),
        new("PDF", "Add a watermark and a highlight annotation to page one."),

        new("Tools", "Search the web for the current ECMA-376 revision and cite the source."),
        new("Tools", "What is today's date in Asia/Jakarta, and how many days until 2027-01-01?"),
        new("Tools", "What is the percentage change from 1,120 to 1,480?"),
        new("Tools", "Fetch a documentation page and summarise its first section."),
    ];

    /// <summary>
    /// The example groups, in the order they should appear.
    /// </summary>
    /// <remarks>
    /// Short labels on purpose: the full library names wrap the chip row onto two lines in the
    /// side panel, and "Word" is unambiguous next to "Excel" and "PowerPoint".
    /// </remarks>
    public IReadOnlyList<string> ExampleGroups { get; } =
        ["Word", "Excel", "PowerPoint", "PDF", "Tools"];

    [ObservableProperty]
    public partial string SelectedExampleGroup { get; set; } = "Word";

    /// <summary>The examples in the selected group.</summary>
    public IEnumerable<PromptExample> VisibleExamples =>
        Examples.Where(e => e.Group == SelectedExampleGroup);

    partial void OnSelectedExampleGroupChanged(string value) =>
        OnPropertyChanged(nameof(VisibleExamples));

    partial void OnSelectedProviderChanged(ProviderInfo value)
    {
        Models = [.. value.Models];
        SelectedModel = Models.FirstOrDefault();
        _kernel = null;

        Status = value.IsConfigured
            ? value.SupportsTools
                ? "Ready — web search, page fetch, date and maths tools available."
                : "Ready — this provider runs without tool calling in this sample."
            : value.MissingRequirement;
    }

    partial void OnSelectedModelChanged(string? value) => _kernel = null;

    // ---- Sessions ------------------------------------------------------------------------------

    /// <summary>Starts a new, empty conversation and switches to it.</summary>
    [RelayCommand]
    private void NewSession()
    {
        var session = new ChatSession();
        Sessions.Add(session);
        SelectedSession = session;
        Status = "New session.";
    }

    /// <summary>Removes a conversation. The last one is emptied rather than removed.</summary>
    /// <remarks>
    /// Leaving the list empty would give the composer nowhere to send to, and the next message
    /// would silently do nothing. Keeping one session alive is the simpler contract.
    /// </remarks>
    [RelayCommand]
    private void RemoveSession(ChatSession? session)
    {
        session ??= SelectedSession;

        if (session is null)
        {
            return;
        }

        if (Sessions.Count == 1)
        {
            session.Reset();
            Status = "Session emptied — this is the only one.";
            return;
        }

        var index = Sessions.IndexOf(session);
        Sessions.Remove(session);

        SelectedSession = Sessions[Math.Clamp(index, 0, Sessions.Count - 1)];
        Status = "Session removed.";
    }

    /// <summary>Empties the current conversation, keeping it selected.</summary>
    [RelayCommand]
    private void ResetSession()
    {
        SelectedSession?.Reset();
        Status = "Session reset.";
    }

    [RelayCommand]
    private void UseExample(PromptExample? example)
    {
        if (example is not null)
        {
            Prompt = example.Text;
        }
    }

    // ---- Sending -------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SendAsync()
    {
        var question = Prompt.Trim();

        if (question.Length == 0 || IsBusy || SelectedSession is not { } session)
        {
            return;
        }

        if (!SelectedProvider.IsConfigured)
        {
            Status = SelectedProvider.MissingRequirement;
            return;
        }

        Prompt = string.Empty;
        IsBusy = true;
        Status = $"Asking {SelectedModel}…";

        session.Turns.Add(new ChatTurn(fromUser: true, question));
        session.History.AddUserMessage(question);
        session.TitleFrom(question);
        session.Touch();

        var reply = new ChatTurn(fromUser: false, string.Empty);
        session.Turns.Add(reply);

        try
        {
            var kernel = EnsureKernel();
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            // Streaming so the answer appears as it is written. A code answer takes long enough
            // that a spinner alone makes the app feel broken.
            var builder = new StringBuilder();

            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                               session.History, BuildSettings(), kernel))
            {
                if (chunk.Content is { Length: > 0 } text)
                {
                    builder.Append(text);
                    reply.Text = builder.ToString();
                }
            }

            session.History.AddAssistantMessage(builder.ToString());
            Status = $"{SelectedProvider.Name} · {SelectedModel}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately broad. Each connector throws its own type — ClientResultException from
            // the OpenAI stack, HttpRequestException from the hand-written Anthropic service,
            // KernelException from SK itself — and a chat window that crashes the app because a
            // provider returned 400 is worse than one that prints what the provider said.
            reply.Text = ex.Message;
            Status = "Failed";
        }
        finally
        {
            IsBusy = false;
            session.Touch();
        }
    }

    private Kernel EnsureKernel()
    {
        if (_kernel is not null &&
            ReferenceEquals(_built, SelectedProvider) &&
            _builtModel == SelectedModel)
        {
            return _kernel;
        }

        _kernel = ChatProviders.Build(
            SelectedProvider,
            SelectedModel ?? SelectedProvider.Models[0],
            Environment.GetEnvironmentVariable("TAVILY_API_KEY"));

        _built = SelectedProvider;
        _builtModel = SelectedModel;

        return _kernel;
    }

    private PromptExecutionSettings? BuildSettings()
    {
        // Automatic function choice is what lets the model reach the plugins. The Anthropic service
        // in this sample does not implement tool calling, so asking for it there would advertise
        // something that never happens.
        if (!SelectedProvider.SupportsTools)
        {
            return null;
        }

        // Temperature is deliberately left unset. The reasoning-tuned models reject any value but
        // their default — gpt-5-mini answers a request carrying Temperature = 0.2 with
        // "HTTP 400 unsupported_value: temperature" — so pinning it here would make the provider
        // list quietly wrong for exactly the models people most want to try.
        return new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        };
    }
}
