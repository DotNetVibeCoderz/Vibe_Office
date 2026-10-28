// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ModelContextProtocol;
using OfficeNet.TestKit;
using Xunit;

namespace OfficeNet.Mcp.Tests;

/// <summary>
/// The workspace boundary. Everything else the server does is only as safe as this.
/// </summary>
public class WorkspaceTests
{
    [Fact]
    public void RelativePathsResolveInsideTheRoot()
    {
        using var root = new TempDirectory();
        var workspace = new Workspace(root.Path);

        Assert.Equal(Path.Combine(root.Path, "a", "b.docx"), workspace.Resolve("a/b.docx"));
        Assert.Equal(Path.Combine(root.Path, "c.docx"), workspace.Resolve("a/../c.docx"));
    }

    [Theory]
    [InlineData("../outside.docx")]
    [InlineData("a/../../outside.docx")]
    [InlineData("../../../../../../etc/passwd")]
    public void PathsThatClimbOutAreRefused(string path)
    {
        using var root = new TempDirectory();
        var workspace = new Workspace(root.Path);

        Assert.Throws<McpException>(() => workspace.Resolve(path));
    }

    [Fact]
    public void AnAbsolutePathOutsideIsRefusedAndOneInsideIsAllowed()
    {
        using var root = new TempDirectory();
        var workspace = new Workspace(root.Path);

        Assert.Throws<McpException>(() => workspace.Resolve(Path.GetTempPath()));
        Assert.Equal(Path.Combine(root.Path, "x.pdf"), workspace.Resolve(Path.Combine(root.Path, "x.pdf")));
    }

    [Fact]
    public void ASiblingSharingThePrefixIsNotInside()
    {
        // C:\work must not admit C:\workshop: the prefix check includes the separator.
        using var parent = new TempDirectory();
        var workspace = new Workspace(Path.Combine(parent.Path, "work"));

        Assert.Throws<McpException>(() => workspace.Resolve(Path.Combine(parent.Path, "workshop", "x.docx")));
        Assert.Throws<McpException>(() => workspace.Resolve("../workshop/x.docx"));
    }

    [Fact]
    public void PicturesAreReadFromInsideTheWorkspaceOnly()
    {
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var workspace = new Workspace(root.Path);

        Directory.CreateDirectory(Path.Combine(root.Path, "img"));
        File.WriteAllBytes(Path.Combine(root.Path, "img", "titik.png"), [1, 2, 3]);
        var secret = Path.Combine(outside.Path, "rahasia.png");
        File.WriteAllBytes(secret, [9, 9, 9]);

        var read = workspace.ImageReader(Path.Combine(root.Path, "img"));

        Assert.Equal([1, 2, 3], read("titik.png"));
        Assert.Equal([1, 2, 3], read("../img/titik.png"));
        Assert.Null(read(secret));
        Assert.Null(read(new Uri(secret).AbsoluteUri));
        Assert.Null(read(Path.GetRelativePath(Path.Combine(root.Path, "img"), secret)));
        Assert.Null(read("https://example.org/titik.png"));
        Assert.Null(read("//example.org/titik.png"));
        Assert.Null(read("tidak-ada.png"));
    }

    [Fact]
    public void TheRootItselfIsADirectoryButNotAFile()
    {
        using var root = new TempDirectory();
        var workspace = new Workspace(root.Path);

        Assert.Equal(root.Path, workspace.ResolveDirectory(".", create: false));
        Assert.Throws<McpException>(() => workspace.ForWriting("."));
    }

    [Fact]
    public void ALinkLeadingOutsideIsRefused()
    {
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var link = Path.Combine(root.Path, "escape");

        try
        {
            Directory.CreateSymbolicLink(link, outside.Path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Creating a symbolic link needs a privilege Windows does not grant by default; where
            // it cannot be made, it cannot be followed either.
            Assert.Skip("This environment cannot create symbolic links.");
        }

        var workspace = new Workspace(root.Path);
        Assert.Throws<McpException>(() => workspace.Resolve("escape/secret.docx"));
    }

    [Fact]
    public void ReadOnlyRefusesWritingButAllowsReading()
    {
        using var root = new TempDirectory();
        File.WriteAllText(root.File("ada.txt"), "x");
        var workspace = new Workspace(root.Path, readOnly: true);

        Assert.Equal(root.File("ada.txt"), workspace.Existing("ada.txt"));
        Assert.Throws<McpException>(() => workspace.ForWriting("baru.docx"));
        Assert.Throws<McpException>(() => workspace.ResolveDirectory("bagian", create: true));
    }

    [Fact]
    public void AMissingFileIsReportedByItsWorkspacePath()
    {
        using var root = new TempDirectory();
        var workspace = new Workspace(root.Path);

        var error = Assert.Throws<McpException>(() => workspace.Existing("tidak-ada.docx"));
        Assert.Contains("tidak-ada.docx", error.Message);
        Assert.DoesNotContain(root.Path, error.Message);
    }
}
