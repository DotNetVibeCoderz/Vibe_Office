using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// Standing rules are the first thing in AutoWork that lets a decision outlive the moment it was
/// made, so these are written the way <c>SandboxTests</c> is: as attempts to get more out of a
/// rule than the user put into it.
/// </summary>
public sealed class ApprovalRuleTests
{
    private static ApprovalRequest Request(ApprovalKind kind, params string[] paths) =>
        new() { Title = "do the thing", Kind = kind, AffectedPaths = paths };

    private static string Temp(params string[] parts) =>
        Path.Combine([Path.GetTempPath(), "autowork-rules", .. parts]);

    // ── Deny ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_blanket_deny_refuses_that_kind_everywhere()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Deny, Kind = ApprovalKind.DeleteFiles },
        ]);

        Assert.Equal(ApprovalDecision.Denied,
            engine.Evaluate(Request(ApprovalKind.DeleteFiles, Temp("anywhere", "x.txt"))).Decision);

        // And leaves other kinds to be asked about as usual.
        Assert.Null(engine.Evaluate(Request(ApprovalKind.WriteFiles, Temp("anywhere", "x.txt"))).Decision);
    }

    [Fact]
    public void A_deny_scoped_to_a_folder_only_covers_that_folder()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Deny, Kind = ApprovalKind.WriteFiles, Path = Temp("private") },
        ]);

        Assert.Equal(ApprovalDecision.Denied,
            engine.Evaluate(Request(ApprovalKind.WriteFiles, Temp("private", "notes.txt"))).Decision);

        Assert.Null(engine.Evaluate(Request(ApprovalKind.WriteFiles, Temp("public", "notes.txt"))).Decision);
    }

    /// <summary>
    /// The user who wrote "never delete anything" and later added a broad allow meant the first
    /// to survive the second. Order in the list must not decide it either.
    /// </summary>
    [Fact]
    public void A_deny_beats_an_allow_whichever_order_they_are_in()
    {
        ApprovalRule deny = new() { Effect = RuleEffect.Deny, Kind = ApprovalKind.DeleteFiles };
        ApprovalRule allow = new() { Effect = RuleEffect.Allow, Kind = ApprovalKind.DeleteFiles, Path = Temp("scratch") };

        foreach (var rules in new[] { new[] { deny, allow }, [allow, deny] })
        {
            var verdict = new ApprovalRuleEngine(rules)
                .Evaluate(Request(ApprovalKind.DeleteFiles, Temp("scratch", "junk.txt")));

            Assert.Equal(ApprovalDecision.Denied, verdict.Decision);
        }
    }

    [Fact]
    public void One_denied_path_in_a_batch_denies_the_batch()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Deny, Kind = ApprovalKind.WriteFiles, Path = Temp("private") },
        ]);

        var verdict = engine.Evaluate(Request(ApprovalKind.WriteFiles,
            Temp("public", "a.txt"), Temp("private", "b.txt"), Temp("public", "c.txt")));

        Assert.Equal(ApprovalDecision.Denied, verdict.Decision);
    }

    // ── Allow ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_allow_scoped_to_a_folder_stops_the_asking_inside_it()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = Temp("projects") },
        ]);

        Assert.Equal(ApprovalDecision.Approved,
            engine.Evaluate(Request(ApprovalKind.WriteFiles, Temp("projects", "app", "main.cs"))).Decision);

        Assert.Null(engine.Evaluate(Request(ApprovalKind.WriteFiles, Temp("elsewhere", "main.cs"))).Decision);
    }

    /// <summary>
    /// The single most dangerous shape a rule could take: "allow this kind of thing, anywhere".
    /// It is refused at creation and, if one reaches the engine anyway, ignored.
    /// </summary>
    [Fact]
    public void An_allow_with_no_folder_is_rejected_and_never_takes_effect()
    {
        var rule = new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = "" };

        Assert.NotNull(rule.Validate());

        Assert.Null(new ApprovalRuleEngine([rule])
            .Evaluate(Request(ApprovalKind.WriteFiles, Temp("anywhere", "x.txt"))).Decision);
    }

    [Fact]
    public void An_allow_that_does_not_say_what_it_allows_is_rejected()
    {
        var rule = new ApprovalRule { Effect = RuleEffect.Allow, Kind = null, Path = Temp("projects") };

        Assert.NotNull(rule.Validate());
        Assert.Null(new ApprovalRuleEngine([rule])
            .Evaluate(Request(ApprovalKind.WriteFiles, Temp("projects", "x.txt"))).Decision);
    }

    /// <summary>
    /// Running a command and driving the keyboard cannot be scoped to a folder, so a rule
    /// allowing them would be a blanket surrender of the capability. Those stay per-action.
    /// </summary>
    [Theory]
    [InlineData(ApprovalKind.RunCommand)]
    [InlineData(ApprovalKind.ControlInput)]
    [InlineData(ApprovalKind.NetworkAccess)]
    [InlineData(ApprovalKind.CaptureScreen)]
    public void Only_writing_and_deleting_can_be_allowed_by_a_rule(ApprovalKind kind)
    {
        var rule = new ApprovalRule { Effect = RuleEffect.Allow, Kind = kind, Path = Temp("projects") };

        Assert.NotNull(rule.Validate());
        Assert.Null(new ApprovalRuleEngine([rule]).Evaluate(Request(kind, Temp("projects", "x"))).Decision);
    }

    [Fact]
    public void An_allow_only_applies_when_it_covers_every_path_in_the_request()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = Temp("projects") },
        ]);

        Assert.Null(engine.Evaluate(Request(ApprovalKind.WriteFiles,
            Temp("projects", "a.txt"), Temp("somewhere-else", "b.txt"))).Decision);
    }

    /// <summary>An action with no named paths is not something a folder rule can have covered.</summary>
    [Fact]
    public void An_allow_does_not_apply_to_a_request_with_no_paths()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = Temp("projects") },
        ]);

        Assert.Null(engine.Evaluate(Request(ApprovalKind.WriteFiles)).Decision);
    }

    /// <summary>The same segment-boundary trap PathGuard guards against.</summary>
    [Fact]
    public void A_rule_for_one_folder_does_not_cover_a_folder_whose_name_merely_starts_the_same()
    {
        var rule = new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = Temp("Proj") };

        Assert.True(rule.Covers(Temp("Proj", "a.txt")));
        Assert.False(rule.Covers(Temp("Proj-private", "a.txt")));
        Assert.False(rule.Covers(Temp("Projects", "a.txt")));
    }

    [Fact]
    public void A_relative_path_in_a_request_is_resolved_before_it_is_matched()
    {
        var rule = new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = Temp("projects") };

        Assert.False(rule.Covers(Path.Combine(Temp("projects"), "..", "elsewhere", "x.txt")));
    }

    [Fact]
    public void A_disabled_rule_does_nothing()
    {
        var engine = new ApprovalRuleEngine([
            new ApprovalRule { Effect = RuleEffect.Deny, Kind = ApprovalKind.DeleteFiles, Enabled = false },
        ]);

        Assert.Null(engine.Evaluate(Request(ApprovalKind.DeleteFiles, Temp("x"))).Decision);
    }

    // ── Through the coordinator ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_rule_answers_without_the_user_being_asked_and_says_which_rule_it_was()
    {
        var broker = new CountingBroker();
        ApprovalRule? applied = null;

        var coordinator = new ApprovalCoordinator(
            broker,
            new ApprovalRuleEngine([
                new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = Temp("projects") },
            ]),
            onRuleApplied: (rule, _, _) => applied = rule);

        var decision = await coordinator.RequestAsync(
            Request(ApprovalKind.WriteFiles, Temp("projects", "a.txt")), TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalDecision.Approved, decision);
        Assert.Equal(0, broker.Asked);
        Assert.NotNull(applied);
    }

    /// <summary>
    /// A standing "never" is the more deliberate statement, so it has to survive an
    /// "allow for this run" the user clicked earlier in the same run.
    /// </summary>
    [Fact]
    public async Task A_deny_rule_outlives_an_allow_for_this_run_granted_earlier()
    {
        var broker = new AlwaysAllowForRunBroker();

        var coordinator = new ApprovalCoordinator(
            broker,
            new ApprovalRuleEngine([
                new ApprovalRule { Effect = RuleEffect.Deny, Kind = ApprovalKind.WriteFiles, Path = Temp("private") },
            ]));

        // The user grants the kind for the run, on a path the rule does not cover.
        var first = await coordinator.RequestAsync(
            Request(ApprovalKind.WriteFiles, Temp("public", "a.txt")) with { RunId = "r" },
            TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalDecision.Approved, first);

        var second = await coordinator.RequestAsync(
            Request(ApprovalKind.WriteFiles, Temp("private", "b.txt")) with { RunId = "r" },
            TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalDecision.Denied, second);
    }

    [Fact]
    public async Task With_no_rules_the_coordinator_behaves_exactly_as_before()
    {
        var broker = new CountingBroker();
        var coordinator = new ApprovalCoordinator(broker);

        await coordinator.RequestAsync(Request(ApprovalKind.WriteFiles, Temp("x")), TestContext.Current.CancellationToken);

        Assert.Equal(1, broker.Asked);
    }

    private sealed class CountingBroker : IApprovalBroker
    {
        public int Asked { get; private set; }

        public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(ApprovalDecision.Approved);
        }
    }

    private sealed class AlwaysAllowForRunBroker : IApprovalBroker
    {
        public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ApprovalDecision.ApprovedForRun);
    }
}

