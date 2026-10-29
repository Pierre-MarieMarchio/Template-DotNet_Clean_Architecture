using System.ComponentModel;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.PresentationApp.Common.Security;
using AppTemplate.PresentationApp.Common.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AppTemplate.PresentationApp.Features.Files.Tools;

[McpServerToolType]
internal sealed class FileTools(
    IGetStoredFilesUseCase getStoredFiles,
    IGetStoredFileUseCase getStoredFile)
{
    [McpServerTool(Name = "list_files", Title = "List files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Lists the caller's own stored files, a page at a time: each file's id, name, media type, size, state and tags, never its content.")]
    public async Task<CallToolResult> ListFilesAsync(
        [Description("The page to return, from 1. The first page when omitted.")] int? page = null,
        [Description("How many files a page holds. The API's default when omitted.")] int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await getStoredFiles.ExecuteAsync(GetStoredFilesQuery.Offset(page, pageSize), cancellationToken),
            files => files);

    [McpServerTool(Name = "get_file", Title = "Get a file's details", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Gets the details of one of the caller's stored files, never its content. A file that is not the caller's answers as one that does not exist.")]
    public async Task<CallToolResult> GetFileAsync(
        [Description("The file's id, as list_files gives it.")] Guid storedFileId,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await getStoredFile.ExecuteAsync(new GetStoredFileQuery(storedFileId), cancellationToken),
            file => file.Value);
}
