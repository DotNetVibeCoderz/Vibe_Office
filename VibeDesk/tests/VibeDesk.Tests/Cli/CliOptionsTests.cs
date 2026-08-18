using VibeDesk.Cli;
using VibeDesk.Domain;
using Xunit;

namespace VibeDesk.Tests.Cli;

public class CliOptionsTests
{
    [Fact]
    public void FirstNonFlagArgumentIsTheTarget()
    {
        var options = CliOptions.Parse(["report.py", "--scope", "SheetsRead", "other.py"]);

        // Only the first: a second bare argument is a mistake, and silently switching targets to it
        // would run the wrong file.
        Assert.Equal("report.py", options.Target);
    }

    [Fact]
    public void ScopesAccumulate()
    {
        var options = CliOptions.Parse(["x.js", "--scope", "SheetsRead", "-s", "DriveWrite"]);

        Assert.Equal(ScriptScope.SheetsRead | ScriptScope.DriveWrite, options.Scopes);
        Assert.True(options.ScopesGiven);
    }

    [Fact]
    public void ScopeNamesAreCaseInsensitive()
    {
        var options = CliOptions.Parse(["x.js", "--scope", "sheetsread"]);

        Assert.Equal(ScriptScope.SheetsRead, options.Scopes);
    }

    [Theory]
    [InlineData("read", ScriptScope.ReadOnly)]
    [InlineData("readonly", ScriptScope.ReadOnly)]
    [InlineData("all", ScriptScope.All)]
    public void ShorthandScopesExpand(string name, ScriptScope expected)
    {
        Assert.Equal(expected, CliOptions.Parse(["x.js", "--scope", name]).Scopes);
    }

    [Fact]
    public void AnUnknownScopeIsRefusedRatherThanIgnored()
    {
        // A typo that quietly granted nothing would surface much later, inside the script, as a
        // permission error with no obvious cause.
        var error = Assert.Throws<CliException>(() => CliOptions.Parse(["x.js", "--scope", "SheetsReed"]));

        Assert.Contains("SheetsReed", error.Message);
        Assert.Contains("SheetsRead", error.Message);
    }

    [Fact]
    public void NoScopeFlagIsDistinctFromAnEmptyScopeSet()
    {
        var options = CliOptions.Parse(["x.js"]);

        // push relies on this: "grant nothing" and "leave the grants alone" are different orders.
        Assert.Equal(ScriptScope.None, options.Scopes);
        Assert.False(options.ScopesGiven);
    }

    [Fact]
    public void InputsSplitOnTheFirstEqualsOnly()
    {
        var options = CliOptions.Parse(["x.js", "--input", "url=https://example.com/a=b"]);

        Assert.Equal("https://example.com/a=b", options.Input["url"]);
    }

    [Theory]
    [InlineData("novalue")]
    [InlineData("=orphan")]
    public void MalformedInputIsRefused(string pair)
    {
        Assert.Throws<CliException>(() => CliOptions.Parse(["x.js", "--input", pair]));
    }

    [Fact]
    public void LaterInputWinsForTheSameKey()
    {
        var options = CliOptions.Parse(["x.js", "-i", "month=07", "-i", "month=08"]);

        Assert.Equal("08", options.Input["month"]);
    }

    [Fact]
    public void HostsAreTrimmedAndOrdered()
    {
        var options = CliOptions.Parse(["x.js", "--host", " api.one.com ", "--host", "api.two.com"]);

        Assert.Equal(["api.one.com", "api.two.com"], options.Hosts);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void ATimeoutThatIsNotAPositiveNumberIsRefused(string value)
    {
        Assert.Throws<CliException>(() => CliOptions.Parse(["x.js", "--timeout", value]));
    }

    [Fact]
    public void AnIdMustParse()
    {
        Assert.Throws<CliException>(() => CliOptions.Parse(["x.js", "--id", "not-a-guid"]));
    }

    [Fact]
    public void AFlagMissingItsValueDoesNotConsumeTheTarget()
    {
        // "--name" with nothing after it must not swallow the next parse or crash.
        var options = CliOptions.Parse(["x.js", "--name"]);

        Assert.Equal("x.js", options.Target);
        Assert.Null(options.Name);
    }
}
