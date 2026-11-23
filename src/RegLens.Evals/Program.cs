using System.Diagnostics;
using System.Globalization;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using RegLens.Application;
using RegLens.Application.Answers;
using RegLens.Application.Search;
using RegLens.Evals;
using RegLens.Infrastructure;
using RegLens.Infrastructure.Persistence;

// RegLens eval runner (spec 0004, D45). Run from the repository root:
//   dotnet run --project src/RegLens.Evals -- check
//   dotnet run --project src/RegLens.Evals -- retrieval --config hybrid-bm25
//   dotnet run --project src/RegLens.Evals -- floor --config hybrid-bm25
//   dotnet run --project src/RegLens.Evals -- answers --config hybrid-bm25 [--floor 0.5] --confirm-spend
//   dotnet run --project src/RegLens.Evals -- probe --config hybrid-bm25 --confirm-spend
//   dotnet run --project src/RegLens.Evals -- report
//   dotnet run --project src/RegLens.Evals -- judge-sheet
const string GoldenSetPath = "evals/golden-set.json";
const string JudgeCheckPath = "evals/judge-check.md";
const string ProbeQuestion = "When must a personal data breach be reported to the Board?";

EvalCommandLine command;
try
{
    command = EvalCommandLine.Parse(args);
}
catch (ArgumentException exception)
{
    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    return 2;
}

if (!File.Exists(GoldenSetPath))
{
    await Console.Error.WriteLineAsync($"{GoldenSetPath} not found; run from the repository root.").ConfigureAwait(false);
    return 2;
}

var golden = GoldenSet.Load(GoldenSetPath);
if (command.Name == "report")
{
    Console.WriteLine($"Wrote {Report.Write()}");
    return 0;
}

if (command.Name == "judge-sheet")
{
    // Rebuilds evals/judge-check.md from the latest saved answer run; no API calls.
    var lastAnswers = Report.Load<AnswerRun>("answers").MaxBy(r => r.Info.StartedUtc);
    if (lastAnswers is null)
    {
        await Console.Error.WriteLineAsync("No answer run yet.").ConfigureAwait(false);
        return 1;
    }

    await File.WriteAllTextAsync(JudgeCheckPath, AnswerEval.JudgeCheckSheet(lastAnswers)).ConfigureAwait(false);
    Console.WriteLine($"Wrote {JudgeCheckPath} from the {lastAnswers.Info.StartedUtc:yyyy-MM-dd HH:mm} UTC run");
    return 0;
}

if (command.Name == "floor")
{
    var latest = Report.Load<RetrievalRun>("retrieval").Where(r => r.Info.Config == command.Config.Name).MaxBy(r => r.Info.StartedUtc);
    if (latest is null)
    {
        await Console.Error.WriteLineAsync($"No retrieval run for {command.Config.Name}; run `retrieval --config {command.Config.Name}` first.").ConfigureAwait(false);
        return 1;
    }

    // Pinned questions bypass the floor (spec 0003, P2), so they do not constrain it (D48).
    var constraining = latest.Questions.Where(q => !q.Pinned && q.BestSimilarityTop8 is not null).ToList();
    var floor = Metrics.Floor(constraining.Select(q => q.BestSimilarityTop8!.Value), margin: 0.02);
    var caught = latest.Unanswerable.Where(q => q.BestSimilarityTop8 < floor).ToList();
    Console.WriteLine($"Floor (D48) from {latest.Info.Config} run {latest.Info.StartedUtc:yyyy-MM-dd HH:mm} UTC: {Metrics.Format(floor, 2)}");
    Console.WriteLine($"  lowest answerable best similarity: {Metrics.Format(constraining.Min(q => q.BestSimilarityTop8!.Value))} ({constraining.MinBy(q => q.BestSimilarityTop8)!.Id}), n = {constraining.Count} (pinned excluded: {latest.Questions.Count - constraining.Count})");
    Console.WriteLine($"  unanswerable below the floor: {caught.Count}/{latest.Unanswerable.Count} ({string.Join(", ", caught.Select(q => q.Id))})");
    foreach (var q in latest.Unanswerable.OrderBy(q => q.BestSimilarityTop8))
    {
        Console.WriteLine($"    {q.Id,-7} {Metrics.Format(q.BestSimilarityTop8 ?? 0)}");
    }

    return 0;
}

if (command.Spends && !command.ConfirmSpend)
{
    var estimate = command.Name == "answers" ? "about $0.7 (30 Sonnet calls + Haiku judge)" : "about $0.02 (1 Sonnet call)";
    Console.WriteLine($"`{command.Name}` calls the Anthropic API: {estimate}. Re-run with --confirm-spend.");
    return 3;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
// The API's user-secrets hold the connection string (R4.13); the Anthropic key comes from ANTHROPIC_API_KEY.
builder.Configuration.AddUserSecrets("reglens-api");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    [$"{SearchOptions.SectionName}:{nameof(SearchOptions.Mode)}"] = command.Config.Mode.ToString(),
    [$"{SearchOptions.SectionName}:{nameof(SearchOptions.KeywordScoring)}"] = command.Config.Scoring.ToString(),
    [$"{AnswerOptions.SectionName}:{nameof(AnswerOptions.SimilarityFloor)}"] = command.Floor?.ToString(CultureInfo.InvariantCulture),
}.Where(kv => kv.Value is not null));
// No Redis connection string here: every question is really asked, with a fresh memory-only cache (P4).
builder.Configuration["ConnectionStrings:" + InfrastructureServiceCollectionExtensions.RedisConnectionStringName] = "";
builder.Services.AddRegLensApplication().AddRegLensInfrastructure(builder.Configuration).AddRegLensAnswers(builder.Configuration);
builder.Services.AddSingleton(provider => new RecordingPassageReader(new PostgresPassageReader(provider.GetRequiredService<NpgsqlDataSource>())));
builder.Services.AddSingleton<IPassageReader>(provider => provider.GetRequiredService<RecordingPassageReader>());
builder.Services.AddSingleton(provider => new Judge(new DropTopP(
    provider.GetRequiredService<AnthropicClient>().AsIChatClient(builder.Configuration["Judge:Model"] ?? "claude-haiku-4-5"))));
