// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OfficeNet.TestKit;

/// <summary>
/// veraPDF, the reference PDF/A validator, run as an independent reader.
/// </summary>
/// <remarks>
/// Not shipped with the tests: set <c>VERAPDF</c> to its launcher (<c>verapdf</c> or
/// <c>verapdf.bat</c>). A test that needs it skips with that instruction when it is not there, so
/// "skipped" never reads as "passed".
/// </remarks>
public static partial class VeraPdf
{
    /// <summary>The launcher, when <c>VERAPDF</c> names one that exists.</summary>
    public static string? Launcher =>
        Environment.GetEnvironmentVariable("VERAPDF") is { Length: > 0 } path && File.Exists(path) ? path : null;

    public const string SkipReason = "Set VERAPDF to veraPDF's launcher (verapdf or verapdf.bat) to validate against it.";

    /// <summary>
    /// Validates a file against a flavour such as <c>2b</c> or <c>3b</c>.
    /// </summary>
    /// <returns><c>true</c>, or <c>false: </c> and the failed clauses.</returns>
    public static string Validate(string path, string flavour, CancellationToken cancellationToken = default)
    {
        var launcher = Launcher ?? throw new InvalidOperationException(SkipReason);

        var start = new ProcessStartInfo(launcher)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in new[] { "--flavour", flavour, "--format", "xml", path })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        _ = process.StandardError.ReadToEndAsync(cancellationToken);

        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            return "false: veraPDF did not finish in three minutes";
        }

        var xml = output.Result;
        var verdict = Verdict().Match(xml).Groups[1].Value;

        return verdict == "true"
            ? verdict
            : (verdict.Length > 0 ? verdict : "no verdict") + ": " +
              string.Join("; ", Failure().Matches(xml).Select(m => m.Groups[1].Value + "-" + m.Groups[2].Value));
    }

    [GeneratedRegex("isCompliant=\"(\\w+)\"")]
    private static partial Regex Verdict();

    [GeneratedRegex("clause=\"([^\"]+)\" testNumber=\"(\\d+)\" status=\"failed\"")]
    private static partial Regex Failure();
}
