using System.Text;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// What the leading bytes of a file say it is. A table of constants and the two lookups over it, with
/// no dependency on anything.
/// <para>
/// <b>It is a table, not a package.</b> Every format this template's own documentation claims — an
/// image, a sound, a document — is identified by between two and eight bytes at offset zero, and the
/// whole of that knowledge fits below. A file-type detection library would be a supply-chain
/// dependency, a version to track and an advisory surface, bought to hold thirty constants that have
/// not changed in twenty years.
/// </para>
/// <para>
/// It lives beside <see cref="StoredFileContentPolicy"/> rather than in the module that reads the
/// bytes, because it is the policy's data: what counts as a disagreement is decided here, in the
/// layer that can be tested without a bucket, and an adapter only has to hand over the prefix.
/// </para>
/// </summary>
internal static class MediaTypeSignatures
{
    /// <summary>
    /// Signatures that identify exactly one media type, and the type each one identifies.
    /// <para>
    /// <b>Exclusivity is the entry condition, and it is what keeps the ZIP magic out.</b>
    /// <c>PK\x03\x04</c> is the start of a ZIP archive — and of every Office document, every
    /// OpenDocument file, every EPUB and every JAR. Reading it as <c>application/zip</c> would make
    /// this table refuse an honestly declared <c>.docx</c>, which is a false refusal of a real user's
    /// real file; leaving it out costs nothing, because a container declared as an image is still
    /// refused by the rule that an image must carry an image's signature.
    /// </para>
    /// <para>
    /// Ordered longest-first, so a prefix of a longer signature cannot claim a file the longer one
    /// describes.
    /// </para>
    /// </summary>
    private static readonly (byte[] Magic, string MediaType)[] _signatures =
    [
        ([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], "image/png"),
        ([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7', (byte)'a'], "image/gif"),
        ([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a'], "image/gif"),
        ([(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-'], "application/pdf"),
        ([(byte)'O', (byte)'g', (byte)'g', (byte)'S'], "audio/ogg"),
        ([(byte)'f', (byte)'L', (byte)'a', (byte)'C'], "audio/flac"),
        ([0xFF, 0xD8, 0xFF], "image/jpeg"),
        ([(byte)'I', (byte)'D', (byte)'3'], "audio/mpeg"),
        ([(byte)'B', (byte)'M'], "image/bmp"),
    ];

    /// <summary>
    /// The markup that makes a file a program rather than a document, recognised from its own text
    /// because none of these formats has a byte signature.
    /// <para>
    /// This is the half of the table that closes the gap <c>SECURITY.md</c> calls the one most likely
    /// to be exploited. An SVG is XML, so it starts with whatever whitespace, byte-order mark,
    /// declaration, doctype or comment its author felt like — which is exactly why it cannot be
    /// caught by a signature at offset zero and has to be searched for in the prefix instead.
    /// </para>
    /// </summary>
    private static readonly string[] _scriptContainerMarkup =
    [
        "<svg",
        "<html",
        "<!doctype html",
        "<script",
    ];

    /// <summary>
    /// The media type <paramref name="head"/> identifies, or <c>null</c> when it matches no signature
    /// this table holds.
    /// <para>
    /// <c>null</c> means "no opinion" and never "it is what it claims to be". Most text formats carry
    /// no signature at all, so the caller must not read an absence here as agreement.
    /// </para>
    /// </summary>
    internal static string? DetectedMediaTypeOf(ReadOnlySpan<byte> head)
    {
        foreach ((byte[] magic, string mediaType) in _signatures)
        {
            if (head.StartsWith(magic))
            {
                return mediaType;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this table knows a signature for <paramref name="mediaType"/> — that is, whether a
    /// file honestly declared as it would have to start with recognisable bytes.
    /// <para>
    /// This is what lets a declared type be checked even when the content matched nothing: a file
    /// claiming <c>image/png</c> whose head is unrecognisable is not a PNG, whatever else it may be.
    /// </para>
    /// </summary>
    internal static bool IsRecognisable(string mediaType) =>
        Array.Exists(_signatures, signature => string.Equals(signature.MediaType, mediaType, StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="head"/> is the start of a document a browser would execute.
    /// <para>
    /// Searched case-insensitively over the prefix rather than matched at an offset, and searched as
    /// ASCII: XML declares its own encoding, but every character of the markup being looked for is
    /// below 0x80 in each of the encodings an XML document may use. Bytes that are not valid ASCII
    /// become a placeholder rather than being rejected, so a binary file whose head happens to
    /// contain no markup simply does not match.
    /// </para>
    /// <para>
    /// Null bytes are dropped first, which is what makes the search see a UTF-16 document. Encoded
    /// that way <c>&lt;svg</c> is <c>&lt;\0s\0v\0g\0</c> — the same characters with a null between
    /// each — and a search that did not drop them would find nothing while a browser rendered the
    /// file perfectly well. It costs one pass over a kibibyte and closes the cheapest evasion there
    /// is.
    /// </para>
    /// </summary>
    internal static bool IsScriptContainer(ReadOnlySpan<byte> head)
    {
        string text = Encoding.ASCII.GetString(head).Replace("\0", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return Array.Exists(_scriptContainerMarkup, markup => text.Contains(markup, StringComparison.Ordinal));
    }
}
