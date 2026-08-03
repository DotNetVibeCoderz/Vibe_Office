using AutoWork.Core;
using AutoWork.Core.Storage;

namespace AutoWork.Tests;

/// <summary>
/// The recycle bin's whole claim is that a deletion is undoable. That claim rests on the index —
/// without a record of where a file came from, "recoverable" only means "still on disk
/// somewhere", which is what it meant before.
/// </summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-recycle", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _bin;
    private readonly string _work;

    public RecycleBinTests()
    {
        _bin = Path.Combine(_root, "recycle");
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_bin);
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private RecycleBin Bin() => new(_bin);

    private string WriteFile(string name, string content = "hello")
    {
        var path = Path.Combine(_work, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_deleted_file_leaves_its_original_location_and_is_listed()
    {
        var path = WriteFile("notes.txt");
        var item = Bin().Store(path, isDirectory: false, runId: "r1");

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(item.StoredPath));

        var listed = Assert.Single(Bin().List());
        Assert.Equal(path, listed.OriginalPath);
        Assert.Equal("notes.txt", listed.Name);
        Assert.Equal("r1", listed.RunId);
        Assert.True(listed.CanRestore);
        Assert.Equal(5, listed.SizeBytes);
    }

    [Fact]
    public void Putting_a_file_back_returns_it_to_where_it_was_with_its_contents()
    {
        var path = WriteFile("report.txt", "the original contents");
        var item = Bin().Store(path, isDirectory: false);

        var result = Bin().Restore(item.Id);

        Assert.Equal(RestoreStatus.Restored, result.Status);
        Assert.True(File.Exists(path));
        Assert.Equal("the original contents", File.ReadAllText(path));

        // And it is gone from the bin, both from the listing and from disk.
        Assert.Empty(Bin().List());
        Assert.False(File.Exists(item.StoredPath));
    }

    [Fact]
    public void A_whole_folder_comes_back_with_everything_in_it()
    {
        var folder = Path.Combine(_work, "project");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "a.txt"), "A");
        File.WriteAllText(Path.Combine(folder, "nested", "b.txt"), "B");

        var item = Bin().Store(folder, isDirectory: true);
        Assert.False(Directory.Exists(folder));

        Assert.Equal(RestoreStatus.Restored, Bin().Restore(item.Id).Status);

        Assert.Equal("A", File.ReadAllText(Path.Combine(folder, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(folder, "nested", "b.txt")));
    }

    /// <summary>
    /// The case this page exists to handle carefully: the user recreated the file, and putting
    /// the old one back would silently destroy the new one.
    /// </summary>
    [Fact]
    public void Restoring_over_something_that_exists_is_refused_until_it_is_asked_for_twice()
    {
        var path = WriteFile("draft.txt", "old");
        var item = Bin().Store(path, isDirectory: false);

        File.WriteAllText(path, "new work since then");

        var first = Bin().Restore(item.Id);
        Assert.Equal(RestoreStatus.Occupied, first.Status);
        Assert.Equal("new work since then", File.ReadAllText(path));

        // Still in the bin — a refusal must not consume the item.
        Assert.Single(Bin().List());

        var second = Bin().Restore(item.Id, overwrite: true);
        Assert.Equal(RestoreStatus.Restored, second.Status);
        Assert.Equal("old", File.ReadAllText(path));
    }

    [Fact]
    public void A_file_whose_parent_folder_has_since_gone_is_still_restorable()
    {
        var path = WriteFile(Path.Combine("subfolder", "buried.txt"), "still wanted");
        var item = Bin().Store(path, isDirectory: false);

        Directory.Delete(Path.Combine(_work, "subfolder"), recursive: true);

        Assert.Equal(RestoreStatus.Restored, Bin().Restore(item.Id).Status);
        Assert.Equal("still wanted", File.ReadAllText(path));
    }

    /// <summary>
    /// The index is the only thing saying where a file goes back to. A doctored line must not
    /// become a way to drop a file on top of config.json or secrets.json.
    /// </summary>
    [Fact]
    public void Restoring_into_AutoWorks_own_folder_is_refused()
    {
        var path = WriteFile("innocent.txt");
        var item = Bin().Store(path, isDirectory: false);

        var forged = new RecycledItem
        {
            Id = item.Id,
            OriginalPath = Path.Combine(AppPaths.Root, "config.json"),
            StoredPath = item.StoredPath,
            IsDirectory = false,
        };

        File.AppendAllText(Path.Combine(_bin, "index.jsonl"),
            System.Text.Json.JsonSerializer.Serialize(forged,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
            + Environment.NewLine);

        var result = Bin().Restore(item.Id);

        Assert.Equal(RestoreStatus.Refused, result.Status);
        Assert.True(File.Exists(item.StoredPath), "the item was consumed by a refused restore");
    }

    [Fact]
    public void Deleting_for_good_removes_the_file_and_the_listing()
    {
        var item = Bin().Store(WriteFile("gone.txt"), isDirectory: false);

        Assert.True(Bin().Purge(item.Id));
        Assert.False(File.Exists(item.StoredPath));
        Assert.Empty(Bin().List());
    }

    [Fact]
    public void Emptying_clears_everything_including_the_index()
    {
        Bin().Store(WriteFile("one.txt"), isDirectory: false);
        Bin().Store(WriteFile("two.txt"), isDirectory: false);

        Assert.Equal(2, Bin().PurgeAll());
        Assert.Empty(Bin().List());
        Assert.False(File.Exists(Path.Combine(_bin, "index.jsonl")));
    }

    /// <summary>
    /// Files recycled by an earlier build have no index entry. They still take up space, so
    /// hiding them would be worse than showing them as unrestorable.
    /// </summary>
    [Fact]
    public void Items_recycled_before_the_index_existed_are_listed_but_cannot_be_put_back()
    {
        var day = Path.Combine(_bin, "20260101");
        Directory.CreateDirectory(day);
        File.WriteAllText(Path.Combine(day, "120000-legacy.txt"), "from an older build");

        var listed = Assert.Single(Bin().List());

        Assert.False(listed.CanRestore);
        Assert.Equal("120000-legacy.txt", listed.Name);
        Assert.Equal(RestoreStatus.OriginUnknown, Bin().Restore(listed.Id).Status);

        // Purging still works, which is the point of showing them at all.
        Assert.True(Bin().Purge(listed.Id));
    }

    [Fact]
    public void A_torn_index_line_costs_one_entry_rather_than_the_whole_index()
    {
        var good = Bin().Store(WriteFile("kept.txt"), isDirectory: false);

        // A process killed mid-append.
        File.AppendAllText(Path.Combine(_bin, "index.jsonl"), "{\"id\":\"half-writ");

        var second = Bin().Store(WriteFile("also-kept.txt"), isDirectory: false);

        var ids = Bin().List().Select(i => i.Id).ToArray();

        Assert.Contains(good.Id, ids);
        Assert.Contains(second.Id, ids);
    }

    [Fact]
    public void Restoring_something_that_has_since_been_removed_by_hand_says_so()
    {
        var item = Bin().Store(WriteFile("vanishing.txt"), isDirectory: false);
        File.Delete(item.StoredPath);

        Assert.Equal(RestoreStatus.Missing, Bin().Restore(item.Id).Status);
    }

    [Fact]
    public void Two_files_deleted_in_the_same_second_do_not_collide()
    {
        var first = Bin().Store(WriteFile("a/same.txt", "first"), isDirectory: false);
        var second = Bin().Store(WriteFile("b/same.txt", "second"), isDirectory: false);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.StoredPath, second.StoredPath);
        Assert.Equal(2, Bin().List().Count);

        Assert.Equal(RestoreStatus.Restored, Bin().Restore(first.Id).Status);
        Assert.Equal(RestoreStatus.Restored, Bin().Restore(second.Id).Status);

        Assert.Equal("first", File.ReadAllText(Path.Combine(_work, "a", "same.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_work, "b", "same.txt")));
    }
}
