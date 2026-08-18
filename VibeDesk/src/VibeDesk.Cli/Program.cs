using System.Text;
using VibeDesk.Cli;

// Windows consoles still default to a legacy code page, which turns every arrow and dash in the
// output into a question mark.
try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { /* no console attached */ }

// Ctrl+C should stop the CLI, not the script the server is already running — cancelling the request
// only abandons the response. The message says so rather than pretending the run was stopped.
using var lifetime = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    lifetime.Cancel();
};

try
{
    return await Commands.RunAsync(args, lifetime.Token);
}
catch (CliException ex)
{
    Output.Error(ex.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Output.Error("Cancelled. A run already started on the server keeps going; check 'vibedesk logs'.");
    return 130;
}
catch (HttpRequestException ex)
{
    Output.Error($"Could not reach the server: {ex.Message}");
    Output.Hint($"Endpoint: {CliConfig.Load().Endpoint}. Change it with 'vibedesk config <url>'.");
    return 1;
}
