using AutoWork.Core.Knowledge;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The local embedder. Its output is written to disk and compared months later, so the property
/// that matters most is not quality — it is that the same text always produces the same vector.
/// </summary>
public sealed class LocalEmbeddingTests
{
    [Fact]
    public void The_same_text_always_produces_the_same_vector()
    {
        var first = LocalEmbeddingGenerator.Embed("the quarterly invoice for the north region");
        var second = LocalEmbeddingGenerator.Embed("the quarterly invoice for the north region");

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The reason the hash is FNV-1a rather than <c>string.GetHashCode</c>: .NET randomises
    /// string hashing per process, so a vector written today would compare as noise against one
    /// written after the next restart. Pinned against a known value so a "harmless" change to
    /// the hash cannot slip through and silently invalidate every stored knowledge base.
    /// </summary>
    [Theory]
    [InlineData("", 2166136261u)]
    [InlineData("a", 3826002220u)]
    [InlineData("invoice", 1519411472u)]
    public void The_hash_is_the_specified_one_and_does_not_move(string text, uint expected) =>
        Assert.Equal(expected, LocalEmbeddingGenerator.Fnv1a(text));

    [Fact]
    public void Vectors_are_the_declared_size_and_unit_length()
    {
        var vector = LocalEmbeddingGenerator.Embed("some ordinary sentence about filing");

        Assert.Equal(LocalEmbeddingGenerator.Dimensions, vector.Length);

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        Assert.InRange(length, 0.99f, 1.01f);
    }

    [Fact]
    public void Empty_text_gives_a_zero_vector_rather_than_throwing()
    {
        Assert.All(LocalEmbeddingGenerator.Embed(""), v => Assert.Equal(0, v));
        Assert.All(LocalEmbeddingGenerator.Embed("   "), v => Assert.Equal(0, v));
    }

    private static double Similarity(string a, string b)
    {
        var x = LocalEmbeddingGenerator.Embed(a);
        var y = LocalEmbeddingGenerator.Embed(b);

        return System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(x, y);
    }

    [Fact]
    public void Text_is_most_similar_to_itself()
    {
        Assert.InRange(Similarity("quarterly invoice", "quarterly invoice"), 0.99, 1.01);
    }

    [Fact]
    public void Related_text_scores_above_unrelated_text()
    {
        var related = Similarity(
            "the quarterly invoice for the north region",
            "invoices for the northern region this quarter");

        var unrelated = Similarity(
            "the quarterly invoice for the north region",
            "photographs of the garden in spring");

        Assert.True(related > unrelated, $"related {related:0.000} was not above unrelated {unrelated:0.000}");
    }

    /// <summary>
    /// What character n-grams buy: a word form it has never seen still matches. This is the
    /// difference from plain keyword overlap, and the honest limit of what a lexical embedder
    /// can do — it will not know that "bill" means "invoice".
    /// </summary>
    [Theory]
    [InlineData("invoice", "invoices")]
    [InlineData("invoice", "invoicing")]
    [InlineData("management", "managements")]
    public void A_different_form_of_the_same_word_still_matches(string a, string b)
    {
        Assert.True(Similarity(a, b) > 0.3, $"\"{a}\" and \"{b}\" scored {Similarity(a, b):0.000}");
    }

    [Fact]
    public void Word_order_makes_some_difference()
    {
        // Not a lot — but "cost centre" and "centre cost" should not be identical.
        Assert.True(Similarity("cost centre budget", "budget centre cost") < 0.999);
    }

    [Fact]
    public async Task It_reports_which_model_produced_each_vector()
    {
        var generator = new LocalEmbeddingGenerator();

        var embeddings = await generator.GenerateAsync(["one", "two"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(LocalEmbeddingGenerator.ModelId, e.ModelId));
    }

    [Fact]
    public void Tokenising_splits_on_anything_that_is_not_a_letter_or_digit()
    {
        Assert.Equal(["invoice", "2026", "q3", "north"],
            LocalEmbeddingGenerator.Tokenise("Invoice-2026 (Q3): north!"));
    }
}

/// <summary>
/// Searching with the local embedder, and the failure that made the model tag necessary.
/// </summary>
public sealed class LocalEmbeddingSearchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-localembed", Guid.NewGuid().ToString("n")[..8]);

    public LocalEmbeddingSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private JsonKnowledgeStore Store(Func<IEmbeddingGenerator<string, Embedding<float>>?> embedder) =>
        new(directory: _root, embedderFactory: embedder);

    [Fact]
    public async Task Knowledge_can_be_searched_with_nothing_but_this_machine()
    {
        var store = Store(() => new LocalEmbeddingGenerator());
        var kb = store.Create("Notes");

        await store.AddEntryAsync(kb.Id, "Filing invoices",
            "Invoices from the north region go in the quarterly folder.",
            cancellationToken: TestContext.Current.CancellationToken);

        await store.AddEntryAsync(kb.Id, "Garden photos",
            "Photographs of the garden, sorted by season.",
            cancellationToken: TestContext.Current.CancellationToken);

        var hits = await store.SearchAsync("where do northern invoices go", topK: 2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(hits);
        Assert.Equal("Filing invoices", hits[0].Entry.Title);

        // And the vector really was stored, tagged with what made it.
        Assert.Equal(LocalEmbeddingGenerator.ModelId, hits[0].Entry.EmbeddingModel);
        Assert.NotNull(hits[0].Entry.Embedding);
    }

    /// <summary>
    /// The defect the model tag exists to prevent.
    ///
    /// Vectors from two different models are not comparable. Compared anyway they return a
    /// meaningless number — or exactly zero when the dimensions differ, which the search then
    /// treats as "no match" and drops the entry from every result. Switching embedding model
    /// would have quietly emptied the knowledge base.
    /// </summary>
    [Fact]
    public async Task Switching_embedding_model_falls_back_to_keywords_instead_of_finding_nothing()
    {
        var store = Store(() => new LocalEmbeddingGenerator());
        var kb = store.Create("Notes");

        await store.AddEntryAsync(kb.Id, "Filing invoices",
            "Invoices from the north region go in the quarterly folder.",
            cancellationToken: TestContext.Current.CancellationToken);

        // The same store, now embedding with something else entirely — a different dimension
        // count, which is what makes the failure silent rather than merely wrong.
        var switched = Store(() => new FixedSizeEmbedder(dimensions: 16));

        var hits = await switched.SearchAsync("invoices", topK: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(hits);
        Assert.Equal("Filing invoices", hits[0].Entry.Title);
    }

    [Fact]
    public async Task With_no_embedder_at_all_search_still_works_on_keywords()
    {
        var store = Store(() => null);
        var kb = store.Create("Notes");

        await store.AddEntryAsync(kb.Id, "Filing invoices", "Invoices go in the quarterly folder.",
            cancellationToken: TestContext.Current.CancellationToken);

        var hits = await store.SearchAsync("invoices", topK: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(hits);
        Assert.Null(hits[0].Entry.Embedding);
    }

    /// <summary>Stands in for a different provider: same interface, different vector size.</summary>
    private sealed class FixedSizeEmbedder : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly int _dimensions;

        public FixedSizeEmbedder(int dimensions) => _dimensions = dimensions;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values
                .Select(v => new Embedding<float>(new float[_dimensions]) { ModelId = "someone-elses-model" })
                .ToList();

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
