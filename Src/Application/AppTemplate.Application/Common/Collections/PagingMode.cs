namespace AppTemplate.Application.Common.Collections;

/// <summary>Which pagination strategy a request uses.</summary>
public enum PagingMode
{
    /// <summary><c>page</c>/<c>pageSize</c>, answering a total count.</summary>
    Offset,

    /// <summary>An opaque cursor minted by the previous page, answering the next one.</summary>
    Cursor,
}
