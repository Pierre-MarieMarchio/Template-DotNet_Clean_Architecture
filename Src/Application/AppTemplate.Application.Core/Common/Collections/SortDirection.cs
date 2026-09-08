namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>Which way a sort term orders — the only two forms the <c>sort</c> parameter spells.</summary>
public enum SortDirection
{
    /// <summary>Smallest first; what a bare field name with no direction token means.</summary>
    Ascending,

    /// <summary>Largest first, spelled <c>field:desc</c>.</summary>
    Descending,
}
