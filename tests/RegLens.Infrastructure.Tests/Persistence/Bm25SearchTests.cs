using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RegLens.Application.Ingestion;
using RegLens.Application.Search;
using RegLens.Domain.Chunking;
using RegLens.Domain.Corpus;
using RegLens.Infrastructure.Persistence;

namespace RegLens.Infrastructure.Tests.Persistence;

/// <summary>BM25 keyword scoring and its statistics views (spec 0004, R4.3–R4.6).</summary>
[Collection(PostgresTests.Name)]
public class Bm25SearchTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // tsv = heading + para_ref + content (migration 002): "alpha", "para", the number, then the content's lexemes.
    private static readonly (string ParaRef, string Content)[] Chunks =
    [
        ("Para 1", "phone phone phone phone phone phone"),
        ("Para 2", "phone"),
        ("Para 3", "phone loan"),
        ("Para 4", "phone loan"),
        ("Para 5", "contact"),
    ];

    [Fact]
    public async Task CountsDocumentFrequencyAndLengthAfterARefresh()
    {
        var dataSource = await Seeded();

        (await Scalar<long>(dataSource, "SELECT ndoc FROM lexeme_stats WHERE lexeme = 'phone'")).Should().Be(4);
        (await Scalar<long>(dataSource, "SELECT ndoc FROM lexeme_stats WHERE lexeme = 'contact'")).Should().Be(1);
        // Para 1: alpha + para + 1 + phone × 6 = 9 positions.
        (await Scalar<int>(dataSource, "SELECT max(len) FROM chunk_lengths")).Should().Be(9);
        (await Scalar<int>(dataSource, "SELECT n FROM corpus_stats")).Should().Be(5);
        // (9 + 4 + 5 + 5 + 4) / 5
        (await Scalar<double>(dataSource, "SELECT avg_len FROM corpus_stats")).Should().BeApproximately(27.0 / 5, 1e-9);
    }

    [Fact]
    public async Task RanksARareWordAboveARepeatedCommonOne()
    {
        var search = new PostgresChunkSearch(await Seeded());

        var hits = await search.HybridAsync(KeywordOnly("phone contact", KeywordScoring.Bm25), Ct);

        // idf(contact) = ln(1 + 4.5 / 1.5) ≈ 1.386 against idf(phone) = ln(1 + 1.5 / 4.5) ≈ 0.288, and BM25 saturates
        // Para 1's six repeats at most to idf × (k1 + 1): 0.288 × 2.2 ≈ 0.63, below Para 5's ≈ 1.39 × 2.2 / 1.1.
        hits.Select(h => h.ParaRef).Should().StartWith("Para 5");
        hits.Should().HaveCount(5);
        hits.Should().AllSatisfy(h => h.DenseRank.Should().BeNull());
    }

    [Fact]
    public async Task ScoresTheSameMatchesAsTsRankCd()
    {
        var search = new PostgresChunkSearch(await Seeded());

        var bm25 = await search.HybridAsync(KeywordOnly("loan", KeywordScoring.Bm25), Ct);
        var tsRank = await search.HybridAsync(KeywordOnly("loan", KeywordScoring.TsRankCd), Ct);

        bm25.Select(h => h.ParaRef).Should().BeEquivalentTo(tsRank.Select(h => h.ParaRef)).And.BeEquivalentTo("Para 3", "Para 4");
    }

    [Fact]
    public async Task DropsTheKeywordSideWhenKeywordsAreOff()
    {
        var search = new PostgresChunkSearch(await Seeded());
        var vector = new float[768];
        vector[4] = 1;

        var hits = await search.HybridAsync(new SearchQuery("contact", vector, 3, 10, KeywordScoring.Bm25, UseKeywords: false), Ct);

        hits.Should().HaveCount(3).And.AllSatisfy(h => h.KeywordRank.Should().BeNull());
        hits[0].ParaRef.Should().Be("Para 5");
    }

    private async Task<NpgsqlDataSource> Seeded()
    {
        var dataSource = await postgres.CreateDatabaseAsync();
        await new SqlMigrationRunner(dataSource, NullLogger<SqlMigrationRunner>.Instance).ApplyAsync(Ct);
        var document = new SourceDocument("doc", "doc.pdf", "Test Directions", "RBI/TEST", "v1", "https://example.test",
            new string('a', 64), 100, 1, new PageRange(1, 1), DocumentStructure.RbiDirections);
        var store = new PostgresChunkStore(dataSource);
        await store.ReplaceDocumentAsync(document,
            [.. Chunks.Select((c, i) => new EmbeddedChunk(new Chunk("doc", c.ParaRef, "Alpha", 1, 1, c.Content), Axis(i)))], Ct);
        await store.RefreshSearchStatisticsAsync(Ct);
        return dataSource;
    }

    private static SearchQuery KeywordOnly(string text, KeywordScoring scoring) =>
        new(text, ReadOnlyMemory<float>.Empty, CandidatesPerSide: 40, Limit: 10, scoring);

    private static async Task<T> Scalar<T>(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync(Ct))!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static float[] Axis(int i)
    {
        var v = new float[768];
        v[i] = 1;
        return v;
    }
}
