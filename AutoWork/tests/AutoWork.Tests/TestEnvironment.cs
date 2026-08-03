using System.Runtime.CompilerServices;

namespace AutoWork.Tests;

/// <summary>
/// Points AutoWork's two well-known folders at throwaway directories before any test runs.
///
/// A module initializer rather than a fixture, because <c>AppPaths</c> resolves both lazily and
/// caches them for the life of the process: whichever test touched them first would otherwise
/// decide where every other test wrote, and the real answer is the developer's own
/// <c>%APPDATA%\AutoWork</c> and <c>Documents\AutoWork</c>.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        Point("AUTOWORK_HOME", "home");
        Point("AUTOWORK_WORKSPACE", "workspace");
    }

    /// <summary>Set only when absent, so a deliberate override from the shell still wins.</summary>
    private static void Point(string variable, string leaf)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))) return;

        Environment.SetEnvironmentVariable(variable,
            Path.Combine(Path.GetTempPath(), "autowork-tests", Guid.NewGuid().ToString("n")[..8], leaf));
    }
}
