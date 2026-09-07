// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.TestKit;

/// <summary>
/// A temporary directory that deletes itself and everything in it.
/// </summary>
/// <remarks>
/// The companion to <see cref="TempFile"/>, for the tests that write several files at once — a
/// batch conversion, a multi-page render, a document plus the parts it points at.
/// </remarks>
public sealed class TempDirectory : IDisposable
{
    /// <summary>Creates and returns a new empty directory under the system temp path.</summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "officenet-test-" + Guid.NewGuid().ToString("N")[..12]);

        Directory.CreateDirectory(Path);
    }

    /// <summary>The directory's full path.</summary>
    public string Path { get; }

    /// <summary>Combines a file name onto the directory.</summary>
    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by a failing test must not replace that test's failure with
            // this one. The OS clears the temp directory eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
