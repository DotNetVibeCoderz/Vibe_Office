using System.ComponentModel;
using AutoWork.Core.Agents;
using AutoWork.Core.Knowledge;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

/// <summary>
/// Cross-session memory. Knowledge bases are what let the agent know, on a Tuesday, what it
/// worked out on the previous Friday — the client's folder layout, the naming convention for
/// invoices, which spreadsheet is authoritative.
///
/// Writing is a deliberate act rather than an automatic one: an agent that silently records
/// everything it sees becomes a privacy problem, so it saves only when the user asks or when
/// it has learned something it was told to remember.
/// </summary>
public sealed class KnowledgeTools : ToolSetBase, IToolProvider
{
    private readonly IKnowledgeStore _store;

    public KnowledgeTools(ToolContext context, IKnowledgeStore store) : base(context) => _store = store;

    protected override AgentOrgan Organ => AgentOrgan.Brain;

    public string Name => "Knowledge";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(SearchAsync, "knowledge_search",
                "Search the user's saved notes from earlier sessions. Check here before asking the user something they may have already told you."),
            Organ = AgentOrgan.Brain,
            Risk = ToolRisk.Safe,
            Category = "Knowledge",
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(SaveAsync, "knowledge_save",
                "Save a fact worth remembering across sessions. Use this when the user asks you to remember something, or when you discover a durable fact about how their work is organised."),
            Organ = AgentOrgan.Brain,
            Risk = ToolRisk.Write,
            Category = "Knowledge",
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(ListAsync, "knowledge_list",
                "List the user's knowledge bases."),
            Organ = AgentOrgan.Brain,
            Risk = ToolRisk.Safe,
            Category = "Knowledge",
        };
    }

    [Description("Search saved notes.")]
    private Task<string> SearchAsync(
        [Description("What to look for.")] string query,
        [Description("How many results to return.")] int limit = 5)
        => GuardedAsync("knowledge.search", $"Search notes for \"{query}\"", async () =>
        {
            var hits = await _store.SearchAsync(query, Math.Clamp(limit, 1, 20)).ConfigureAwait(false);

            if (hits.Count == 0)
                return Ok($"Nothing saved matches \"{query}\".");

            var lines = hits.Select(h =>
                $"[{h.KnowledgeBaseName}] {h.Entry.Title} (relevance {h.Score:0.00})\n{h.Entry.Text}");

            return Ok(Cap(string.Join("\n\n", lines)));
        });

    [Description("Save a note for future sessions.")]
    private Task<string> SaveAsync(
        [Description("Knowledge base name. It is created if it does not exist.")] string knowledgeBase,
        [Description("Short title.")] string title,
        [Description("The fact to remember, written so it makes sense months from now.")] string text,
        [Description("Optional comma-separated tags.")] string? tags = null)
        => GuardedAsync("knowledge.save", $"Remember \"{title}\" in {knowledgeBase}", async () =>
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
                return Failed("A note needs both a title and some text.");

            var existing = _store.List().FirstOrDefault(kb =>
                string.Equals(kb.Name, knowledgeBase, StringComparison.OrdinalIgnoreCase));

            var target = existing ?? _store.Create(knowledgeBase);

            var parsedTags = (tags ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            await _store.AddEntryAsync(target.Id, title, text, source: $"run:{RunId}", parsedTags)
                .ConfigureAwait(false);

            return Ok($"Saved \"{title}\" to the {target.Name} knowledge base.");
        });

    [Description("List knowledge bases.")]
    private Task<string> ListAsync()
        => GuardedAsync("knowledge.list", "List knowledge bases", () =>
        {
            var bases = _store.List();

            if (bases.Count == 0)
                return Ok("There are no knowledge bases yet. knowledge_save will create one.");

            var lines = bases.Select(kb =>
                $"{kb.Name} — {kb.Entries.Count} note(s){(string.IsNullOrWhiteSpace(kb.Description) ? "" : $" — {kb.Description}")}");

            return Ok(string.Join('\n', lines));
        });
}
