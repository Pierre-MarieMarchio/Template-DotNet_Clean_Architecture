namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>
/// The five raw values a caller sends for any paginated collection, before any of them has been
/// checked against a feature's own whitelist.
/// <para>
/// An interface rather than a base record, and deliberately: a feature's query type is the HTTP
/// contract — it is model-bound from the query string and its <c>param</c> documentation is what the
/// OpenAPI document publishes for each parameter — so it keeps its own positional members and
/// declares this instead. What a feature filters by is its own and is not here.
/// </para>
/// </summary>
public interface ICollectionQuery
{
    /// <summary>"offset" or "cursor". Blank means offset.</summary>
    string? Paging { get; }

    /// <summary>1-based page number. Offset mode only.</summary>
    int? Page { get; }

    /// <summary>How many rows to answer with, bounded by the feature's own ceiling.</summary>
    int? PageSize { get; }

    /// <summary>Opaque, minted by a previous page. Cursor mode only.</summary>
    string? Cursor { get; }

    /// <summary>Comma-separated sort terms, bounded by the feature's own whitelist.</summary>
    string? Sort { get; }
}
