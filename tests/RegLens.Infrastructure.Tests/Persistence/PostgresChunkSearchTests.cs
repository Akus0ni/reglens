using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RegLens.Application.Ingestion;
using RegLens.Application.Search;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;
using RegLens.Domain.Search;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

/// <summary>
/// Hybrid search on a real pgvector database. Vectors are one-hot (axis i = chunk i), so the dense ranking is known:
/// a query vector close to axis 0, then 1, then 2 ranks chunks in that order.
/// </summary>
[Collection(PostgresTests.Name)]
public class PostgresChunkSearchTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string ParaRef, string Heading, string Content)[] Chunks =
    [
        ("Para 6", "Chapter III – Digital Lending", "6. The NBFC shall ensure the LSP follows these Directions."),
        ("Para 60", "Chapter VIII – Other Matters", "60. Loans against shares are governed by the limits below."),
        ("Para 13", "Chapter III – Digital Lending", "13. Apps shall desist from accessing the contact list and call logs on a phone."),
        ("First Schedule, Part A, Item 4", "First Schedule — Conditions", "4. The net worth of the applicant is not less than two crore rupees."),
    ];

    [Fact]
    public async Task FusesDenseAndKeywordRanksWithReciprocalRankFusion()
    {
        var search = await SeededSearch();

        var hits = await search.HybridAsync(Query("phone contact list", Near(2, 0, 1)), Ct);

        var top = hits[0];
        top.ParaRef.Should().Be("Para 13");
        top.DenseRank.Should().Be(1);
        top.KeywordRank.Should().Be(1);
        top.FusedScore.Should().BeApproximately((1.0 / 61) + (1.0 / 61), 1e-9);
        hits.Skip(1).Should().AllSatisfy(h => h.KeywordRank.Should().BeNull());
    }

    [Fact]
    public async Task MatchesAnyQuestionWordOnTheKeywordSide()
    {
        var search = await SeededSearch();

        var hits = await search.HybridAsync(KeywordOnly("Can the lender read my phone or my net worth?"), Ct);

        hits.Select(h => h.ParaRef).Should().BeEquivalentTo("Para 13", "First Schedule, Part A, Item 4");
        hits.Should().AllSatisfy(h => h.DenseRank.Should().BeNull());
    }

    [Fact]
    public async Task SearchesHeadingsAndParagraphReferences()
    {
        var search = await SeededSearch();

        var byHeading = await search.HybridAsync(KeywordOnly("digital lending"), Ct);
        var byReference = await search.HybridAsync(KeywordOnly("schedule item"), Ct);

        byHeading.Select(h => h.ParaRef).Should().BeEquivalentTo("Para 6", "Para 13");
        byReference.Should().ContainSingle().Which.ParaRef.Should().Be("First Schedule, Part A, Item 4");
    }

    [Fact]
    public async Task SkipsTheKeywordSideForAQuestionOfStopWords()
    {
        var search = await SeededSearch();

        var keywordOnly = await search.HybridAsync(KeywordOnly("what is the"), Ct);
        var hybrid = await search.HybridAsync(Query("what is the", Near(0, 1, 2)), Ct);

        keywordOnly.Should().BeEmpty();
        hybrid.Should().NotBeEmpty().And.AllSatisfy(h => h.KeywordRank.Should().BeNull());
    }

    [Fact]
    public async Task FindsCitedParagraphsWithoutMatchingLongerNumbers()
    {
        var search = await SeededSearch();

        var hits = await search.FindByCitationsAsync([new Citation("Para 6", CitationMatch.Prefix)], KeywordOnly("para 6"), Ct);

        hits.Should().ContainSingle().Which.ParaRef.Should().Be("Para 6");
    }

    [Fact]
    public async Task FindsScheduleItemsByTheirEnding()
    {
        var search = await SeededSearch();

        var hits = await search.FindByCitationsAsync(
            [new Citation("Item 4", CitationMatch.PrefixOrSuffix)], KeywordOnly("item 4"), Ct);

        hits.Should().ContainSingle().Which.ParaRef.Should().Be("First Schedule, Part A, Item 4");
    }

    [Fact]
    public async Task ReturnsTheDocumentTitleAndAnExcerpt()
    {
        var search = await SeededSearch();

        var hit = (await search.HybridAsync(Query("phone", Near(2, 0, 1)), Ct))[0];

        hit.DocumentTitle.Should().Be("Test Directions");
        hit.Excerpt.Should().StartWith("13. Apps shall desist");
        hit.Excerpt.Length.Should().BeLessThanOrEqualTo(PostgresChunkSearch.ExcerptLength);
    }

    private async Task<PostgresChunkSearch> SeededSearch()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(Ct);
        var document = new SourceDocument("doc", "doc.pdf", "Test Directions", "RBI/TEST", "v1", "https://example.test",
            new string('a', 64), 100, 1, new PageRange(1, 1), DocumentStructure.RbiDirections);
        var embedded = Chunks
            .Select((c, i) => new EmbeddedChunk(new Chunk("doc", c.ParaRef, c.Heading, 1, 1, c.Content), Axis(i)))
            .ToList();
        await new PostgresChunkStore(dataSource).ReplaceDocumentAsync(document, embedded, Ct);
        return new PostgresChunkSearch(dataSource);
    }

    private static SearchQuery Query(string text, float[] vector) => new(text, vector, CandidatesPerSide: 3, Limit: 10);

    private static SearchQuery KeywordOnly(string text) => new(text, ReadOnlyMemory<float>.Empty, CandidatesPerSide: 40, Limit: 10);

    private static float[] Axis(int i)
    {
        var v = new float[768];
        v[i] = 1;
        return v;
    }

    // Closest to the first axis, then the second, then the third; the fourth chunk falls outside 3 candidates.
    private static float[] Near(int first, int second, int third)
    {
        var v = new float[768];
        v[first] = 0.9f;
        v[second] = 0.4f;
        v[third] = 0.2f;
        return v;
    }
}
