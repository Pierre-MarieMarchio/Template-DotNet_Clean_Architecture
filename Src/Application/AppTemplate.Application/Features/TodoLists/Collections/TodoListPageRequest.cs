using AppTemplate.Application.Common.Collections;

namespace AppTemplate.Application.Features.TodoLists.Collections;

/// <summary>
/// The only collection shape <see cref="Ports.ITodoListQueries"/> accepts. Unconstructible without
/// having gone through <see cref="PageRequest.Create"/>, <see cref="SortOrder.Parse"/> and
/// <see cref="TodoListFilter.Create"/>, so the read side never has to re-validate what already
/// cleared the whitelist.
/// </summary>
public sealed record TodoListPageRequest
{
    private TodoListPageRequest(PageRequest paging, SortOrder sort, TodoListFilter filter)
    {
        Paging = paging;
        Sort = sort;
        Filter = filter;
    }

    public PageRequest Paging { get; }

    public SortOrder Sort { get; }

    public TodoListFilter Filter { get; }

    internal static TodoListPageRequest Of(PageRequest paging, SortOrder sort, TodoListFilter filter) =>
        new(paging, sort, filter);
}
