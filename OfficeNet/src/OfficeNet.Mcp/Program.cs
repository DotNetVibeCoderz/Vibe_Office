// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeNet.Mcp;
using OfficeNet.Mcp.Tools;

// officenet-mcp — an MCP server over stdio.
//
//   officenet-mcp [--root <directory>] [--read-only]
//
// --root     The workspace; every path a tool receives is relative to it, and nothing outside it is
//            read or written. Defaults to OFFICENET_MCP_ROOT, then to the current directory.
// --read-only  Refuse every tool that would create or change a file.

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine("""
        officenet-mcp — create, edit, convert and preview Word, Excel, PowerPoint and PDF over MCP.

        Usage: officenet-mcp [--root <directory>] [--read-only]

          --root <directory>  Workspace. Paths are relative to it; nothing outside is touched.
                              Default: $OFFICENET_MCP_ROOT, else the current directory.
          --read-only         Refuse every tool that would create or change a file.
        """);
    return 0;
}

var root = Argument("--root") ?? Environment.GetEnvironmentVariable("OFFICENET_MCP_ROOT") ?? Environment.CurrentDirectory;
var readOnly = args.Contains("--read-only");

var builder = Host.CreateApplicationBuilder(args);

// stdout is the protocol channel. Anything else written there — a log line, a banner — corrupts
// the JSON-RPC stream and the client drops the connection, so every log goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(new Workspace(root, readOnly));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "officenet",
            Title = "OfficeNet",
            Version = typeof(Workspace).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        };
        options.ServerInstructions =
            "Creates, edits, converts and previews Word (.docx), Excel (.xlsx), PowerPoint (.pptx) " +
            "and PDF files inside one workspace directory. Paths are relative to that directory. " +
            "Use render_page to look at a result after creating it. The workspace's documents are also " +
            "resources (officenet://workspace/<path>, read as text), and the prompts are ready-made plans.";
    })
    .WithStdioServerTransport()
    .WithTools<WordTools>()
    .WithTools<ExcelTools>()
    .WithTools<PowerPointTools>()
    .WithTools<PdfTools>()
    .WithTools<DocumentTools>()
    .WithResources<WorkspaceResources>()
    .WithListResourcesHandler((request, _) =>
        ValueTask.FromResult(new WorkspaceResources(request.Services!.GetRequiredService<Workspace>()).List()))
    .WithPrompts<OfficePrompts>();

await builder.Build().RunAsync();
return 0;

string? Argument(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
