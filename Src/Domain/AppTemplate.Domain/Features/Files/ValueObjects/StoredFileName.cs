using System.Buffers;
using System.Collections.Frozen;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The name a client gave a file, kept to be shown back and offered as a download name.
/// <para>
/// It is a label. It never reaches a filesystem and never addresses anything: bytes are addressed by
/// <see cref="ObjectKey"/>, minted independently. Keep it that way; the day this value is joined onto a path or a
/// key, the rules below become the only defence. They exist anyway because the value ends up in a
/// <c>Content-Disposition</c> header and on a user's disk, and a refusal here is visible where a truncation later is
/// not.
/// </para>
/// </summary>
public sealed record StoredFileName
{
    /// <summary>
    /// Essentially every filesystem caps a path component at 255; a longer name is silently truncated by whatever
    /// saves it, and two files then collide.
    /// </summary>
    public const int MaxLength = 255;

    // The path separators plus the characters Windows refuses. Separators are the load-bearing entries: without one
    // no arrangement of dots can leave a directory, so an embedded .. is not itself forbidden
    // (archive..2026.zip is fine).
    private static readonly SearchValues<char> _forbiddenCharacters = SearchValues.Create("/\\:*?\"<>|");

    // Windows device names resolve before the directory is consulted, with or without an extension (NUL.txt
    // writes to the null device). Matched on the stem, case-insensitively.
    private static readonly FrozenSet<string> _reservedStems = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private StoredFileName(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="DomainException">The value is blank, too long, or unsafe to offer as a
    /// filename.</exception>
    public static StoredFileName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A file name cannot be empty.");
        }

        // Trailing dots go with the whitespace: Windows strips both, and nothing is left of "." or "..".
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
            // NUL truncates the name for C APIs; a newline would inject a Content-Disposition header line.
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
