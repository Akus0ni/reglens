using System.Security.Cryptography;
using System.Text;

namespace RegLens.Domain.Chunking;

/// <summary>
/// Identity of a chunk: SHA-256 of document id, paragraph reference and content (spec 0001, P4).
/// Including the document and paragraph keeps identical boilerplate in two documents as two chunks.
/// </summary>
public readonly record struct ContentHash(string Value)
{
    public static ContentHash Of(string documentId, string paraRef, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paraRef);
        ArgumentNullException.ThrowIfNull(content);

        var bytes = Encoding.UTF8.GetBytes($"{documentId}\n{paraRef}\n{content}");
        return new ContentHash(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public override string ToString() => Value;
}
