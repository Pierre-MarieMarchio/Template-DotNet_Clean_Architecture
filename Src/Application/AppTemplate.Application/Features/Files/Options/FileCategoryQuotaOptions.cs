namespace AppTemplate.Application.Features.Files.Options;

/// <summary>
/// What one owner may hold in one category. Counted per (owner, category); pending registrations
/// count with their declared size.
/// </summary>
public sealed class FileCategoryQuotaOptions
{
    /// <summary>
    /// The quota holds under concurrency, across every host sharing the database: the usage read, the
    /// insert and its commit run under a lock on (owner, category). Costs one extra database
    /// connection for the registration's duration and serialises one owner's registrations in one
    /// category.
    /// </summary>
    public const string StrictMode = "Strict";

    /// <summary>
    /// Usage is read, then the row inserted, with no lock: a burst of N concurrent registrations of
    /// one owner in one category can overshoot by at most N - 1 files and their declared bytes.
    /// </summary>
    public const string IndicativeMode = "Indicative";

    /// <summary><see cref="StrictMode"/> or <see cref="IndicativeMode"/>, compared ignoring case.</summary>
    public string Mode { get; set; } = StrictMode;

    /// <summary>Files one owner may keep, in any state.</summary>
    public int MaxFiles { get; set; } = 1_000;

    /// <summary>Bytes one owner may hold, stored and promised. 10 GiB by default.</summary>
    public long MaxBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Registrations one owner may have waiting for their bytes.</summary>
    public int MaxPendingUploads { get; set; } = 20;

    /// <summary>Whether registrations in this category take the owner's quota lock.</summary>
    public bool IsStrict => string.Equals(Mode, StrictMode, StringComparison.OrdinalIgnoreCase);

    internal static bool IsKnownMode(string? mode) =>
        string.Equals(mode, StrictMode, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, IndicativeMode, StringComparison.OrdinalIgnoreCase);
}
