using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// How a file's content is handed over: <see cref="Redirect"/> to a signed URL, or
/// <see cref="Content"/> streamed by the API.
/// </summary>
public sealed class FileDownload
{
    private FileDownload(
        IssuedDownloadGrant? redirect,
        Stream? content,
        string fileName,
        string mediaType,
        long sizeInBytes)
    {
        Redirect = redirect;
        Content = content;
        FileName = fileName;
        MediaType = mediaType;
        SizeInBytes = sizeInBytes;
    }

    /// <summary>The signed URL to redirect to, or <c>null</c> when the content is streamed.</summary>
    public IssuedDownloadGrant? Redirect { get; }

    /// <summary>The opened content, or <c>null</c> for a redirect. Whoever writes it disposes it.</summary>
    public Stream? Content { get; }

    /// <summary>The name to offer the content under.</summary>
    public string FileName { get; }

    /// <summary>The declared media type.</summary>
    public string MediaType { get; }

    /// <summary>The stored size, in bytes.</summary>
    public long SizeInBytes { get; }

    /// <summary>The content is at <paramref name="grant"/>'s URL.</summary>
    public static FileDownload RedirectTo(IssuedDownloadGrant grant, string fileName, string mediaType, long sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new FileDownload(grant, null, fileName, mediaType, sizeInBytes);
    }

    /// <summary>The content is <paramref name="content"/>, to be streamed by the API.</summary>
    public static FileDownload Streamed(Stream content, string fileName, string mediaType, long sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new FileDownload(null, content, fileName, mediaType, sizeInBytes);
    }
}
