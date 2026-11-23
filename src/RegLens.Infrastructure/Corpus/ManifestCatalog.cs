using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RegLens.Application.Ingestion;
using RegLens.Domain.Corpus;

namespace RegLens.Infrastructure.Corpus;

/// <summary>
/// Reads <c>corpus/manifest.json</c> and checks every file's size and SHA-256 before anything is written
/// (spec 0001, R1.10). Unknown manifest fields are an error, so a typo cannot silently drop a setting.
/// </summary>
public sealed class ManifestCatalog(IOptions<CorpusOptions> options) : ICorpusCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private static readonly Dictionary<string, DocumentStructure> Structures = new(StringComparer.Ordinal)
    {
        ["rbi-directions"] = DocumentStructure.RbiDirections,
        ["gazette-rules"] = DocumentStructure.GazetteRules,
        ["corrigendum"] = DocumentStructure.Corrigendum,
    };

    public async Task<IReadOnlyList<SourceDocument>> LoadAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.ResolveRoot();
        var manifest = await ReadManifestAsync(root, cancellationToken).ConfigureAwait(false);

        var documents = new List<SourceDocument>(manifest.Documents.Count);
        foreach (var entry in manifest.Documents)
        {
            await VerifyFileAsync(root, entry, cancellationToken).ConfigureAwait(false);
            documents.Add(ToDocument(entry));
        }

        return documents;
    }

    private static async Task<Manifest> ReadManifestAsync(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, CorpusOptions.ManifestFileName);
        if (!File.Exists(path))
        {
            throw new CorpusIntegrityException(
                $"Corpus manifest not found at '{path}'. Run from the repository root or set Corpus__Path.");
        }

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new CorpusIntegrityException($"Corpus manifest '{path}' is empty.");
            }
        }
        catch (JsonException exception)
        {
            throw new CorpusIntegrityException($"Corpus manifest '{path}' is not valid: {exception.Message}", exception);
        }
    }

    private static async Task VerifyFileAsync(string root, ManifestDocument entry, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, entry.File);
        if (!File.Exists(path))
        {
            throw new CorpusIntegrityException($"Document '{entry.Id}': file '{entry.File}' is missing from '{root}'.");
        }

        var bytes = new FileInfo(path).Length;
        if (bytes != entry.Bytes)
        {
            throw new CorpusIntegrityException(
                $"Document '{entry.Id}': '{entry.File}' is {bytes} bytes; the manifest says {entry.Bytes}.");
        }

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CorpusIntegrityException(
                    $"Document '{entry.Id}': SHA-256 of '{entry.File}' is {hash}; the manifest says {entry.Sha256}.");
            }
        }
    }

    private static SourceDocument ToDocument(ManifestDocument entry)
    {
        if (!Structures.TryGetValue(entry.Structure, out var structure))
        {
            throw new CorpusIntegrityException(
                $"Document '{entry.Id}': unknown structure '{entry.Structure}'. Use one of: {string.Join(", ", Structures.Keys)}.");
        }

        PageRange pages;
        try
        {
            pages = entry.IngestPages is null ? new PageRange(1, entry.Pages) : PageRange.Parse(entry.IngestPages);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new CorpusIntegrityException($"Document '{entry.Id}': ingest_pages '{entry.IngestPages}' is not valid.", exception);
        }

        return new SourceDocument(
            entry.Id, entry.File, entry.Title, entry.Reference, entry.Version, entry.SourceUrl,
            entry.Sha256, entry.Bytes, entry.Pages, pages, structure);
    }

    // The manifest's shape. Every field must be known; see corpus/manifest.json.
    private sealed record Manifest(
        string Corpus,
        string Scope,
        string Checked,
        IReadOnlyList<string> NotIncluded,
        IReadOnlyList<ManifestDocument> Documents);

    private sealed record ManifestDocument(
        string Id,
        string File,
        string Title,
        string Issuer,
        string Reference,
        string Issued,
        string Version,
        string SourcePage,
        string SourceUrl,
        string Downloaded,
        long Bytes,
        int Pages,
        string Sha256,
        string Language,
        string Structure,
        string? IngestPages = null,
        string? Corrects = null,
        string? Notes = null);
}
