using System.Reflection;
using Microsoft.CodeAnalysis;
using VibeDesk.Scripting.Host;
using VibeDesk.Scripting.Sandbox;
using Xunit;

namespace VibeDesk.Tests.Scripting;

/// <summary>
/// The C# sandbox.
/// </summary>
/// <remarks>
/// These are regression tests for a hole that was live: because the guard's compilation did not use
/// the same <c>using</c> directives as the execution, a bare <c>Environment.Exit(1)</c> failed to
/// resolve, its symbol came back null, and the check skipped it — while the fully-qualified
/// <c>System.IO.File</c> was caught. It killed the host process. Both spellings are asserted here.
/// </remarks>
public class CSharpGuardTests
{
    private static readonly MetadataReference[] References =
    [
        .. new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly,
            typeof(WorkspaceApi).Assembly,
        }
        .Distinct()
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)),
    ];

    private static string? Inspect(string code) => CSharpGuard.Inspect(code, References);

    private static void Refused(string code) =>
        Assert.False(string.IsNullOrEmpty(Inspect(code)), $"should have been refused:\n{code}");

    private static void Allowed(string code) =>
        Assert.Null(Inspect(code));

    [Theory]
    [InlineData("System.IO.File.Delete(\"x\");")]
    [InlineData("File.Delete(\"x\");")]
    [InlineData("var s = new System.IO.StreamWriter(\"x\");")]
    [InlineData("System.IO.Directory.Delete(\"x\");")]
    public void RefusesFilesystemAccess(string code) => Refused(code);

    [Theory]
    [InlineData("Environment.Exit(1);")]                    // the one that killed the host
    [InlineData("System.Environment.Exit(1);")]
    [InlineData("var v = Environment.MachineName;")]
    public void RefusesEnvironment(string code) => Refused(code);

    [Theory]
    [InlineData("System.Diagnostics.Process.Start(\"cmd\");")]
    [InlineData("Process.Start(\"cmd\");")]
    public void RefusesProcessLaunch(string code) => Refused(code);

    [Theory]
    [InlineData("var t = Type.GetType(\"System.IO.File\");")]
    [InlineData("var a = AppDomain.CurrentDomain;")]
    [InlineData("var o = Activator.CreateInstance(typeof(object));")]
    [InlineData("var m = typeof(string).GetMethod(\"Trim\");")]
    public void RefusesReflection(string code) => Refused(code);

    [Theory]
    [InlineData("dynamic d = 1; d.Anything();")]
    [InlineData("unsafe { int* p = null; }")]
    [InlineData("using System.IO;\nvar x = 1;")]
    [InlineData("using System.Reflection;\nvar x = 1;")]
    public void RefusesAnalysisEscapes(string code) => Refused(code);

    [Theory]
    [InlineData("var c = new System.Net.Http.HttpClient();")]
    [InlineData("var t = new System.Threading.Thread(() => { });")]
    public void RefusesItsOwnNetworkAndThreading(string code) => Refused(code);

    [Fact]
    public void RefusalNamesWhatWasRefused()
    {
        var message = Inspect("Environment.Exit(1);");

        Assert.Contains("Environment", message);
    }

    [Theory]
    [InlineData("var xs = new List<int> { 3, 1, 2 }; return xs.OrderBy(x => x).Sum();")]
    [InlineData("var s = \"a,b,c\".Split(',').Select(x => x.Trim()).ToList(); return s.Count;")]
    [InlineData("return System.Text.RegularExpressions.Regex.IsMatch(\"abc\", \"^a\");")]
    [InlineData("var d = new Dictionary<string, int> { [\"a\"] = 1 }; return d[\"a\"];")]
    [InlineData("return DateTime.UtcNow.Year;")]
    [InlineData("return Math.Round(1.2345, 2);")]
    public void AllowsOrdinaryScriptCode(string code) => Allowed(code);

    [Fact]
    public void AllowsTheHostApiItself() =>
        Allowed("foreach (var f in Api.Drive.List(null, 5)) Log(f.Name); return 1;");

    [Fact]
    public void ALocalCalledPathIsTheAuthorsNotTheFrameworks()
    {
        // The symbol-free name check must not refuse a variable that merely shares a denied name,
        // or ordinary code becomes unwritable.
        Allowed("var path = \"a/b\"; return path.Length;");
        Allowed("var file = \"report.csv\"; Log(file); return 1;");
    }

    [Fact]
    public void ReportsSyntaxErrorsWithALineNumber()
    {
        var message = Inspect("var x = ;");

        Assert.Contains("Syntax error", message);
        Assert.Contains("line", message);
    }
}
