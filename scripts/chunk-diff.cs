#!/usr/bin/env dotnet
// Re-chunks the corpus with the current chunker, without a database or any model call (spec 0005, R5.5 / A2).
//   dotnet run scripts/chunk-diff.cs -- dump <out.json>                 the chunks as JSON
//   dotnet run scripts/chunk-diff.cs -- diff <before.json>              what changed against an earlier dump
//   dotnet run scripts/chunk-diff.cs -- lines <document-id> <from> <to> the cleaned lines of some pages
// Export "before" from Postgres with:
//   docker compose exec -T postgres psql -U reglens -d reglens -At -c "select json_agg(json_build_object(
//     'documentId',document_id,'paraRef',para_ref,'heading',heading,'pageStart',page_start,'pageEnd',page_end,
//     'content',content) order by id) from chunks" > before.json
// Run from the repo root (corpus/ is relative).
#:project ../src/RegLens.Infrastructure/RegLens.Infrastructure.csproj
#:property PublishAot=false

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RegLens.Domain.Chunking;
using RegLens.Infrastructure.Corpus;

var corpus = Options.Create(new CorpusOptions());
var documents = await new ManifestCatalog(corpus).LoadAsync(CancellationToken.None);
var source = new PdfPigTextSource(corpus);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

switch (args)
{
    case ["lines", var id, var from, var to]:
        var pages = source.ReadPages(documents.Single(d => d.Id == id));
        foreach (var line in TextCleaner.Clean(pages).Where(l => l.Page >= int.Parse(from, CultureInfo.InvariantCulture) && l.Page <= int.Parse(to, CultureInfo.InvariantCulture)))
        {
            Console.WriteLine($"{line.Page,3} | {line.Text}");
        }

        break;

    case ["dump", var path]:
        File.WriteAllText(path, JsonSerializer.Serialize(ChunkAll(), json));
        Console.WriteLine($"Wrote {path}");
        break;

    case ["diff", var path]:
        var before = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(path), json)!;
        Diff(before, ChunkAll());
        break;

    default:
        Console.Error.WriteLine("usage: dump <out.json> | diff <before.json> | lines <document-id> <from> <to>");
        return 1;
}

return 0;

List<Row> ChunkAll()
{
    var chunker = DocumentChunker.CreateDefault();
    return documents
        .SelectMany(d => chunker.Chunk(d, source.ReadPages(d)))
        .Select(c => new Row(c.DocumentId, c.ParaRef, c.Heading, c.PageStart, c.PageEnd, c.Content))
        .ToList();
}

// Chunks are matched by document and para_ref (with an occurrence number for repeats).
static void Diff(List<Row> before, List<Row> after)
{
    var changes = 0;
    foreach (var document in before.Select(r => r.DocumentId).Union(after.Select(r => r.DocumentId)))
    {
        var old = Keyed(before.Where(r => r.DocumentId == document));
        var now = Keyed(after.Where(r => r.DocumentId == document));
        Console.WriteLine($"== {document}: {old.Count} before, {now.Count} after");

        foreach (var (key, row) in old.Where(o => !now.ContainsKey(o.Key)))
        {
            Console.WriteLine($"  - removed  {key}  [{row.Heading}]  {Snippet(row.Content)}");
            changes++;
        }

        foreach (var (key, row) in now.Where(n => !old.ContainsKey(n.Key)))
        {
            Console.WriteLine($"  + added    {key}  [{row.Heading}]  {Snippet(row.Content)}");
            changes++;
        }

        foreach (var (key, row) in now.Where(n => old.ContainsKey(n.Key)))
        {
            var was = old[key];
            if (was.Heading != row.Heading)
            {
                Console.WriteLine($"  ~ heading  {key}\n      was [{was.Heading}]\n      now [{row.Heading}]");
                changes++;
            }

            if (was.Content != row.Content || was.PageStart != row.PageStart || was.PageEnd != row.PageEnd)
            {
                Console.WriteLine($"  ~ content  {key}  ({was.Content.Length} -> {row.Content.Length} chars, "
                    + $"pp. {was.PageStart}-{was.PageEnd} -> {row.PageStart}-{row.PageEnd})");
                changes++;
            }
        }
    }

    Console.WriteLine($"{changes} change(s); {before.Count} chunks before, {after.Count} after");
}

static Dictionary<string, Row> Keyed(IEnumerable<Row> rows)
{
    var seen = new Dictionary<string, int>();
    var keyed = new Dictionary<string, Row>();
    foreach (var row in rows)
    {
        var n = seen[row.ParaRef] = seen.GetValueOrDefault(row.ParaRef) + 1;
        keyed[n == 1 ? row.ParaRef : $"{row.ParaRef} #{n}"] = row;
    }

    return keyed;
}

static string Snippet(string text)
{
    var flat = text.ReplaceLineEndings(" ");
    return flat.Length <= 90 ? flat : flat[..90] + "…";
}

sealed record Row(string DocumentId, string ParaRef, string Heading, int PageStart, int PageEnd, string Content);
