using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// Synthetic mouse and keyboard — the most invasive thing AutoWork can do, and the only tool
/// group that is off by default even after folders have been granted.
///
/// There is no sandbox for this. Once input is synthesised it goes to whatever window has
/// focus, and no permission model in this process can constrain that. The controls that exist
/// are therefore consent and visibility: the capability is opt-in, each burst is
/// approval-gated by default, and every action is logged.
/// </summary>
public sealed class InputTools : ToolSetBase, IToolProvider
{
    public InputTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Input control";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        if (!context.Guard.Policy.AllowInputControl) yield break;

        var tools = new InputTools(context);

        yield return Describe(AIFunctionFactory.Create(tools.ClickAsync, "input_click",
            "Move the pointer to a screen coordinate and click. Take a screenshot first so you know what is there."));

        yield return Describe(AIFunctionFactory.Create(tools.MoveAsync, "input_move",
            "Move the pointer to a screen coordinate without clicking."));

        yield return Describe(AIFunctionFactory.Create(tools.TypeAsync, "input_type",
            "Type text into whatever window currently has focus."));

        yield return Describe(AIFunctionFactory.Create(tools.KeyAsync, "input_key",
            "Press a key combination such as ctrl+s, alt+tab, enter or escape."));
    }

    private static ToolDescriptor Describe(AIFunction function) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = ToolRisk.System,
        Category = "Input control",
        ApprovalKind = ApprovalKind.ControlInput,
    };

    private ApprovalKind? Approval =>
        Guard.Policy.InputControlRequiresApproval ? ApprovalKind.ControlInput : null;

    [Description("Move the pointer and click.")]
    private Task<string> ClickAsync(
        [Description("X coordinate in screen pixels.")] int x,
        [Description("Y coordinate in screen pixels.")] int y,
        [Description("Which button: left, right or middle.")] string button = "left",
        [Description("Double-click instead of a single click.")] bool doubleClick = false)
        => GuardedAsync("input.click", $"Click {button} at ({x}, {y})", async () =>
        {
            if (!Guard.Policy.AllowInputControl)
                return Refused("Controlling the mouse and keyboard is turned off in Settings › Permissions.");

            if (OperatingSystem.IsWindows())
            {
                WindowsInput.MoveTo(x, y);
                WindowsInput.Click(button, doubleClick);
                return Ok($"Clicked {button} at ({x}, {y}).");
            }

            return await ClickElsewhereAsync(x, y, button, doubleClick).ConfigureAwait(false);
        },
        approval: Approval,
        approvalDetail: $"AutoWork will click the {button} button at screen position ({x}, {y}).");

    [Description("Move the pointer.")]
    private Task<string> MoveAsync(
        [Description("X coordinate in screen pixels.")] int x,
        [Description("Y coordinate in screen pixels.")] int y)
        => GuardedAsync("input.move", $"Move pointer to ({x}, {y})", async () =>
        {
            if (!Guard.Policy.AllowInputControl)
                return Refused("Controlling the mouse and keyboard is turned off in Settings › Permissions.");

            if (OperatingSystem.IsWindows())
            {
                WindowsInput.MoveTo(x, y);
                return Ok($"Moved the pointer to ({x}, {y}).");
            }

            if (OperatingSystem.IsMacOS() && IsOnPath("cliclick"))
            {
                await RunAsync("cliclick", [$"m:{x},{y}"]).ConfigureAwait(false);
                return Ok($"Moved the pointer to ({x}, {y}).");
            }

            if (IsOnPath("xdotool"))
            {
                await RunAsync("xdotool", ["mousemove", x.ToString(), y.ToString()]).ConfigureAwait(false);
                return Ok($"Moved the pointer to ({x}, {y}).");
            }

            return Refused(MissingHelperMessage());
        },
        approval: Approval);

    [Description("Type text.")]
    private Task<string> TypeAsync([Description("The text to type.")] string text)
        => GuardedAsync("input.type", $"Type {text.Length} characters", async () =>
        {
            if (!Guard.Policy.AllowInputControl)
                return Refused("Controlling the mouse and keyboard is turned off in Settings › Permissions.");

            if (string.IsNullOrEmpty(text)) return Ok("Nothing to type.");

            if (OperatingSystem.IsWindows())
            {
                WindowsInput.Type(text);
                return Ok($"Typed {text.Length} characters.");
            }

            if (OperatingSystem.IsMacOS() && IsOnPath("cliclick"))
            {
                await RunAsync("cliclick", [$"t:{text}"]).ConfigureAwait(false);
                return Ok($"Typed {text.Length} characters.");
            }

            if (IsOnPath("xdotool"))
            {
                await RunAsync("xdotool", ["type", "--delay", "12", text]).ConfigureAwait(false);
                return Ok($"Typed {text.Length} characters.");
            }

            return Refused(MissingHelperMessage());
        },
        approval: Approval,
        approvalDetail: $"AutoWork will type this into the focused window:\n\n{Cap(text, 500)}");

    [Description("Press a key combination.")]
    private Task<string> KeyAsync(
        [Description("Combination such as ctrl+s, alt+tab, enter, escape or f5.")] string keys)
        => GuardedAsync("input.key", $"Press {keys}", async () =>
        {
            if (!Guard.Policy.AllowInputControl)
                return Refused("Controlling the mouse and keyboard is turned off in Settings › Permissions.");

            if (OperatingSystem.IsWindows())
            {
                if (!WindowsInput.TryPressCombination(keys, out var problem)) return Failed(problem);
                return Ok($"Pressed {keys}.");
            }

            if (OperatingSystem.IsMacOS() && IsOnPath("cliclick"))
            {
                await RunAsync("cliclick", [$"kp:{keys.Split('+').Last().Trim()}"]).ConfigureAwait(false);
                return Ok($"Pressed {keys}.");
            }

            if (IsOnPath("xdotool"))
            {
                await RunAsync("xdotool", ["key", keys.Replace(" ", "")]).ConfigureAwait(false);
                return Ok($"Pressed {keys}.");
            }

            return Refused(MissingHelperMessage());
        },
        approval: Approval,
        approvalDetail: $"AutoWork will press {keys} in the focused window.");

    private static async Task<string> ClickElsewhereAsync(int x, int y, string button, bool doubleClick)
    {
        if (OperatingSystem.IsMacOS() && IsOnPath("cliclick"))
        {
            var verb = button.Equals("right", StringComparison.OrdinalIgnoreCase) ? "rc" : doubleClick ? "dc" : "c";
            await RunAsync("cliclick", [$"{verb}:{x},{y}"]).ConfigureAwait(false);
            return Ok($"Clicked {button} at ({x}, {y}).");
        }

        if (IsOnPath("xdotool"))
        {
            var code = button.ToLowerInvariant() switch { "right" => "3", "middle" => "2", _ => "1" };
            await RunAsync("xdotool", ["mousemove", x.ToString(), y.ToString()]).ConfigureAwait(false);
            await RunAsync("xdotool", ["click", "--repeat", doubleClick ? "2" : "1", code]).ConfigureAwait(false);
            return Ok($"Clicked {button} at ({x}, {y}).");
        }

        return Refused(MissingHelperMessage());
    }

    private static string MissingHelperMessage() =>
        OperatingSystem.IsMacOS()
            ? "Input control on macOS needs the cliclick helper (brew install cliclick), and AutoWork must be granted Accessibility permission in System Settings › Privacy & Security."
            : "Input control on Linux needs xdotool (X11). Install it with your package manager. Wayland sessions additionally require a compositor that permits input synthesis.";

    private static bool IsOnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

        return path.Split(Path.PathSeparator)
            .Any(directory => !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, tool)));
    }

    private static async Task RunAsync(string fileName, string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{fileName} could not be started.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
    }
}

