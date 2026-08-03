using System.Runtime.InteropServices;
using AutoWork.Core;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The Eyes and the most invasive part of the Hands, actually executed.
///
/// These paths compile and follow the documented Win32 API, which is not the same as working —
/// a wrong struct size or a missing DPI call produces a black image or a click in the wrong
/// place, and neither shows up until someone runs it.
///
/// They move the real pointer, so they run only when asked for explicitly: set
/// <c>AUTOWORK_LIVE_INPUT=1</c>. The pointer is put back where it started.
///
/// **`input_type` and `input_key` are deliberately not exercised here.** Synthetic keystrokes go
/// to whichever window has focus, which during a test run may well be the terminal that started
/// it — typing into a shell to prove typing works is not thoroughness, it is a hazard. Asserting
/// their effect would mean driving a foreign window and reading it back, which trades real risk
/// for modest assurance. Pointer movement exercises the same `SendInput` call and struct layout;
/// the keyboard field of that union remains unproven, and Progress.md says so.
/// </summary>
public sealed class LiveScreenAndInputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autowork-live-input", Guid.NewGuid().ToString("n")[..8]);

    public LiveScreenAndInputTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ToolContext Context(bool allowInput) => new()
    {
        Guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _root, Access = FolderAccess.ReadWrite }],
            AllowScreenCapture = true,
            AllowInputControl = allowInput,
            InputControlRequiresApproval = false,
        }),
        Approvals = new AutoApproveBroker(),
        Log = NullActionLog.Instance,
        Options = new AgentOptions(),
        RunId = "input",
        WorkingDirectory = _root,
    };

    private static async Task<string> InvokeAsync(IToolProvider provider, ToolContext context, string tool, Dictionary<string, object?> arguments)
    {
        var function = provider.GetTools(context).Single(t => t.Name == tool).Function;
        var result = await function.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return result?.ToString() ?? "";
    }

    // ── Eyes ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A capture that returns a valid but uniformly black PNG is the classic failure of this
    /// interop, and a file-exists check would sail straight past it. So the pixels are examined.
    /// </summary>
    [Fact]
    public async Task A_screen_capture_produces_a_real_image_with_more_than_one_colour()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows capture path. Skipped.");

        var context = Context(allowInput: false);
        var result = await InvokeAsync(new ScreenTools(context), context, "screen_capture", new() { ["label"] = "livetest" });

        Assert.DoesNotContain("ERROR", result);
        Assert.DoesNotContain("REFUSED", result);

        var captures = Directory.GetFiles(AppPaths.ScreenshotsDirectory, "*livetest*.png");
        var newest = captures.OrderByDescending(File.GetCreationTimeUtc).FirstOrDefault();

        Assert.NotNull(newest);
        Assert.True(new FileInfo(newest).Length > 5_000, "the capture is too small to be a real screen.");

        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(newest);

        Assert.True(image.Width > 200 && image.Height > 200, $"captured {image.Width}x{image.Height}.");

        var colours = new HashSet<uint>();
        for (var y = 0; y < image.Height; y += 17)
        {
            for (var x = 0; x < image.Width; x += 17)
            {
                colours.Add(image[x, y].PackedValue);
                if (colours.Count > 8) break;
            }
        }

        Assert.True(colours.Count > 8, $"the capture has only {colours.Count} distinct colours — it is probably blank.");

        File.Delete(newest);
    }

    [Fact]
    public async Task Listing_displays_reports_at_least_the_one_being_used()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows capture path. Skipped.");

        var context = Context(allowInput: false);
        var result = await InvokeAsync(new ScreenTools(context), context, "screen_displays", []);

        Assert.DoesNotContain("ERROR", result);
        Assert.Matches(@"\d+\s*[x×]\s*\d+", result);
    }

    // ── Hands, the invasive part ──────────────────────────────────────────────────────────

    /// <summary>
    /// Moving the pointer is the one synthetic-input effect that can be read back without
    /// disturbing anything, which makes it the honest way to prove SendInput actually fires.
    /// </summary>
    [Fact]
    public async Task Moving_the_pointer_actually_moves_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows SendInput path. Skipped.");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_INPUT") is null,
            "Moves the real pointer. Set AUTOWORK_LIVE_INPUT=1 to run. Skipped.");

        Assert.True(GetCursorPos(out var original), "could not read the pointer position.");

        try
        {
            var context = Context(allowInput: true);

            // Somewhere unambiguous and safely inside any display.
            const int targetX = 300;
            const int targetY = 220;

            var result = await InvokeAsync(new InputTools(context), context, "input_move",
                new() { ["x"] = targetX, ["y"] = targetY });

            Assert.DoesNotContain("ERROR", result);
            Assert.DoesNotContain("REFUSED", result);

            await Task.Delay(250, TestContext.Current.CancellationToken);

            Assert.True(GetCursorPos(out var moved), "could not read the pointer position afterwards.");

            // Windows can round by a pixel on a scaled display, so this is not an exact match.
            Assert.True(Math.Abs(moved.X - targetX) <= 2 && Math.Abs(moved.Y - targetY) <= 2,
                $"asked for ({targetX}, {targetY}), pointer is at ({moved.X}, {moved.Y}).");
        }
        finally
        {
            SetCursorPos(original.X, original.Y);
        }
    }

    /// <summary>
    /// The capability is the most invasive one AutoWork has, so the switch being off must mean
    /// the tools do not exist at all — not that they exist and decline.
    /// </summary>
    [Fact]
    public void Input_tools_are_absent_entirely_when_the_capability_is_off()
    {
        var context = Context(allowInput: false);

        Assert.Empty(new InputTools(context).GetTools(context));
        Assert.NotEmpty(new InputTools(Context(allowInput: true)).GetTools(Context(allowInput: true)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
