using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Knowledge;
using AutoWork.Core.Skills;
using AutoWork.Providers;
using AutoWork.Tools;

namespace AutoWork.Agents;

/// <summary>
/// Assembles the agent's capabilities for a run.
///
/// Tool availability is decided here rather than at call time: a provider whose capability the
/// permission policy forbids returns nothing, so a forbidden tool is never described to the
/// model at all. Advertising a tool and then refusing every call wastes tokens and leads the
/// model to keep trying.
/// </summary>
public sealed class ToolRegistry
{
    private readonly ModelClientFactory _factory;
    private readonly IKnowledgeStore _knowledge;
    private readonly ISkillStore? _skills;
    private readonly IReadOnlyList<IToolProvider> _extra;

    public ToolRegistry(
        ModelClientFactory factory,
        IKnowledgeStore knowledge,
        IEnumerable<IToolProvider>? additionalProviders = null,
        ISkillStore? skills = null)
    {
        _factory = factory;
        _knowledge = knowledge;
        _extra = additionalProviders?.ToArray() ?? [];

        // Optional so tests and headless callers need not build a store; without one the agent
        // simply has no skills, which is the same as having none installed.
        _skills = skills;
    }

    public IReadOnlyList<ToolDescriptor> Build(ToolContext context, ModelProfile? visionModel)
    {
        var providers = new List<IToolProvider>
        {
            new FileTools(context),
            new DocumentTools(context),
            new DataTools(context),
            new ImageTools(context),
            new ScreenTools(context),
            new ShellTools(context),
            new InputTools(context),
            new WebTools(context),
            new MeetingTools(context),
            new BrowserTools(context),
            new VisionTools(context, _factory, visionModel),
            new KnowledgeTools(context, _knowledge),
        };

        if (_skills is not null) providers.Add(new SkillTools(context, _skills));

        providers.AddRange(_extra);

        var tools = new List<ToolDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            foreach (var tool in provider.GetTools(context))
            {
                // First registration wins, so an integration cannot shadow a core tool.
                if (seen.Add(tool.Name)) tools.Add(tool);
            }
        }

        return tools;
    }

    /// <summary>Everything that could exist, ignoring the current policy — for Settings.</summary>
    public IReadOnlyList<string> DescribeAllCategories() =>
        ["Files", "Documents", "Data", "Images", "Screen", "Shell", "Input control", "Web", "Knowledge", "Skills", "MCP"];
}
