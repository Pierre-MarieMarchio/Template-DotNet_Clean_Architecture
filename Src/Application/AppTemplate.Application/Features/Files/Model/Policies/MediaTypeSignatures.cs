using System.Text;

namespace AppTemplate.Application.Features.Files.Model.Policies;

/// <summary>
/// What the leading bytes of a file say it is: a table of constants and the lookups over it. A table and not a
/// detection package, since the few formats claimed are identified by two to eight bytes at offset zero. It lives
/// beside <see cref="StoredFileContentPolicy"/> so what counts as a disagreement is testable without a bucket.
/// </summary>
internal static class MediaTypeSignatures
{
    // Signatures that identify exactly one media type, longest first. Exclusivity is the entry condition:
    // PK\x03\x04 also opens every Office, OpenDocument, EPUB and JAR file, so reading it as ZIP would falsely
    // refuse an honest .docx; a container declared as an image is still refused for lacking an image's
    // signature.
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

    // Markup that makes a file a program, searched for in the prefix because it has no byte signature (an SVG may
    // open with anything XML allows). The markup rule is in SECURITY.md.
    private static readonly string[] _scriptContainerMarkup =
    [
        "<svg",
        "<html",
        "<!doctype html",
        "<script",
    ];

    /// <summary>
    /// The media type <paramref name="head"/> identifies, or null for "no opinion", never for "it is what it claims".
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
    /// Whether this table knows a signature for <paramref name="mediaType"/>, so a file declared as it must start
    /// with recognisable bytes.
    /// </summary>
    internal static bool IsRecognisable(string mediaType) =>
        Array.Exists(_signatures, signature => string.Equals(signature.MediaType, mediaType, StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="head"/> contains markup a browser would execute. Searched as ASCII with null bytes
    /// dropped, so a UTF-16 document (a null between each character) is seen too.
    /// </summary>
    internal static bool IsScriptContainer(ReadOnlySpan<byte> head)
    {
        string text = Encoding.ASCII.GetString(head).Replace("\0", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return Array.Exists(_scriptContainerMarkup, markup => text.Contains(markup, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the first meaningful byte of <paramref name="head"/> is <c>&lt;</c>. This is what makes
    /// <see cref="IsScriptContainer"/> unevadable: padding can push a marker past the bounded prefix but not away
    /// from offset zero, since all that XML allows before a root element is whitespace or another tag. It refuses
    /// honest XML too, deliberately; see <c>SECURITY.md</c>.
    /// </summary>
    internal static bool BeginsAsMarkup(ReadOnlySpan<byte> head)
    {
        foreach (byte value in WithoutByteOrderMark(head))
        {
            // UTF-16 padding, as in IsScriptContainer.
            if (value == 0)
            {
                continue;
            }

            if (value is _tab or _lineFeed or _carriageReturn or _space)
            {
                continue;
            }

            return value == _lessThan;
        }

        return false;
    }

    private const byte _tab = 0x09;
    private const byte _lineFeed = 0x0A;
    private const byte _carriageReturn = 0x0D;
    private const byte _space = 0x20;
    private const byte _lessThan = (byte)'<';

    // Strips the mark from the bytes, not a decoded string: an ASCII decode would turn a UTF-8 mark into placeholders
    // the scan stops on, so every marked document would pass as not markup.
    private static ReadOnlySpan<byte> WithoutByteOrderMark(ReadOnlySpan<byte> head)
    {
        foreach (byte[] mark in _byteOrderMarks)
        {
            if (head.StartsWith(mark))
            {
                return head[mark.Length..];
            }
        }

        return head;
    }

    /// <summary>UTF-8, UTF-16 little-endian and UTF-16 big-endian.</summary>
    private static readonly byte[][] _byteOrderMarks =
    [
        [0xEF, 0xBB, 0xBF],
        [0xFF, 0xFE],
        [0xFE, 0xFF],
    ];
}