/// <summary>Win32 SendInput wrapper. Only touched when running on Windows.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsInput
{
    private const int InputMouse = 0;
    private const int InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint LeftDown = 0x0002, LeftUp = 0x0004;
    private const uint RightDown = 0x0008, RightUp = 0x0010;
    private const uint MiddleDown = 0x0020, MiddleUp = 0x0040;

    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    public static void MoveTo(int x, int y)
    {
        // Absolute coordinates are expressed as 0..65535 across the whole virtual desktop,
        // so a multi-monitor setup needs the virtual origin subtracted first.
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        var normalizedX = (int)Math.Round((x - left) * 65535.0 / width);
        var normalizedY = (int)Math.Round((y - top) * 65535.0 / height);

        Send([MouseInput(normalizedX, normalizedY, MouseMove | MouseAbsolute | MouseVirtualDesk)]);
    }

    public static void Click(string button, bool doubleClick)
    {
        var (down, up) = button.ToLowerInvariant() switch
        {
            "right" => (RightDown, RightUp),
            "middle" => (MiddleDown, MiddleUp),
            _ => (LeftDown, LeftUp),
        };

        Send([MouseInput(0, 0, down), MouseInput(0, 0, up)]);

        if (doubleClick)
        {
            Thread.Sleep(40);
            Send([MouseInput(0, 0, down), MouseInput(0, 0, up)]);
        }
    }

    public static void Type(string text)
    {
        // KEYEVENTF_UNICODE sidesteps keyboard layouts entirely, so accented and non-Latin
        // characters type correctly regardless of what the user has installed.
        var inputs = new List<Input>(text.Length * 2);

        foreach (var character in text)
        {
            if (character == '\n')
            {
                inputs.Add(KeyInput(0x0D, 0));
                inputs.Add(KeyInput(0x0D, KeyUp));
                continue;
            }

            if (character == '\r') continue;

            inputs.Add(UnicodeInput(character, 0));
            inputs.Add(UnicodeInput(character, KeyUp));
        }

        // SendInput takes a bounded array; chunk so very long strings still go through.
        foreach (var chunk in inputs.Chunk(64)) Send(chunk);
    }

    public static bool TryPressCombination(string combination, out string problem)
    {
        var parts = combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { problem = "No keys were given."; return false; }

        var codes = new List<ushort>();
        foreach (var part in parts)
        {
            if (!TryMapKey(part, out var code))
            {
                problem = $"\"{part}\" is not a key AutoWork recognises.";
                return false;
            }
            codes.Add(code);
        }

        var inputs = new List<Input>();
        foreach (var code in codes) inputs.Add(KeyInput(code, 0));
        for (var i = codes.Count - 1; i >= 0; i--) inputs.Add(KeyInput(codes[i], KeyUp));

        Send(inputs.ToArray());
        problem = "";
        return true;
    }

    private static bool TryMapKey(string name, out ushort code)
    {
        code = name.ToLowerInvariant() switch
        {
            "ctrl" or "control" => 0x11,
            "alt" => 0x12,
            "shift" => 0x10,
            "win" or "cmd" or "meta" => 0x5B,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "space" => 0x20,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            _ => 0,
        };

        if (code != 0) return true;

        // Function keys.
        if (name.Length >= 2 && (name[0] is 'f' or 'F') && int.TryParse(name[1..], out var number)
            && number is >= 1 and <= 24)
        {
            code = (ushort)(0x6F + number);
            return true;
        }

        // Single printable characters map to their VK directly for A-Z and 0-9.
        if (name.Length == 1)
        {
            var character = char.ToUpperInvariant(name[0]);
            if (char.IsAsciiLetterOrDigit(character)) { code = character; return true; }
        }

        return false;
    }

    private static Input MouseInput(int x, int y, uint flags) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInputData { X = x, Y = y, Flags = flags } },
    };

    private static Input KeyInput(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInputData { VirtualKey = virtualKey, Flags = flags } },
    };

    private static Input UnicodeInput(char character, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData { VirtualKey = 0, ScanCode = character, Flags = flags | KeyUnicode },
        },
    };

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new InvalidOperationException(
                "Windows rejected the synthetic input. This usually means the focused window runs " +
                "with higher privileges than AutoWork.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
