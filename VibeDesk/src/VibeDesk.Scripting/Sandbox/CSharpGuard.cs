using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VibeDesk.Scripting.Sandbox;

/// <summary>
/// Refuses C# script source that reaches outside the host API, before any of it runs.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the three runtimes genuinely differ. Jint interprets JavaScript and cannot
/// see the CLR at all unless something is handed to it; IronPython is constrained by withholding its
/// dangerous modules. Roslyn compiles to real IL with the full framework in scope — <c>File.Delete</c>
/// is one line away — so C# has to be refused at analysis time instead.
/// </para>
/// <para>
/// <b>This is defence in depth, not a VM boundary.</b> It resolves every identifier against the
/// compilation and rejects the ones whose declaring namespace or type is on the deny list, which
/// stops the direct routes and the obvious indirect ones (reflection, <c>dynamic</c>, unsafe code,
/// P/Invoke). A determined attacker with a novel gadget is a different problem, and the answer to
/// that one is process isolation — see docs/scripting.md.
/// </para>
/// </remarks>
public static class CSharpGuard
{
    /// <summary>Namespaces no script may touch, matched on the symbol's containing namespace.</summary>
    private static readonly string[] DeniedNamespaces =
    [
        "System.IO",
        "System.Diagnostics",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
        "System.Runtime.CompilerServices",
        "System.Net",              // outbound HTTP goes through api.http, which enforces the host list
        "System.Threading",        // no spawning threads out from under the timeout
        "System.Security",
        "Microsoft.Win32",
        "System.CodeDom",
        "System.Data.Common",
        "System.Configuration",
        "System.Management",
        "System.ComponentModel.Composition",
    ];

    /// <summary>Types reachable from allowed namespaces that still hand over the machine.</summary>
    private static readonly string[] DeniedTypes =
    [
        "System.Environment",
        "System.AppDomain",
        "System.AppContext",
        "System.Activator",
        "System.Type",             // Type.GetType(...) is the reflection back door
        "System.GC",
        "System.Console",          // scripts log through api.log so output is captured and capped
        "System.OperatingSystem",
        "System.Uri",              // network reachability belongs to api.http
    ];

    /// <summary>Imports the execution uses. The guard must resolve names identically or it is blind.</summary>
    private static readonly string[] ScriptUsings =
    [
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.Text.RegularExpressions",
        "VibeDesk.Scripting.Host",
    ];

    /// <summary>
    /// Type names refused on sight, without needing the symbol to resolve. Deliberately the short
    /// names: this layer exists precisely for the case where resolution failed.
    /// </summary>
    private static readonly string[] DeniedSimpleNames =
    [
        "Environment", "AppDomain", "AppContext", "Activator", "GC", "Console", "OperatingSystem",
        "File", "Directory", "FileInfo", "DirectoryInfo", "Path", "FileStream", "StreamWriter",
        "StreamReader", "Process", "ProcessStartInfo", "Assembly", "Marshal", "DllImport",
        "HttpClient", "WebClient", "Socket", "TcpClient", "Thread", "ThreadPool", "Mutex",
        "RuntimeHelpers", "Unsafe", "Registry", "AssemblyLoadContext",
    ];

    private static readonly string[] DeniedMembers =
    [
        "GetType",
        "InvokeMember",
        "DynamicInvoke",
    ];

    /// <summary>Returns a message when the source must be refused, or null when it may run.</summary>
    public static string? Inspect(string code, IEnumerable<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(
            code, new CSharpParseOptions(kind: SourceCodeKind.Script, languageVersion: LanguageVersion.Latest));

        if (tree.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } syntax)
        {
            return $"Syntax error: {syntax.GetMessage()} (line {syntax.Location.GetLineSpan().StartLinePosition.Line + 1})";
        }

        var root = tree.GetRoot();

        // Syntax-level refusals first: these need no symbol resolution and cover the constructs whose
        // whole purpose is to escape static analysis.
        if (root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .FirstOrDefault(u => IsDenied(u.Name?.ToString())) is { } deniedUsing)
        {
            return $"'using {deniedUsing.Name}' is not permitted in a script.";
        }

