using System.ComponentModel;
using System.Text;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
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

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.ReadSkillFile, "skill_file",
                "Read a file bundled with a skill — a reference document, a template, or a script's " +
                "source. Skill instructions often say \"see REFERENCE.md\"; this is how you see it."),
            Organ = AgentOrgan.Brain,
            Risk = ToolRisk.Safe,
            Category = "Skills",
        };

        if (!context.Guard.Policy.AllowSkillScripts) yield break;

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.RunSkillScriptAsync, "skill_run",
                "Run a script bundled with a skill. Read the script with skill_file first if you " +
                "are unsure what it does, and pass file paths as arguments."),
            Organ = AgentOrgan.Hands,

            // Someone else's code, running as the user. There is no honest lower rating.
            Risk = ToolRisk.System,
            Category = "Skills",
            ApprovalKind = ApprovalKind.RunCommand,
        };
    }

    [Description("Read an installed skill.")]
    private Task<string> OpenSkill([Description("The skill's name, as shown by skill_list.")] string name)
        => GuardedAsync("skill.open", $"Open skill \"{name}\"", () =>
        {
            if (Resolve(name) is not { } skill) return Failed(NotInstalled(name));

            var builder = new StringBuilder($"# Skill: {skill.Name}\nSource: {skill.Source}\n\n{skill.Body}");

            // Skill instructions routinely say "see REFERENCE.md" or "run scripts/fill.py".
            // Listing what actually shipped turns those references into something reachable
            // instead of a dead end.
            if (skill.Files.Count > 0)
            {
                builder.AppendLine().AppendLine().AppendLine("---")
                       .AppendLine("Files bundled with this skill — read one with skill_file:");

                foreach (var file in skill.Files.Take(60)) builder.AppendLine($"  {file}");

                if (skill.Files.Count > 60)
                    builder.AppendLine($"  … and {skill.Files.Count - 60} more");
            }

            return Ok(builder.ToString());
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

    [Description("Read a file bundled with a skill.")]
    private Task<string> ReadSkillFile(
        [Description("The skill's name.")] string skill,
        [Description("Path of the file inside the skill, e.g. \"reference.md\" or \"scripts/fill_form.py\".")] string path,
        [Description("Maximum characters to return.")] int maxCharacters = 12000)
        => GuardedAsync("skill.file", $"Read {path} from skill \"{skill}\"", () =>
        {
            if (Resolve(skill) is not { } found) return Failed(NotInstalled(skill));

            var target = FileSkillStore.Contain(found.Folder, path);
            if (target is null)
                return Refused($"\"{path}\" is outside the skill's folder.");

            if (!File.Exists(target))
            {
                return Failed($"{found.Name} has no file \"{path}\". It bundles: " +
                              $"{(found.Files.Count == 0 ? "nothing" : string.Join(", ", found.Files.Take(40)))}.");
            }

            if (new FileInfo(target).Length > 4L * 1024 * 1024)
                return Failed($"\"{path}\" is too large to read into context.");

            // Fonts and compiled assets are bundled too, and handing their bytes to a model is
            // pure waste. Say so instead.
            var text = File.ReadAllText(target);
            if (text.Contains('\0'))
                return Failed($"\"{path}\" is a binary file. It can be used by a script, but not read as text.");

            return Ok($"{found.Name}/{path}\n\n{Cap(text, maxCharacters)}");
        });

    /// <summary>
    /// Runs a script that came with a skill.
    ///
    /// This is the one place AutoWork executes code it did not write and the user did not type.
    /// The permission is separate from the shell's, every call is approval-gated by default, and
    /// the exact interpreter and argument list are shown in the consent card — so the decision
    /// being asked for is the real one, not a paraphrase of it.
    /// </summary>
    [Description("Run a script bundled with a skill.")]
    private Task<string> RunSkillScriptAsync(
        [Description("The skill's name.")] string skill,
        [Description("Path of the script inside the skill, e.g. \"scripts/fill_form.py\".")] string script,
        [Description("Arguments to pass to the script.")] string[]? arguments = null,
        [Description("Seconds to wait before giving up.")] int timeoutSeconds = 120)
    {
        var found = Resolve(skill);
        var target = found is null ? null : FileSkillStore.Contain(found.Folder, script);
        var runner = target is null ? null : ScriptRunner.For(target);

        // Worked out before the approval card is raised, so the packages that would be installed
        // are part of what the user is being asked to agree to — that list is the thing worth
        // reading, since a wrong name there runs code of its own at install time.
        var plan = found is not null && runner is not null && runner.UsesPython
            ? SkillDependencies.Plan(found)
            : new DependencyPlan();

        var willInstall = found is not null && !SkillDependencies.IsProvisioned(found, plan);

        var preview = runner is null
            ? script
            : $"{runner.Interpreter} {target} {string.Join(" ", arguments ?? [])}".TrimEnd();

        if (willInstall && !plan.IsEmpty)
        {
            preview += $"\n\nWill first install into an isolated environment for this skill " +
                       $"(from {plan.Source}):\n  {string.Join(", ", plan.Packages)}";
        }

        if (plan.Notes.Count > 0) preview += $"\n\nNote: {string.Join("; ", plan.Notes)}.";

        return GuardedAsync("skill.run", $"Run {script} from skill \"{skill}\"", async () =>
        {
            if (!Guard.Policy.AllowSkillScripts)
                return Refused("Running skill scripts is turned off in Settings › Permissions.");

            if (found is null) return Failed(NotInstalled(skill));

            if (target is null)
                return Refused($"\"{script}\" is outside the skill's folder.");

            if (!File.Exists(target))
                return Failed($"{found.Name} has no file \"{script}\".");

            if (runner is null)
            {
                return Refused($"\"{Path.GetExtension(script)}\" scripts cannot be run. " +
                               $"Supported: {string.Join(", ", ScriptRunner.SupportedExtensions)}.");
            }

            var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, Math.Max(1, Context.Options.ToolTimeoutSeconds)));

            if (willInstall && !plan.IsEmpty)
            {
                // Installing can take a while on a cold environment, so it gets its own budget
                // rather than eating the script's.
                var failure = await SkillDependencies
                    .ProvisionAsync(found, plan, runner.Interpreter, TimeSpan.FromMinutes(5), CancellationToken.None)
                    .ConfigureAwait(false);

                if (failure is not null) return Failed(failure);
            }

            // Use the skill's own environment when it has one, so its libraries are there and the
            // user's Python stays untouched.
            var interpreter = runner.UsesPython ? SkillDependencies.InterpreterFor(found) : null;

            var result = await runner
                .ExecuteAsync(target, arguments ?? [], Context.WorkingDirectory, timeout, interpreter)
                .ConfigureAwait(false);

            // A module that is genuinely unknown to us surfaces here rather than as a bare
            // traceback, with the name the user would need to act on.
            if (result.Contains("ModuleNotFoundError", StringComparison.Ordinal) && plan.Notes.Count > 0)
                result += $"\n\n{string.Join("; ", plan.Notes)}.";

            return result;
        },
        [Context.WorkingDirectory],
        Guard.Policy.SkillScriptsRequireApproval ? ApprovalKind.RunCommand : null,
        $"{preview}\n\nFrom skill: {found?.Name ?? skill} ({found?.Source})\n" +
        $"Working directory: {PathGuard.Describe(Context.WorkingDirectory)}");
    }

    private Skill? Resolve(string name)
    {
        var available = _skills.List();

        return available.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? available.FirstOrDefault(s => string.Equals(s.Id, FileSkillStore.ToId(name), StringComparison.OrdinalIgnoreCase));
    }

    private string NotInstalled(string name) =>
        $"There is no skill called \"{name}\". Installed: " +
        $"{string.Join(", ", _skills.List().Select(s => s.Name))}.";

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
