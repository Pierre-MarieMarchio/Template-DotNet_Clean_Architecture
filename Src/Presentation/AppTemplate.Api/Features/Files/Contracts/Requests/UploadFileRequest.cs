namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>
/// The fields of a <c>multipart/form-data</c> upload. Every field is optional to the binder so that
/// a missing one is reported by name rather than as an unreadable form.
/// </summary>
/// <param name="Category">One of the categories configured in <c>Files:Categories</c>.</param>
/// <param name="File">The content. Its part's <c>Content-Type</c> is the declared media type.</param>
/// <param name="Name">A label to show and to offer as a download name; the part's file name when
/// absent or blank. It addresses nothing.</param>
public sealed record UploadFileRequest(
    string? Category,
    IFormFile? File,
    string? Name);
