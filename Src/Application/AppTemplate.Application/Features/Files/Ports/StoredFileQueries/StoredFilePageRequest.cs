using AppTemplate.Application.Common.Collections;

namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>
/// The only collection shape <see cref="IStoredFileQueries"/> accepts. Unconstructible without
/// having gone through <see cref="PageRequest.Create"/>, <see cref="SortOrder.Parse"/> and
/// <see cref="StoredFileFilter.Create"/>, so the read side never re-validates what already cleared
/// the whitelist.
/// </summary>
public sealed record StoredFilePageRequest
{
    private StoredFilePageRequest(PageRequest paging, SortOrder sort, StoredFileFilter filter)
    {
        Paging = paging;
        Sort = sort;
        Filter = filter;
    }

    public PageRequest Paging { get; }

    public SortOrder Sort { get; }

    public StoredFileFilter Filter { get; }

    internal static StoredFilePageRequest Of(PageRequest paging, SortOrder sort, StoredFileFilter filter) =>
        new(paging, sort, filter);
}
