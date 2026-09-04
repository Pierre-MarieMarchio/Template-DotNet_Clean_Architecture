using System.Buffers.Binary;
using System.Buffers.Text;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

/// <summary>
/// The <c>ETag</c> a versioned aggregate publishes, and the reverse reading of one a caller sends
/// back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Strong, not weak.</b> The value behind the tag is the aggregate's exact version, so two
/// representations carrying the same tag are byte-for-byte the same representation. That is what a
/// strong validator asserts, and it is what <c>If-Match</c> requires: RFC 9110 compares
/// <c>If-Match</c> with the strong function, under which a weak tag never matches anything.
/// </para>
/// <para>
/// <b>Why it is encoded rather than printed.</b> The version is PostgreSQL's <c>xmin</c>. A client
/// that saw <c>"12345"</c> would eventually treat it as a number — comparing two of them for
/// ordering, or incrementing one — and none of that is true of a transaction id, which wraps around.
/// Encoding it makes the tag what RFC 9110 says an entity tag is: opaque. Nothing outside this class
/// knows the layout, so it can change without any client noticing.
/// </para>
/// </remarks>
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

    /// <summary>
    /// The version a caller's entity tag names.
    /// </summary>
    /// <returns>
    /// <c>false</c> for a weak tag, and for any tag this API could not have issued. Both are
    /// well-formed values that simply cannot match, which is a failed precondition rather than a
    /// malformed request.
    /// </returns>
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
