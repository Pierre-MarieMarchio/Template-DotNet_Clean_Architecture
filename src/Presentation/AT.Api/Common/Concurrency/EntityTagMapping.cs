using System.Buffers.Binary;
using System.Buffers.Text;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

internal static class EntityTagMapping
{
    private const int _versionByteCount = sizeof(uint);

    internal static string From(uint version)
    {
        Span<byte> bytes = stackalloc byte[_versionByteCount];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, version);

        return string.Concat("\"", Base64Url.EncodeToString(bytes), "\"");
    }

    internal static bool TryReadVersion(EntityTagHeaderValue tag, out uint version)
    {
        version = 0;

        if (tag is null || tag.IsWeak)
        {
            return false;
        }

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
