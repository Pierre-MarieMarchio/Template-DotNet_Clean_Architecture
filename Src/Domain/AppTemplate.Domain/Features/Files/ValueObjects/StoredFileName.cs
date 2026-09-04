using System.Buffers;
using System.Collections.Frozen;
using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The name a client gave a file, kept so it can be shown back and offered as a download name.
/// <para>
/// <b>This is a label. It never reaches a filesystem, and it never addresses anything.</b> The bytes
/// are addressed by <see cref="ObjectKey"/>, which is minted independently and derives nothing from
/// this value — so no name a caller can write here decides which object is read or written. That
/// separation is what makes the whole feature safe, and it is the property to preserve: the day
/// something joins this value onto a path or onto a key, every rule below becomes the only thing
/// standing between a caller and someone else's bytes, and a list of rules is a far weaker defence
/// than an architecture in which the value is never used that way.
/// </para>
/// <para>
/// The rules exist anyway, because the value does end up somewhere hostile: in a
/// <c>Content-Disposition</c> header, in an archive a client builds, and finally on the disk of
/// whoever saves it. A name is refused here — where the refusal is visible and attributable — rather
/// than truncated, mangled or misinterpreted later by something that will not report it.
/// </para>
/// </summary>
public sealed record StoredFileName
{
    /// <summary>
    /// One path component is capped at 255 bytes or characters by essentially every filesystem a
    /// downloaded file could land on. A longer name is not rejected downstream, it is silently
    /// truncated by whatever saves it, and two files then collide under one name.
    /// </summary>
    public const int MaxLength = 255;

    /// <summary>
    /// The two path separators, plus the characters Windows refuses in a filename. The separators
    /// are the load-bearing entries: without one, no arrangement of dots can leave a directory,
    /// which is why an embedded <c>..</c> is not itself forbidden — refusing it would reject
    /// <c>archive..2026.zip</c> to prevent nothing. The rest are refused because a name containing
    /// them cannot be saved on a common platform, and finding that out at save time is worse than
    /// finding it out at upload time.
    /// </summary>
    private static readonly SearchValues<char> _forbiddenCharacters = SearchValues.Create("/\\:*?\"<>|");

    /// <summary>
    /// Device names that Windows resolves before it ever looks at the directory, with or without an
    /// extension: saving as <c>NUL.txt</c> writes to the null device and the user's file is gone
    /// with no error anywhere. Matched on the stem, case-insensitively, exactly as Windows does.
    /// </summary>
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

        // Trailing dots go with the surrounding whitespace: Windows strips both when it creates the
        // file and reports nothing, so "report.txt " and "report.txt" are already one name by the
        // time anyone could notice. Normalising here makes them one name in this system's equality
        // too — and incidentally leaves nothing of "." or "..", which are names for a directory
        // rather than for a file.
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
            // Includes the NUL byte, which truncates the name in any consumer that hands it to a C
            // API, and the newline, which would let a name inject a second header line into the
            // Content-Disposition it is written to.
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
