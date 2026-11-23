using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RegLens.Application.Ingestion;
using RegLens.Domain.Corpus;
using RegLens.Infrastructure.Corpus;

namespace RegLens.Infrastructure.Tests.Corpus;

/// <summary>Runs against the real corpus/ folder, so the manifest and the committed PDFs are checked on every test run.</summary>
public sealed class CorpusFilesTests : IDisposable
{
    private static readonly string RepoCorpus = FindCorpus();
    private readonly string _tempDir = Directory.CreateTempSubdirectory("reglens-corpus-").FullName;

    [Fact]
    public async Task LoadsEveryDocumentInTheRealManifest()
    {
        var documents = await Catalog(RepoCorpus).LoadAsync(TestContext.Current.CancellationToken);

        documents.Select(d => (d.Id, d.Structure)).Should().BeEquivalentTo(new[]
        {
            ("rbi-nbfc-kyc-2025", DocumentStructure.RbiDirections),
            ("rbi-nbfc-credit-facilities-2025", DocumentStructure.RbiDirections),
            ("rbi-nbfc-responsible-business-conduct-2025", DocumentStructure.RbiDirections),
            ("dpdp-rules-2025", DocumentStructure.GazetteRules),
            ("dpdp-rules-2025-corrigendum", DocumentStructure.Corrigendum),
        });
        documents.Single(d => d.Id == "dpdp-rules-2025").IngestPages.Should().Be(new PageRange(24, 41));
    }

    [Fact]
    public async Task RefusesAFileWhoseHashDoesNotMatch()
    {
        var corpus = CopyCorrigendumCorpus(entry => entry["sha256"] = new string('0', 64));

        var act = () => Catalog(corpus).LoadAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CorpusIntegrityException>().WithMessage("*dpdp-rules-2025-corrigendum*SHA-256*");
    }

    [Fact]
    public async Task RefusesAManifestWithAnUnknownField()
    {
        var corpus = CopyCorrigendumCorpus(entry => entry["ingest_page"] = "1-1");

        var act = () => Catalog(corpus).LoadAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CorpusIntegrityException>().WithMessage("*ingest_page*");
    }

    [Fact]
    public async Task RefusesAManifestWithAMissingField()
    {
        var corpus = CopyCorrigendumCorpus(entry => entry.Remove("structure"));

        var act = () => Catalog(corpus).LoadAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CorpusIntegrityException>();
    }

    [Fact]
    public async Task ReadsOnlyTheIngestPagesWithPdfPig()
    {
        var documents = await Catalog(RepoCorpus).LoadAsync(TestContext.Current.CancellationToken);
        var source = new PdfPigTextSource(Options.Create(new CorpusOptions { Path = RepoCorpus }));

        var rules = source.ReadPages(documents.Single(d => d.Id == "dpdp-rules-2025"));
        var corrigendum = source.ReadPages(documents.Single(d => d.Id == "dpdp-rules-2025-corrigendum"));

        rules.Select(p => p.Number).Should().Equal(Enumerable.Range(24, 18));
        rules[0].Text.Should().Contain("G.S.R. 846(E)");
        corrigendum.Should().ContainSingle().Which.Text.Should().Contain("G.S.R. 892(E)");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static ManifestCatalog Catalog(string path) => new(Options.Create(new CorpusOptions { Path = path }));

    // A one-document corpus (the 1-page corrigendum) with the manifest entry altered by the test.
    private string CopyCorrigendumCorpus(Action<JsonObject> alter)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoCorpus, CorpusOptions.ManifestFileName)))!.AsObject();
        var documents = manifest["documents"]!.AsArray();
        var entry = documents.Single(d => (string)d!["id"]! == "dpdp-rules-2025-corrigendum")!.AsObject();
        alter(entry);
        manifest["documents"] = new JsonArray(entry.DeepClone());

        File.WriteAllText(Path.Combine(_tempDir, CorpusOptions.ManifestFileName), manifest.ToJsonString());
        File.Copy(Path.Combine(RepoCorpus, "dpdp-rules-2025-corrigendum.pdf"), Path.Combine(_tempDir, "dpdp-rules-2025-corrigendum.pdf"));
        return _tempDir;
    }

    private static string FindCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "corpus");
            if (File.Exists(Path.Combine(candidate, CorpusOptions.ManifestFileName)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find corpus/manifest.json above the test output folder.");
    }
}
