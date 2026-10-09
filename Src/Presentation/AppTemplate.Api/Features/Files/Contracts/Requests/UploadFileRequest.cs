namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>
/// The fields of a <c>multipart/form-data</c> upload, all nullable so a missing one is reported by
/// name rather than as an unreadable form.
/// </summary>
/// <param name="Category">One of the categories configured in <c>Files:Categories</c>.</param>
/// <param name="File">The content; its part's <c>Content-Type</c> is the declared media type.</param>
/// <param name="Name">A label and download name; the part's file name when absent or blank.</param>
public sealed record UploadFileRequest(
    string? Category,
    IFormFile? File,
    string? Name);
