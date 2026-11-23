using Npgsql;
using RegLens.Application.Search;

namespace RegLens.Evals;

/// <summary>
/// Retrieval metrics without an LLM (spec 0004, R4.8): each answerable question runs through the real search path,
/// top 20, and is scored by the rank of its first expected chunk.
/// </summary>
public sealed class RetrievalEval(SearchRegulations search, NpgsqlDataSource dataSource)
{
    public const int TopK = 20;

    /// <summary>How many hits <c>/ask</c> sends to the model; the floor is computed over these (D33).</summary>
    public const int AskTopK = 8;

    public async Task<RetrievalRun> RunAsync(GoldenSet golden, RunInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(golden);

        var questions = new List<RetrievalQuestion>();
        foreach (var question in golden.Answerable)
        {
            var hits = (await search.RunAsync(question.Question, TopK, cancellationToken).ConfigureAwait(false)).Hits;
            var firstHit = hits.Select((hit, index) => (hit, rank: index + 1))
                .FirstOrDefault(h => GoldenSet.Matches(question, h.hit.DocumentId, h.hit.ParaRef));
            questions.Add(new RetrievalQuestion(
                question.Id,
                question.Kind,
                firstHit.hit is null ? null : firstHit.rank,
                BestSimilarity(hits),
                hits.Take(AskTopK).Any(hit => hit.Pinned),
                [.. hits.Take(5).Select(hit => $"{hit.DocumentId} {hit.ParaRef}")]));
        }

        var unanswerable = new List<UnanswerableSimilarity>();
        foreach (var question in golden.Unanswerable)
        {
            var hits = (await search.RunAsync(question.Question, TopK, cancellationToken).ConfigureAwait(false)).Hits;
            unanswerable.Add(new UnanswerableSimilarity(question.Id, question.Kind, BestSimilarity(hits)));
        }

        var ranks = questions.Select(q => q.FirstHitRank).ToList();
        return new RetrievalRun(info, ranks.Count,
            Metrics.RecallAt(ranks, 5), Metrics.RecallAt(ranks, 8), Metrics.MeanReciprocalRank(ranks, 10),
            questions, unanswerable);
    }

    /// <summary>Every expected reference must exist in the index (R4.7); returns the ones that do not.</summary>
    public async Task<IReadOnlyList<string>> MissingReferencesAsync(GoldenSet golden, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(golden);

        var stored = new List<(string Document, string ParaRef)>();
        await using (var command = dataSource.CreateCommand("SELECT document_id, para_ref FROM chunks"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                stored.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return
        [
            .. from question in golden.Answerable
               from expected in question.Expected!
               from reference in expected.Refs
               where !stored.Any(s => s.Document == expected.Document && GoldenSet.Matches(reference, s.ParaRef))
               select $"{question.Id}: {expected.Document} {reference}",
        ];
    }

    private static double? BestSimilarity(IReadOnlyList<SearchHit> hits) => hits.Take(AskTopK).Max(hit => hit.Similarity);
}
