using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

/// <summary>
/// The <c>bigint</c> a PostgreSQL advisory lock is taken on, derived from a name.
/// </summary>
/// <remarks>
/// SHA-256 truncated to eight bytes, read big-endian. <c>string.GetHashCode()</c> is randomised per
/// process and <c>BitConverter.ToInt64</c> follows the machine's endianness: either would give two
/// hosts two keys for one name, and the lock would serialise nothing while looking healthy.
/// </remarks>
internal static class AdvisoryLockKey
{
    /// <summary>The key for <paramref name="name"/>, the same on every host and every release.</summary>
    internal static long Of(string name)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), digest);

        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }
}
