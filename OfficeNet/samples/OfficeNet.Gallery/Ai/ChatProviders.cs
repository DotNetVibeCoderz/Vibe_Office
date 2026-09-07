// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace OfficeNet.Gallery.Ai;

/// <summary>Which model provider the chat talks to.</summary>
internal enum ChatProvider
{
    OpenAI,
    AzureOpenAI,
    Anthropic,
    Gemini,
    DeepSeek,
}

/// <summary>A provider and the models it offers.</summary>
internal sealed record ProviderInfo(
    ChatProvider Provider,
    string Name,
    string EnvironmentVariable,
    IReadOnlyList<string> Models,
    bool SupportsTools,
    string? EndpointVariable = null)
{
    public string Key => Environment.GetEnvironmentVariable(EnvironmentVariable) ?? string.Empty;

    /// <summary>The endpoint, for providers that need one. Azure names a resource; DeepSeek a host.</summary>
    public string Endpoint =>
        EndpointVariable is null
            ? string.Empty
            : Environment.GetEnvironmentVariable(EndpointVariable) ?? string.Empty;

    /// <summary>A provider needs its key, and its endpoint when it declares one.</summary>
    public bool IsConfigured =>
        Key.Length > 0 && (EndpointVariable is null || Endpoint.Length > 0);

    /// <summary>What is missing, phrased so it can be shown to a person as-is.</summary>
    public string MissingRequirement =>
        Key.Length == 0
            ? $"Set {EnvironmentVariable} to use {Name}."
            : $"Set {EndpointVariable} to use {Name}.";

    public override string ToString() => Name;
}

/// <summary>
/// Builds the Semantic Kernel for a chosen provider.
/// </summary>
/// <remarks>
/// <para>
/// Keys come from environment variables, never from a file in the repository. A sample that asks
/// you to paste a key into a text box teaches the wrong habit, and a sample that ships one is a
/// leak.
/// </para>
/// <para>
/// DeepSeek is OpenAI-compatible, so it uses the OpenAI connector pointed at a different endpoint.
/// Azure uses its own connector and names a deployment rather than a model. Gemini's connector is
/// alpha and needs the experimental diagnostics suppressed. Anthropic has no official connector at
/// all — see <see cref="AnthropicChatCompletionService"/>.
/// </para>
/// </remarks>
internal static class ChatProviders
{
    public static IReadOnlyList<ProviderInfo> All { get; } =
    [
        new(ChatProvider.OpenAI, "OpenAI", "OPENAI_API_KEY",
            ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini"], SupportsTools: true),

        // Azure names a *deployment*, not a model: the string here is whatever the deployment was
        // called in the portal, which is often but not always the model name.
        new(ChatProvider.AzureOpenAI, "Azure OpenAI", "AZURE_OPENAI_API_KEY",
            ["gpt-5-mini", "gpt-4o", "gpt-4o-mini"], SupportsTools: true,
            EndpointVariable: "AZURE_OPENAI_ENDPOINT"),

        new(ChatProvider.Anthropic, "Anthropic", "ANTHROPIC_API_KEY",
            ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5-20251001"], SupportsTools: false),

        new(ChatProvider.Gemini, "Google Gemini", "GEMINI_API_KEY",
            ["gemini-2.0-flash", "gemini-1.5-pro"], SupportsTools: true),

        new(ChatProvider.DeepSeek, "DeepSeek", "DEEPSEEK_API_KEY",
            ["deepseek-v4-flash", "deepseek-chat", "deepseek-reasoner"], SupportsTools: true),
    ];

    public static Kernel Build(ProviderInfo provider, string model, string? tavilyKey)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!provider.IsConfigured)
        {
            throw new InvalidOperationException(provider.MissingRequirement);
        }

        var builder = Kernel.CreateBuilder();

        switch (provider.Provider)
        {
            case ChatProvider.OpenAI:
                builder.AddOpenAIChatCompletion(model, provider.Key);
                break;

            case ChatProvider.AzureOpenAI:
                builder.AddAzureOpenAIChatCompletion(model, provider.Endpoint, provider.Key);
                break;

            case ChatProvider.DeepSeek:
                builder.AddOpenAIChatCompletion(
                    model, new Uri("https://api.deepseek.com"), provider.Key);
                break;

            case ChatProvider.Gemini:
#pragma warning disable SKEXP0070 // The Google connector is alpha; this is the sanctioned opt-in.
                builder.AddGoogleAIGeminiChatCompletion(model, provider.Key);
#pragma warning restore SKEXP0070
                break;

            case ChatProvider.Anthropic:
                builder.Services.AddSingleton<IChatCompletionService>(
                    new AnthropicChatCompletionService(model, provider.Key));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }

        var kernel = builder.Build();

        kernel.Plugins.AddFromObject(new SearchPlugin(tavilyKey), "Search");
        kernel.Plugins.AddFromObject(new ScrapePlugin(), "Web");
        kernel.Plugins.AddFromObject(new TimePlugin(), "Time");
        kernel.Plugins.AddFromObject(new MathPlugin(), "Math");

        return kernel;
    }

    /// <summary>The system prompt: what the assistant is for, and what it must not invent.</summary>
    public const string SystemPrompt = """
        You help developers write .NET code that uses OfficeNet — a set of .NET 10 libraries for
        Word, Excel, PowerPoint and PDF, by Gravicode Studios.

        The libraries and their entry points:
          WordNet        WordDocument.Create/Open      .docx
          ExcelNet       Workbook.Create/Open          .xlsx
          PowerPointNet  Presentation.Create/Open      .pptx
          PdfNet         PdfDocument.Create/Open       PDF
          OfficeNet      Office.DetectFormat/Open/ExtractText/ConvertToPdf
          OfficeNet.Rendering  DocumentRenderer.Render/RenderThumbnail

        Things that are true and that people get wrong:
          - ExcelNet: call workbook.Recalculate() before saving, or every consumer other than Excel
            reads the cached formula results as zero.
          - Lengths are a Length struct built from Units.Cm/Inches/Pt/Twips, never a raw number.
          - Word run formatting is tri-state: null inherits, false is explicitly off.
          - PdfCanvas.TopDown = true makes the canvas measure from the top, like Office does.
          - Rendering lives in a separate package because it is the only part needing a native
            dependency.

        When you are unsure whether an API exists, say so rather than inventing a plausible name.
        Prefer short, complete, compilable examples. Use the tools you have for anything that needs
        today's date, arithmetic, or information from the web.
        """;
}
