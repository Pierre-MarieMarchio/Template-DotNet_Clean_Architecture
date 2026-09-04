using System.Globalization;
using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// How many bytes a file has. Declared by the client when the file is registered, and compared
/// against what the object store reports before the file becomes available — so this type is what a
/// size is checked <em>as</em>, not merely what it is stored in.
/// </summary>
public sealed record FileSize
{
    /// <summary>
    /// A zero-byte object and an upload that never happened look identical to the object store. The
    /// whole point of registering before depositing is to be able to tell those two apart, so a
    /// declared size of zero is refused rather than becoming a file that can never be confirmed.
    /// </summary>
    public const long MinBytes = 1;

    /// <summary>
    /// 5 GiB, which is the protocol's own ceiling on a single-part <c>PUT</c>. This feature offers
    /// one signed upload URL and therefore exactly one part; anything larger needs a multipart
    /// session, which is a different port with a different lifecycle, and accepting the registration
    /// would mean promising a deposit that cannot physically be made.
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
