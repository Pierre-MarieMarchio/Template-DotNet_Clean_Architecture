using System.Globalization;
using System.Text;

namespace AppTemplate.Application.Features.Files.Model.Policies;

public static class DownloadDispositionPolicy
{
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
