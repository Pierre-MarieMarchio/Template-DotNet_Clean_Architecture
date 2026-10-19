using System.Globalization;
using System.Text;

namespace AppTemplate.Application.Features.Files.Model.Policies;

/// <summary>
/// How a file's content is offered to a browser: always as an attachment, never inline. The media
/// type is the uploader's unverified claim, and rendering it would let them choose what a browser
/// executes.
/// </summary>
public static class DownloadDispositionPolicy
{
    /// <summary>
    /// The <c>Content-Disposition</c> value for <paramref name="fileName"/>: a quoted ASCII fallback
    /// every client understands, and an RFC 5987 <c>filename*</c> carrying the real name in UTF-8
    /// (RFC 6266). Characters that could end the quoted string are replaced, not escaped.
    /// </summary>
    public static string AttachmentFor(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var fallback = new StringBuilder(fileName.Length);

        foreach (char character in fileName)
        {
            fallback.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ' '
                ? character
                : '_');
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");
    }
}
