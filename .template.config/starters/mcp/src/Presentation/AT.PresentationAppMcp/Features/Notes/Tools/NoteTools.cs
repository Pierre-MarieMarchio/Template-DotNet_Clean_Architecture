using System.ComponentModel;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.PresentationAppMcp.Common.Security;
using AppTemplate.PresentationAppMcp.Common.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AppTemplate.PresentationAppMcp.Features.Notes.Tools;

[McpServerToolType]
internal sealed class NoteTools(INoteService notes)
{
    [McpServerTool(Name = "list_notes", Title = "List notes", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Lists the caller's own notes, a page at a time: each note's id, title and dates, without its body.")]
    public async Task<CallToolResult> ListNotesAsync(
        [Description("The page to return, from 1. The first page when omitted.")] int? page = null,
        [Description("How many notes a page holds. The API's default when omitted.")] int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await notes.ListAsync(NoteListQueryDto.Offset(page, pageSize), cancellationToken), notesPage => notesPage);

    [McpServerTool(Name = "get_note", Title = "Get a note", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Gets one of the caller's notes, with its body. A note that is not the caller's answers as one that does not exist.")]
    public async Task<CallToolResult> GetNoteAsync(
        [Description("The note's id, as list_notes gives it.")] Guid noteId,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await notes.GetAsync(noteId, cancellationToken), note => note.Value);

    [McpServerTool(Name = "create_note", Title = "Create a note", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Write)]
    [Description("Creates a note owned by the caller and returns it.")]
    public async Task<CallToolResult> CreateNoteAsync(
        [Description("The note's title.")] string title,
        [Description("The note's body, plain text.")] string body,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await notes.CreateAsync(new NoteContentDto(title, body), cancellationToken), note => note.Value);
}