/// <summary>
/// The invariant everything else rests on: a rule changes what the user is <em>asked</em>, never
/// what the sandbox <em>permits</em>. If this can be broken, standing rules are a hole in the
/// product's central claim rather than a convenience.
/// </summary>
public sealed class ApprovalRuleSandboxTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-rule-sandbox", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _granted;
    private readonly string _forbidden;

    public ApprovalRuleSandboxTests()
    {
        _granted = Path.Combine(_root, "granted");
        _forbidden = Path.Combine(_root, "forbidden");
        Directory.CreateDirectory(_granted);
        Directory.CreateDirectory(_forbidden);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_rule_allowing_a_folder_the_sandbox_does_not_grant_still_cannot_write_there()
    {
        var policy = new PermissionPolicy
        {
            // Only "granted" is reachable — "forbidden" is not, whatever any rule says.
            Roots = [new PermissionRoot { Path = _granted, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            ApprovalRules =
            [
                new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = _forbidden },
            ],
        };

        var context = new ToolContext
        {
            Guard = new PathGuard(policy),

            // Wrapped exactly as the orchestrator wraps it, so the rules are in play.
            Approvals = new ApprovalCoordinator(new DenyAllBroker(), new ApprovalRuleEngine(policy.ApprovalRules)),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "rules",
            WorkingDirectory = _granted,
        };

        var write = new FileTools(context).GetTools(context).First(t => t.Name == "files_write").Function;

        var target = Path.Combine(_forbidden, "should-not-exist.txt");

        var result = (await write.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = target, ["content"] = "nope" }),
            TestContext.Current.CancellationToken))?.ToString() ?? "";

        // The rule got past the consent prompt. The sandbox still said no.
        Assert.StartsWith("REFUSED:", result);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task A_rule_allowing_a_granted_folder_writes_without_a_prompt()
    {
        var policy = new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _granted, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            ApprovalRules =
            [
                new ApprovalRule { Effect = RuleEffect.Allow, Kind = ApprovalKind.WriteFiles, Path = _granted },
            ],
        };

        var context = new ToolContext
        {
            Guard = new PathGuard(policy),

            // DenyAll stands in for the user: if anything reaches the prompt, the write fails.
            Approvals = new ApprovalCoordinator(new DenyAllBroker(), new ApprovalRuleEngine(policy.ApprovalRules)),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "rules",
            WorkingDirectory = _granted,
        };

        var write = new FileTools(context).GetTools(context).First(t => t.Name == "files_write").Function;
        var target = Path.Combine(_granted, "allowed.txt");

        await write.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = target, ["content"] = "written" }),
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(target));
        Assert.Equal("written", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }
}