        if (root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.UnsafeKeyword)))
        {
            return "Unsafe code is not permitted in a script.";
        }

        if (root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.StackAllocKeyword)))
        {
            return "stackalloc is not permitted in a script.";
        }

        if (root.DescendantNodes().OfType<AttributeSyntax>()
                .Any(a => a.Name.ToString().Contains("DllImport", StringComparison.Ordinal)
                          || a.Name.ToString().Contains("LibraryImport", StringComparison.Ordinal)))
        {
            return "P/Invoke is not permitted in a script.";
        }

        // `dynamic` defers binding to runtime, which is precisely where this analysis cannot see.
        if (root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Any(i => i.Identifier.Text == "dynamic"))
        {
            return "'dynamic' is not permitted in a script: it moves member resolution past this check.";
        }

        // Simple names, checked without symbols. Symbol analysis is the precise instrument, but it is
        // silent when a name fails to resolve — and "fails to resolve" is not the same as "safe".
        // This layer costs a name comparison and does not depend on the compilation succeeding.
        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;

            if (!DeniedSimpleNames.Contains(name, StringComparer.Ordinal)) continue;

            // A local or parameter of the same name is the script's own, not the framework type.
            if (IsDeclaredLocally(root, name)) continue;

            var line = identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            return $"'{name}' is not available to scripts (line {line})";
        }

        // The usings must match ScriptOptions exactly. Without them a bare `Environment.Exit(1)`
        // does not resolve, the symbol comes back null, and the check below skips it — which is how
        // an unqualified call slipped through while the fully-qualified `System.IO.File` was caught.
        var compilation = CSharpCompilation.CreateScriptCompilation(
            "vibedesk-script-guard",
            tree,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false,
                usings: ScriptUsings));

        var model = compilation.GetSemanticModel(tree);

        foreach (var node in root.DescendantNodes())
        {
            if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax))
            {
                continue;
            }

            var symbol = model.GetSymbolInfo(node).Symbol
                         ?? model.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault();

            if (symbol is null) continue;

            if (Refuse(symbol) is { } reason)
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                return $"{reason} (line {line})";
            }
        }

        return null;
    }

    private static string? Refuse(ISymbol symbol)
    {
        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        var fullType = containingType?.ToDisplayString();

        if (fullType is not null && DeniedTypes.Contains(fullType, StringComparer.Ordinal))
        {
            return $"'{fullType}' is not available to scripts";
        }

        var ns = containingType?.ContainingNamespace?.ToDisplayString()
                 ?? symbol.ContainingNamespace?.ToDisplayString();

        if (IsDenied(ns)) return $"'{ns}' is not available to scripts";

        if (symbol.Kind == SymbolKind.Method && DeniedMembers.Contains(symbol.Name, StringComparer.Ordinal))
        {
            return $"'{symbol.Name}()' is not available to scripts";
        }

        return null;
    }

    /// <summary>
    /// True when the script declares this name itself — a variable called <c>path</c> or a parameter
    /// called <c>file</c> is the author's, not the framework's.
    /// </summary>
    private static bool IsDeclaredLocally(SyntaxNode root, string name) =>
        root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Any(v => v.Identifier.Text == name)
        || root.DescendantNodes().OfType<ParameterSyntax>()
            .Any(p => p.Identifier.Text == name)
        || root.DescendantNodes().OfType<SingleVariableDesignationSyntax>()
            .Any(d => d.Identifier.Text == name)
        || root.DescendantNodes().OfType<ForEachStatementSyntax>()
            .Any(f => f.Identifier.Text == name);

    private static bool IsDenied(string? ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return false;

        // Prefix match on a namespace boundary, so "System.IO" also denies "System.IO.Compression"
        // without "System.Ion" tripping it.
        return DeniedNamespaces.Any(denied =>
            ns.Equals(denied, StringComparison.Ordinal) ||
            ns.StartsWith(denied + ".", StringComparison.Ordinal));
    }
}
