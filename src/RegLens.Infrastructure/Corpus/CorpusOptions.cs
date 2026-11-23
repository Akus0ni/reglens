using System.ComponentModel.DataAnnotations;

namespace RegLens.Infrastructure.Corpus;

/// <summary>Where the corpus lives. Section "Corpus". A relative path is resolved from the current directory.</summary>
public sealed class CorpusOptions
{
    public const string SectionName = "Corpus";

    public const string ManifestFileName = "manifest.json";

    [Required]
    public string Path { get; set; } = "corpus";

    public string ResolveRoot() => System.IO.Path.GetFullPath(Path);
}
