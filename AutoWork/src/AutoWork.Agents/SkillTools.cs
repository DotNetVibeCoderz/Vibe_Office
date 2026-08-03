using System.ComponentModel;
using System.Text;
using AutoWork.Core.Agents;
using AutoWork.Core.Skills;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

/// <summary>
/// Lets the agent reach for an installed skill.
///
/// Skills are loaded on demand rather than pasted into the system prompt. A dozen installed
/// skills would be tens of thousands of tokens on every single request, most of them irrelevant
/// to the job at hand. Instead the prompt carries only each skill's name and the one line saying
/// when it applies, and the model opens the one it needs — the same reason knowledge bases are
/// searched rather than attached wholesale.
/// </summary>
public sealed class SkillTools : ToolSetBase, IToolProvider
{
    private readonly ISkillStore _skills;

    public SkillTools(ToolContext context, ISkillStore skills) : base(context) => _skills = skills;

    protected override AgentOrgan Organ => AgentOrgan.Brain;

    public string Name => "Skills";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        // Nothing installed means nothing to describe. Advertising an empty catalogue would
        // invite the model to keep looking for skills that are not there.
        if (_skills.List().Count == 0) yield break;

        var tools = new SkillTools(context, _skills);

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.OpenSkill, "skill_open",
                "Read the full instructions of an installed skill. Open a skill before doing work " +
                "it covers, and follow what it says."),
            Organ = AgentOrgan.Brain,
            Risk = ToolRisk.Safe,
            Category = "Skills",
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.ListSkills, "skill_list",
                "List the installed skills with the situations each one covers."),
            Organ = AgentOrgan.Brain,
            Risk = ToolRisk.Safe,
            Category = "Skills",
        };
    }

    [Description("Read an installed skill.")]
    private Task<string> OpenSkill([Description("The skill's name, as shown by skill_list.")] string name)
        => GuardedAsync("skill.open", $"Open skill \"{name}\"", () =>
        {
            var available = _skills.List();

            var skill = available.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? available.FirstOrDefault(s => string.Equals(s.Id, FileSkillStore.ToId(name), StringComparison.OrdinalIgnoreCase));

            if (skill is null)
            {
                return Failed($"There is no skill called \"{name}\". Installed: " +
                              $"{string.Join(", ", available.Select(s => s.Name))}.");
            }

            return Ok($"# Skill: {skill.Name}\nSource: {skill.Source}\n\n{skill.Body}");
        });

    [Description("List installed skills.")]
    private Task<string> ListSkills()
        => GuardedAsync("skill.list", "List installed skills", () =>
        {
            var skills = _skills.List();
            if (skills.Count == 0) return Ok("No skills are installed.");

            var builder = new StringBuilder("Installed skills:\n");
            foreach (var skill in skills)
                builder.AppendLine($"  {skill.Name} — {Cap(skill.Description, 300)}");

            return Ok(builder.ToString());
        });

    /// <summary>
    /// The catalogue line for the system prompt: enough to decide whether a skill is relevant,
    /// not enough to be expensive.
    /// </summary>
    public static string Describe(IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0) return "";

        var builder = new StringBuilder();
        builder.AppendLine("Installed skills. When one covers the job, open it with skill_open and follow it:");

        foreach (var skill in skills)
            builder.AppendLine($"  {skill.Name}: {Trim(skill.Description, 260)}");

        return builder.ToString();
    }

    private static string Trim(string text, int max)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
