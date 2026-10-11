using System.Buffers.Binary;
using System.Buffers.Text;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

/// <summary>The <c>ETag</c> a versioned aggregate publishes, and the reverse reading of one a caller sends back.</summary>
/// <remarks><b>Strong, not weak:</b> the value behind the tag is the aggregate's exact version, so
/// two representations carrying the same tag are byte-for-byte the same — what <c>If-Match</c>
/// requires, since RFC 9110 compares it with the strong function, under which a weak tag never
/// matches anything. <b>Why it is encoded rather than printed:</b> the version is PostgreSQL's
/// <c>xmin</c>, and a client that saw <c>"12345"</c> would eventually treat it as a number, which a
/// wrapping transaction id is not. Encoding keeps the tag opaque, and nothing outside this class
/// knows the layout.</remarks>
internal static class EntityTagMapping
{
    private const int _versionByteCount = sizeof(uint);

    /// <summary>The quoted form, ready to be assigned to an <c>ETag</c> header.</summary>
    internal static string From(uint version)
    {
        Span<byte> bytes = stackalloc byte[_versionByteCount];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, version);

        return string.Concat("\"", Base64Url.EncodeToString(bytes), "\"");
    }

    /// <summary>The version a caller's entity tag names.</summary>
    /// <returns><c>false</c> for a weak tag, and for any tag this API could not have issued — both
    /// well-formed values that cannot match, a failed precondition rather than a malformed
    /// request.</returns>
    internal static bool TryReadVersion(EntityTagHeaderValue tag, out uint version)
    {
        version = 0;

        if (tag is null || tag.IsWeak)
        {
            return false;
        }

        // Tag keeps the surrounding quotes, which are syntax rather than content.
        string quoted = tag.Tag.ToString();

        if (quoted.Length < 2)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[_versionByteCount];

        if (!Base64Url.TryDecodeFromChars(quoted.AsSpan(1, quoted.Length - 2), bytes, out int decoded)
            || decoded != _versionByteCount)
        {
            return false;
        }

        version = BinaryPrimitives.ReadUInt32BigEndian(bytes);

        return true;
    }
}
