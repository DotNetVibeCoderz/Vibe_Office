// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

// One benchmark project with a class per library rather than four projects. The alternative was
// four near-identical .csproj files whose only difference is one ProjectReference; a filter does
// the same job:
//
//     dotnet run -c Release -- --filter '*Word*'
//     dotnet run -c Release -- --filter '*' --job short
//
// Passing --job short trades statistical confidence for a run that finishes in a couple of
// minutes, which is the right default while iterating and the wrong one for a published number.

// No AddJob here: adding an explicit default job makes `--job short` run everything twice, once
// per job, which doubles the wall time and produces a report with two rows per method.
BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, DefaultConfig.Instance);

/// <summary>Entry point marker; BenchmarkSwitcher needs a type from this assembly.</summary>
public partial class Program
{
}
