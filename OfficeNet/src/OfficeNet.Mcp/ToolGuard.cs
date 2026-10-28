// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using ModelContextProtocol;
using OfficeNet.Core;

namespace OfficeNet.Mcp;

/// <summary>
/// Turns the exceptions a tool can meaningfully explain into messages the model sees.
/// </summary>
/// <remarks>
/// The SDK reports an <see cref="McpException"/>'s message to the client and replaces any other
/// exception with a generic error, which is the right default — a stack trace or a full path from
/// some unexpected failure is not something to hand a model. But "the sheet has no cell C9" or
/// "that file is not a .docx" is exactly what the model needs to correct its next call, so the
/// library's own exceptions, and the argument errors a malformed call causes, are let through by
/// name. Anything else stays generic.
/// </remarks>
internal static class ToolGuard
{
    internal static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex) when (ex is OfficeNetException or ArgumentException or FormatException
                                       or InvalidOperationException or JsonException
                                       or KeyNotFoundException or NotSupportedException
                                       or FileNotFoundException or InvalidDataException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
