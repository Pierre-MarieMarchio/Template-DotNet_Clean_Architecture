using System.Globalization;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// How many bytes a file has: declared at registration, and compared with what the store reports before the file
/// becomes available.
/// </summary>
public sealed record FileSize
{
    /// <summary>
    /// A zero-byte object looks like an upload that never happened, and telling those apart is why files are
    /// registered before they are deposited; zero is refused.
    /// </summary>
    public const long MinBytes = 1;

    /// <summary>
    /// 5 GiB, the ceiling of a single-part <c>PUT</c>. Registration offers one signed URL, so a larger file could not
    /// physically be deposited.
    /// </summary>
    public const long MaxBytes = 5L * 1024 * 1024 * 1024;

    private FileSize(long bytes) => Bytes = bytes;

    public long Bytes { get; }

    /// <exception cref="DomainException">The size is outside what a single deposit can carry.</exception>
    public static FileSize Create(long bytes)
    {
        if (bytes < MinBytes)
        {
            throw new DomainException($"A file must be at least {MinBytes} byte.");
        }

        if (bytes > MaxBytes)
        {
            throw new DomainException($"A file cannot exceed {MaxBytes} bytes.");
        }

        return new FileSize(bytes);
    }

    public override string ToString() => Bytes.ToString(CultureInfo.InvariantCulture);
}
