using System.Text;

namespace AppTemplate.Application.Features.Files.Model.Policies;

internal static class MediaTypeSignatures
{
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

    private static readonly string[] _scriptContainerMarkup =
    [
        "<svg",
        "<html",
        "<!doctype html",
        "<script",
    ];

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

    internal static bool IsRecognisable(string mediaType) =>
        Array.Exists(_signatures, signature => string.Equals(signature.MediaType, mediaType, StringComparison.Ordinal));

    internal static bool IsScriptContainer(ReadOnlySpan<byte> head)
    {
        string text = Encoding.ASCII.GetString(head).Replace("\0", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return Array.Exists(_scriptContainerMarkup, markup => text.Contains(markup, StringComparison.Ordinal));
    }

    internal static bool BeginsAsMarkup(ReadOnlySpan<byte> head)
    {
        foreach (byte value in WithoutByteOrderMark(head))
        {
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

    private static readonly byte[][] _byteOrderMarks =
    [
        [0xEF, 0xBB, 0xBF],
        [0xFF, 0xFE],
        [0xFE, 0xFF],
    ];
}
