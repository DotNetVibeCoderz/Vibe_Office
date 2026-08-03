using AutoWork.Agents;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Core.Skills;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The tools a skill actually reaches the agent through. The interesting cases are the ones
/// where a skill or the model asks for something it should not get.
/// </summary>
public sealed class SkillToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-skilltools", Guid.NewGuid().ToString("n")[..8]);
    private readonly string _workspace;
    private readonly FileSkillStore _store;

    public SkillToolTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);

        _store = new FileSkillStore(Path.Combine(_root, "skills"));

        _store.Install(
            """
            ---
            name: pdf
            description: Handle PDFs.
            ---

            Read reference.md, then run scripts/hello.py.
            """,
            "anthropics/skills · skills/pdf", "pdf",
            [
                ("reference.md", "The full reference lives here."u8.ToArray()),
                ("scripts/hello.py", "print('from the skill')"u8.ToArray()),
                ("assets/font.ttf", new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 }),
            ]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PermissionPolicy Policy(bool allowScripts) => new()
    {
        Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite }],
        AllowSkillScripts = allowScripts,

        // Approval is exercised separately; here the point is what the tool does once allowed.
        SkillScriptsRequireApproval = false,
    };

    private ToolContext Context(bool allowScripts) => new()
    {
        Guard = new PathGuard(Policy(allowScripts)),
        Approvals = new AutoApproveBroker(),
        Log = NullActionLog.Instance,
        Options = new AgentOptions(),
        RunId = "test",
        WorkingDirectory = _workspace,
    };

    private IReadOnlyList<ToolDescriptor> Tools(bool allowScripts)
    {
        var context = Context(allowScripts);
        return new SkillTools(context, _store).GetTools(context).ToArray();
    }

    private async Task<string> InvokeAsync(bool allowScripts, string tool, Dictionary<string, object?> arguments)
    {
        var function = Tools(allowScripts).Single(t => t.Name == tool).Function;
        var result = await function.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return result?.ToString() ?? "";
    }

    // ── What is offered ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A forbidden capability yields no tool at all, rather than one that refuses — the same rule
    /// the shell follows. Describing `skill_run` to a model that may never use it wastes tokens
    /// and invites it to keep trying.
    /// </summary>
    [Fact]
    public void The_run_tool_does_not_exist_until_the_permission_is_granted()
    {
        Assert.DoesNotContain(Tools(allowScripts: false), t => t.Name == "skill_run");
        Assert.Contains(Tools(allowScripts: true), t => t.Name == "skill_run");

        // Reading is always available; it cannot do anything.
        Assert.Contains(Tools(allowScripts: false), t => t.Name == "skill_file");
    }

    [Fact]
    public void Running_a_script_is_marked_as_needing_approval_and_as_a_system_level_action()
    {
        var run = Tools(allowScripts: true).Single(t => t.Name == "skill_run");

        Assert.Equal(ToolRisk.System, run.Risk);
        Assert.Equal(ApprovalKind.RunCommand, run.ApprovalKind);
    }

    // ── Reading bundled files ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole reason bundles exist: SKILL.md says "read reference.md", so reference.md has to
    /// be readable. Before bundling, that instruction pointed at nothing.
    /// </summary>
    [Fact]
    public async Task A_bundled_reference_file_can_be_read()
    {
        var result = await InvokeAsync(false, "skill_file", new() { ["skill"] = "pdf", ["path"] = "reference.md" });

        Assert.Contains("The full reference lives here.", result);
    }

    [Fact]
    public async Task Opening_a_skill_lists_what_shipped_with_it()
    {
        var result = await InvokeAsync(false, "skill_open", new() { ["name"] = "pdf" });

        Assert.Contains("Read reference.md", result);
        Assert.Contains("reference.md", result);
        Assert.Contains("scripts/hello.py", result);
    }

    /// <summary>Fonts and other binaries are bundled too; their bytes are useless to a model.</summary>
    [Fact]
    public async Task A_binary_bundled_file_is_reported_rather_than_dumped_into_context()
    {
        var result = await InvokeAsync(false, "skill_file", new() { ["skill"] = "pdf", ["path"] = "assets/font.ttf" });

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("binary", result);
    }

    [Fact]
    public async Task A_path_that_climbs_out_of_the_skill_folder_is_refused()
    {
        var result = await InvokeAsync(false, "skill_file",
            new() { ["skill"] = "pdf", ["path"] = "../../../config.json" });

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains("outside", result);
    }

    [Fact]
    public async Task Asking_for_a_file_that_is_not_there_says_what_is()
    {
        var result = await InvokeAsync(false, "skill_file", new() { ["skill"] = "pdf", ["path"] = "missing.md" });

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("reference.md", result);
    }

    // ── Running scripts ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A script path is held to the same containment rule as a read. Without it, `skill_run`
    /// would be a way to execute any file on the machine through a skill's name.
    /// </summary>
    [Fact]
    public async Task A_script_path_outside_the_skill_folder_is_refused()
    {
        var result = await InvokeAsync(true, "skill_run",
            new() { ["skill"] = "pdf", ["script"] = "../../../../Windows/System32/calc.exe" });

        Assert.StartsWith("REFUSED:", result);
    }

    /// <summary>
    /// An extension with no known interpreter is refused by name rather than handed to a shell
    /// to work out — guessing how to execute an unfamiliar file is the wrong instinct here.
    /// </summary>
    [Fact]
    public async Task A_file_type_with_no_interpreter_is_refused_and_says_what_can_run()
    {
        _store.Install("---\nname: odd\ndescription: d\n---\n\nBody.", "repo", "odd",
            [("run.exe", new byte[] { 0x4D, 0x5A })]);

        var result = await InvokeAsync(true, "skill_run", new() { ["skill"] = "odd", ["script"] = "run.exe" });

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains(".py", result);
    }

    [Fact]
    public async Task An_unknown_skill_is_reported_with_the_ones_that_are_installed()
    {
        var result = await InvokeAsync(true, "skill_run", new() { ["skill"] = "nope", ["script"] = "x.py" });

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("pdf", result);
    }

    /// <summary>
    /// The one test that proves the feature rather than its guardrails: a script that shipped
    /// with a skill runs, and its output comes back.
    ///
    /// Skipped where the interpreter is missing, because "python is not installed" is a fact
    /// about the machine and not a defect in AutoWork — and the tool says exactly that when it
    /// happens.
    /// </summary>
    [Fact]
    public async Task A_bundled_script_actually_runs_and_returns_its_output()
    {
        Assert.SkipWhen(!HasPython(), "Python is not on PATH. Skipped.");

        var result = await InvokeAsync(true, "skill_run",
            new() { ["skill"] = "pdf", ["script"] = "scripts/hello.py" });

        Assert.DoesNotContain("ERROR:", result);
        Assert.Contains("from the skill", result);
    }

    /// <summary>Arguments reach the script as arguments, including ones containing spaces.</summary>
    [Fact]
    public async Task Arguments_are_passed_through_without_being_re_parsed_by_a_shell()
    {
        Assert.SkipWhen(!HasPython(), "Python is not on PATH. Skipped.");

        _store.Install("---\nname: echo\ndescription: d\n---\n\nBody.", "repo", "echo",
            [("echo.py", "import sys; print('|'.join(sys.argv[1:]))"u8.ToArray())]);

        var result = await InvokeAsync(true, "skill_run", new()
        {
            ["skill"] = "echo",
            ["script"] = "echo.py",
            ["arguments"] = new[] { "a file with spaces.txt", "second\"quote" },
        });

        Assert.Contains("a file with spaces.txt|second\"quote", result);
    }

    /// <summary>
    /// The claim in full: a script that imports a library nobody installed still runs, because
    /// the import was resolved to a package and the package was fetched into the skill's own
    /// environment.
    ///
    /// Needs the network and a working `pip`, so it runs with the live suite rather than on every
    /// `dotnet test` — installing a wheel is not something an offline unit test should do.
    /// </summary>
    [Fact]
    public async Task A_script_whose_library_is_missing_gets_it_installed_and_then_runs()
    {
        Assert.SkipWhen(!HasPython(), "Python is not on PATH. Skipped.");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_API_KEY")),
            "Installs from PyPI; runs with the live suite. Skipped.");

        // A small, pure-Python package with no system dependencies, so the test is about
        // provisioning rather than about a toolchain.
        _store.Install("---\nname: needs-yaml\ndescription: d\n---\n\nBody.", "repo", "needs-yaml",
            [("dump.py", "import yaml; print(yaml.safe_dump({'ok': True}).strip())"u8.ToArray())]);

        var skill = _store.List().Single(s => s.Name == "needs-yaml");
        var plan = SkillDependencies.Plan(skill);

        Assert.Equal(["PyYAML"], plan.Packages);
        Assert.False(SkillDependencies.IsProvisioned(skill, plan));

        var result = await InvokeAsync(true, "skill_run", new() { ["skill"] = "needs-yaml", ["script"] = "dump.py" });

        Assert.DoesNotContain("ERROR:", result);
        Assert.Contains("ok: true", result);

        // Second time round it must not reinstall.
        Assert.True(SkillDependencies.IsProvisioned(_store.List().Single(s => s.Name == "needs-yaml"), plan));
    }

    /// <summary>
    /// The environment belongs to the skill, so removing the skill takes its libraries with it and
    /// the user's own Python is never touched.
    /// </summary>
    [Fact]
    public void An_installed_environment_lives_inside_the_skill_and_is_not_listed_as_its_content()
    {
        var skill = _store.List().Single(s => s.Name == "pdf");

        Directory.CreateDirectory(Path.Combine(skill.Folder, ".venv", "Scripts"));
        File.WriteAllText(Path.Combine(skill.Folder, ".venv", "Scripts", "marker.txt"), "x");

        var reloaded = _store.List().Single(s => s.Name == "pdf");

        Assert.DoesNotContain(reloaded.Files, f => f.StartsWith(".venv", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, reloaded.Files.Count);
    }

    private static bool HasPython()
    {
        try
        {
            using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                OperatingSystem.IsWindows() ? "python" : "python3", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            probe?.WaitForExit(10_000);
            return probe?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The switch is checked again inside the tool, not only when the tool set is built. A policy
    /// can change between a run being prepared and a call being made, and the switch has to be
    /// the last word rather than an advisory one.
    /// </summary>
    [Fact]
    public async Task The_permission_is_re_checked_when_the_call_is_made()
    {
        // Build the tool while scripts are allowed, then invoke it against a policy that forbids
        // them — which is exactly what a mid-run settings change looks like.
        var permissive = Context(allowScripts: true);
        var function = new SkillTools(permissive, _store).GetTools(permissive)
            .Single(t => t.Name == "skill_run").Function;

        permissive.Guard.Policy = Policy(allowScripts: false);

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["skill"] = "pdf", ["script"] = "scripts/hello.py" }),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("REFUSED:", result?.ToString() ?? "");
    }
}
