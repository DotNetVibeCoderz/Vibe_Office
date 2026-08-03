using System.Diagnostics;
using AutoWork.Core.Knowledge;

namespace AutoWork.Tests;

/// <summary>
/// How big a knowledge base has to get before searching it is slow.
///
/// Written before building an index, because "outgrows a linear scan" is a claim with a number
/// attached and the number decides whether the index is worth having at all.
/// </summary>
public sealed class KnowledgeScaleTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-scale", Guid.NewGuid().ToString("n")[..8]);

    public KnowledgeScaleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(50_000)]
    public async Task Searching_a_large_knowledge_base_stays_fast(int entries)
    {
        var store = new JsonKnowledgeStore(
            directory: Path.Combine(_root, entries.ToString()),
            embedderFactory: () => new LocalEmbeddingGenerator());

        var kb = store.Create("Scale");

        // Written directly rather than through AddEntryAsync: this measures search, and paying
        // for 50,000 individual file writes first would measure the disk instead.
        for (var i = 0; i < entries; i++)
        {
            kb.Entries.Add(new KnowledgeEntry
            {
                Title = $"Note {i}",
                Text = $"Invoice {i} for the north region, quarter {i % 4}, filed under cost centre {i % 50}.",
                Embedding = LocalEmbeddingGenerator.Embed(
                    $"Note {i}\nInvoice {i} for the north region, quarter {i % 4}, filed under cost centre {i % 50}."),
                EmbeddingModel = LocalEmbeddingGenerator.ModelId,
            });
        }

        // One untimed pass so the measurement is of the search, not of JIT.
        await store.SearchAsync("north region invoices", topK: 5, knowledgeBaseId: kb.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        var stopwatch = Stopwatch.StartNew();

        var hits = await store.SearchAsync("north region invoices", topK: 5, knowledgeBaseId: kb.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        stopwatch.Stop();

        Assert.NotEmpty(hits);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{entries:N0} entries searched in {stopwatch.ElapsedMilliseconds} ms");

        // A knowledge search runs inside an agent step the user is waiting on. Anything under a
        // few hundred milliseconds is invisible next to the provider round trip beside it.
        Assert.True(stopwatch.ElapsedMilliseconds < 1_000,
            $"searching {entries:N0} entries took {stopwatch.ElapsedMilliseconds} ms");
    }
}
