namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>
/// How a caller names the page they want, which decides what the answer can tell them about the rest
/// of the collection.
/// </summary>
public enum PagingMode
{
    /// <summary><c>page</c>/<c>pageSize</c>, answering a total count.</summary>
    Offset,

    /// <summary>An opaque cursor minted by the previous page, answering the next one.</summary>
    Cursor,
}
