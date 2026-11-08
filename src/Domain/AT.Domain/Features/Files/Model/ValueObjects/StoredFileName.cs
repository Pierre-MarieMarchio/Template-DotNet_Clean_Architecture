using System.Buffers;
using System.Collections.Frozen;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record StoredFileName
{
    public const int MaxLength = 255;

    private static readonly SearchValues<char> _forbiddenCharacters = SearchValues.Create("/\\:*?\"<>|");

    private static readonly FrozenSet<string> _reservedStems = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private StoredFileName(string value) => Value = value;

    public string Value { get; }

    public static StoredFileName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A file name cannot be empty.");
        }

        string normalised = value.Trim().TrimEnd(' ', '.');

        if (normalised.Length == 0)
        {
            throw new DomainException("A file name cannot consist only of dots and spaces.");
        }

        if (normalised.Length > MaxLength)
        {
            throw new DomainException($"A file name cannot exceed {MaxLength} characters.");
        }

        if (normalised.AsSpan().ContainsAny(_forbiddenCharacters))
        {
            throw new DomainException("A file name cannot contain a path separator or a reserved character.");
        }

        foreach (char character in normalised)
        {
            if (char.IsControl(character))
            {
                throw new DomainException("A file name cannot contain a control character.");
            }
        }

        int extensionStart = normalised.IndexOf('.');
        string stem = extensionStart < 0 ? normalised : normalised[..extensionStart];

        if (_reservedStems.Contains(stem))
        {
            throw new DomainException($"'{stem}' is a reserved device name and cannot be used as a file name.");
        }

        return new StoredFileName(normalised);
    }

    public override string ToString() => Value;
}
