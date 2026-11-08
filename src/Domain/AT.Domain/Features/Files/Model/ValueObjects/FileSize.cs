using System.Globalization;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record FileSize
{
    public const long MinBytes = 1;

    public const long MaxBytes = 5L * 1024 * 1024 * 1024;

    private FileSize(long bytes) => Bytes = bytes;

    public long Bytes { get; }

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
