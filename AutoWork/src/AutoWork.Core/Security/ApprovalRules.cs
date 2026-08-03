namespace AutoWork.Core.Security;

public enum RuleEffect
{
    /// <summary>Refuse without asking.</summary>
    Deny = 0,
    /// <summary>Permit without asking.</summary>
    Allow = 1,
}

/// <summary>
/// A standing answer to a class of consent prompt, so common work does not become a queue of
/// clicks — "always allow writes under ~/Projects", "never allow deletes".
/// </summary>
public sealed class ApprovalRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Deny by default: a rule created half-configured must not grant anything.</summary>
    public RuleEffect Effect { get; set; } = RuleEffect.Deny;

    /// <summary>Null matches every kind. Only meaningful for a deny — see <see cref="Validate"/>.</summary>
    public ApprovalKind? Kind { get; set; }

    /// <summary>Folder the rule covers. Empty means "anywhere", which only a deny may say.</summary>
    public string Path { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>The user's own words about why this exists. Shown in Settings, never interpreted.</summary>
    public string Note { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Kinds an allow rule may cover.
    ///
    /// Deliberately only the two that are about a place on disk. "Always allow running commands"
    /// or "always allow driving the keyboard" cannot be scoped to anything, so a rule granting
    /// them would be a blanket surrender of the capability — and the capability switches in
    /// Permissions already express that choice, visibly, in one place.
    /// </summary>
    public static readonly ApprovalKind[] AllowableKinds = [ApprovalKind.WriteFiles, ApprovalKind.DeleteFiles];

    /// <summary>Null when the rule is usable; otherwise why it is not.</summary>
    public string? Validate()
    {
        if (Effect != RuleEffect.Allow) return null;

        // An allow with no path is "approve this kind of thing anywhere", which is the one shape
        // that would quietly undo the consent model.
        if (string.IsNullOrWhiteSpace(Path))
            return "An allow rule has to name a folder. Without one it would approve this action anywhere.";

        if (Kind is null)
            return "An allow rule has to say what it allows.";

        if (!AllowableKinds.Contains(Kind.Value))
            return "Only writing and deleting can be allowed by a rule. Running commands and controlling input are asked about every time.";

        return null;
    }

    public bool Matches(ApprovalKind kind) => Kind is null || Kind.Value == kind;

    /// <summary>One line, for the action log and for Settings.</summary>
    public override string ToString()
    {
        var what = Kind?.ToString() ?? "anything";
        var where = string.IsNullOrWhiteSpace(Path) ? "anywhere" : PathGuard.Describe(Path);

        return Effect == RuleEffect.Allow
            ? $"always allow {what} in {where}"
            : $"never allow {what} in {where}";
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is inside this rule's folder.
    ///
    /// Compared at a segment boundary, for the same reason <c>PathGuard</c> does it: a plain
    /// prefix test lets a rule for <c>~/Proj</c> silently cover <c>~/Projects-private</c>.
    /// </summary>
    public bool Covers(string candidate)
    {
        if (string.IsNullOrWhiteSpace(Path)) return true;
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        string root, full;

        try
        {
            root = System.IO.Path.GetFullPath(Path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            full = System.IO.Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return full.Equals(root, comparison)
            || full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, comparison);
    }
}

public sealed record RuleVerdict(ApprovalDecision? Decision, ApprovalRule? Rule)
{
    public static readonly RuleVerdict Ask = new(null, null);
}

/// <summary>
/// Applies the user's standing rules to a consent request.
///
/// Two properties hold this together, and both are load-bearing:
///
/// <list type="number">
/// <item>
/// <b>Deny always wins.</b> Every matching rule is considered, not the first one found. A user
/// who wrote "never delete anything" and later added "allow everything under ~/Scratch" meant
/// the first to survive the second.
/// </item>
/// <item>
/// <b>A rule narrows what is asked, never what is permitted.</b> An allowed request still goes
/// through <c>PathGuard</c> when it runs, so a rule cannot reach outside the granted folders. The
/// worst a bad allow rule can do is stop the user being asked about something the sandbox was
/// always going to permit.
/// </item>
/// </list>
/// </summary>
public sealed class ApprovalRuleEngine
{
    private readonly Func<IReadOnlyList<ApprovalRule>> _rules;

    public ApprovalRuleEngine(Func<IReadOnlyList<ApprovalRule>> rules) => _rules = rules;

    public ApprovalRuleEngine(IReadOnlyList<ApprovalRule> rules) : this(() => rules) { }

    public RuleVerdict Evaluate(ApprovalRequest request)
    {
        var rules = _rules().Where(r => r.Enabled).ToList();

        foreach (var rule in rules.Where(r => r.Effect == RuleEffect.Deny))
        {
            if (!rule.Matches(request.Kind)) continue;

            // Any single affected path inside a denied folder denies the whole request: a batch
            // is one action, and half-performing it is not something the user asked for.
            // A rule with no path denies the kind outright.
            if (string.IsNullOrWhiteSpace(rule.Path) || request.AffectedPaths.Any(rule.Covers))
                return new RuleVerdict(ApprovalDecision.Denied, rule);
        }

        foreach (var rule in rules.Where(r => r.Effect == RuleEffect.Allow))
        {
            if (rule.Validate() is not null) continue;
            if (!rule.Matches(request.Kind)) continue;

            // Every path has to be covered. A request touching five files where the rule covers
            // three is not a request the user pre-approved.
            if (request.AffectedPaths.Count > 0 && request.AffectedPaths.All(rule.Covers))
                return new RuleVerdict(ApprovalDecision.Approved, rule);
        }

        return RuleVerdict.Ask;
    }
}