builder.Services.AddTransient<RetrievalEval>().AddTransient<AnswerEval>();

using var host = builder.Build();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var services = host.Services;
var retrieval = services.GetRequiredService<RetrievalEval>();
var missing = await retrieval.MissingReferencesAsync(golden, cancellation.Token).ConfigureAwait(false);
if (missing.Count > 0)
{
    await Console.Error.WriteLineAsync("Expected references not in the index:\n  " + string.Join("\n  ", missing)).ConfigureAwait(false);
    return 1;
}

// A run is only comparable if the whole index was built one way (spec 0005, R5.15).
var pipelines = await IndexPipelinesAsync(services.GetRequiredService<NpgsqlDataSource>(), cancellation.Token).ConfigureAwait(false);
if (pipelines.Count != 1)
{
    await Console.Error.WriteLineAsync(
        $"The index mixes pipelines ({string.Join(" | ", pipelines)}); re-ingest so every document has the same one.").ConfigureAwait(false);
    return 1;
}

var info = new RunInfo(command.Name, command.Config.Name, DateTimeOffset.UtcNow, GitCommit(),
    builder.Configuration["Ollama:EmbeddingModel"] ?? "?", golden.Version.ToString(CultureInfo.InvariantCulture), pipelines[0]);

switch (command.Name)
{
    case "check":
        Console.WriteLine($"Golden set v{golden.Version}: {golden.Questions.Count} questions, every expected reference is in the index.");
        Console.WriteLine($"Pipeline: {info.Pipeline}");
        return 0;

    case "retrieval":
        var retrievalRun = await retrieval.RunAsync(golden, info, cancellation.Token).ConfigureAwait(false);
        Console.WriteLine($"{info.Config}: recall@5 {Metrics.Format(retrievalRun.RecallAt5, 2)}, recall@8 {Metrics.Format(retrievalRun.RecallAt8, 2)}, MRR@10 {Metrics.Format(retrievalRun.MrrAt10)} (n = {retrievalRun.N})");
        Console.WriteLine($"Saved {Report.Save(retrievalRun, info)}; wrote {Report.Write()}");
        return 0;

    case "answers":
        var answerRun = await services.GetRequiredService<AnswerEval>()
            .RunAsync(golden, info, Console.WriteLine, cancellation.Token).ConfigureAwait(false);
        Console.WriteLine($"Abstained {answerRun.UnanswerableAbstained}/10 unanswerable, {answerRun.AnswerableAbstained}/20 answerable; cited expected {answerRun.AnsweredCitingExpected}/{answerRun.AnsweredAnswerable}; ${Metrics.Format(answerRun.CostUsd, 3)}");
        await File.WriteAllTextAsync(JudgeCheckPath, AnswerEval.JudgeCheckSheet(answerRun), cancellation.Token).ConfigureAwait(false);
        Console.WriteLine($"Saved {Report.Save(answerRun, info)}; wrote {Report.Write()} and {JudgeCheckPath}");
        return 0;

    case "probe":
        var passages = services.GetRequiredService<RecordingPassageReader>();
        passages.PlantCanary = true;
        var probed = await services.GetRequiredService<AnswerQuestion>().RunAsync(ProbeQuestion, RetrievalEval.AskTopK, cancellation.Token).ConfigureAwait(false);
        var model = services.GetRequiredService<IOptions<AnswerOptions>>().Value.Model;
        var leaked = probed.Answer.Contains(RecordingPassageReader.Canary, StringComparison.OrdinalIgnoreCase)
            || probed.MissingInfo.Contains(RecordingPassageReader.Canary, StringComparison.OrdinalIgnoreCase);
        var probeRun = new ProbeRun(info, ProbeQuestion, !leaked,
            probed.Citations.Any(c => c.ChunkId == RecordingPassageReader.CanaryChunkId), probed.Abstained, probed.Answer,
            Pricing.Cost(model, probed.Usage?.InputTokens ?? 0, probed.Usage?.OutputTokens ?? 0));
        Console.WriteLine($"Probe {(probeRun.Passed ? "passed" : "FAILED")}: {probed.Answer}");
        Console.WriteLine($"Saved {Report.Save(probeRun, info)}; wrote {Report.Write()}");
        return probeRun.Passed ? 0 : 1;

    default:
        return 2;
}

static async Task<List<string>> IndexPipelinesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
{
    await using var command = dataSource.CreateCommand("SELECT DISTINCT pipeline FROM documents ORDER BY pipeline");
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    var pipelines = new List<string>();
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
        pipelines.Add(reader.GetString(0));
    }

    return pipelines;
}

static string GitCommit()
{
    try
    {
        using var git = Process.Start(new ProcessStartInfo("git", "describe --always --dirty")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var commit = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        return git.ExitCode == 0 ? commit : "unknown";
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return "unknown";
    }
}
