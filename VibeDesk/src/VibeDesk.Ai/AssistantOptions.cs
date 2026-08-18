using VibeDesk.Domain;

namespace VibeDesk.Ai;

/// <summary>
/// The <c>Assistant</c> section of appsettings. Every provider is optional: a provider with no
/// credentials is simply absent from the picker rather than failing at runtime, which is what lets
/// the app ship with an empty key file and still start.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>Default provider when a chat session has no override.</summary>
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;

    public double Temperature { get; set; } = 0.4;

    /// <summary>
    /// Output cap per reply. On models that think by default (Claude Opus 5 and up) this budget covers
    /// reasoning *and* the visible answer, so it is deliberately generous rather than chat-sized.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>How many prior turns are replayed. Older turns are dropped, not summarised.</summary>
    public int MaxHistoryTurns { get; set; } = 20;

    public string SystemPrompt { get; set; } = "You are Mr Clippy, a helpful assistant inside VibeDesk.";

    /// <summary>Cap on characters pulled into context by the read/scrape tools.</summary>
    public int MaxToolResultChars { get; set; } = 12_000;

    /// <summary>Maximum tool round trips before the assistant is forced to answer.</summary>
    public int MaxToolIterations { get; set; } = 6;

    public string TimeZoneId { get; set; } = "Asia/Jakarta";

    /// <summary>
    /// Lets the assistant create and edit the user's documents, spreadsheets, presentations and
    /// folders, not just read them.
    /// </summary>
    /// <remarks>
    /// Even when enabled, nothing the assistant can call deletes: there is no trash or delete tool,
    /// so the worst a misread instruction produces is a stray file. Set false to keep the assistant
    /// strictly read-only.
    /// </remarks>
    public bool AllowWorkspaceWrites { get; set; } = true;

    public OpenAIOptions OpenAI { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public TavilyOptions Tavily { get; set; } = new();

    public sealed class OpenAIOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
        public List<string> Models { get; set; } = [];
        public string? Endpoint { get; set; }
    }

    public sealed class AnthropicOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "claude-sonnet-5";
        public List<string> Models { get; set; } = [];
    }

    public sealed class GoogleOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gemini-2.5-flash";
        public List<string> Models { get; set; } = [];
    }

    /// <summary>
    /// Ollama needs no key — it is considered available whenever an endpoint is configured, so a local
    /// model is a working out-of-the-box option for anyone who does not want to pay for tokens.
    /// </summary>
    public sealed class OllamaOptions
    {
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "llama3.2";
        public List<string> Models { get; set; } = [];
    }

    public sealed class TavilyOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Endpoint { get; set; } = "https://api.tavily.com/search";
        public int MaxResults { get; set; } = 5;
    }
}
