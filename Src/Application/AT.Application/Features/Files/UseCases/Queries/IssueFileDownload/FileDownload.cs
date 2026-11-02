using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

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

    public IssuedDownloadGrant? Redirect { get; }

    public Stream? Content { get; }

    public string FileName { get; }

    public string MediaType { get; }

    public long SizeInBytes { get; }

    public static FileDownload RedirectTo(IssuedDownloadGrant grant, string fileName, string mediaType, long sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new FileDownload(grant, null, fileName, mediaType, sizeInBytes);
    }

    public static FileDownload Streamed(Stream content, string fileName, string mediaType, long sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new FileDownload(null, content, fileName, mediaType, sizeInBytes);
    }
}
