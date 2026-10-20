using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

internal static class AdvisoryLockKey
{
    internal static long Of(string name)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), digest);

        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }
}
