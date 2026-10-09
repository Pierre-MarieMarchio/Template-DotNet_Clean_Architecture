namespace AppTemplate.Application.Features.Files.Options;

/// <summary>
/// What one owner may hold in one category. Counted per (owner, category); pending registrations
/// count with their declared size.
/// </summary>
public sealed class FileCategoryQuotaOptions
{
    /// <summary>
    /// A check that must hold under concurrent registrations of one owner. Accepted, and applied
    /// like <see cref="IndicativeMode"/> until registrations take a lock.
    /// </summary>
    public const string StrictMode = "Strict";

    /// <summary>
    /// Usage is read, then the row inserted, with no lock: concurrent registrations of one owner in
    /// one category may overshoot by at most one file per extra concurrent request.
    /// </summary>
    public const string IndicativeMode = "Indicative";

    /// <summary><see cref="StrictMode"/> or <see cref="IndicativeMode"/>, compared ignoring case.</summary>
    public string Mode { get; set; } = IndicativeMode;

    /// <summary>Files one owner may keep, in any state.</summary>
    public int MaxFiles { get; set; } = 1_000;

    /// <summary>Bytes one owner may hold, stored and promised. 10 GiB by default.</summary>
    public long MaxBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Registrations one owner may have waiting for their bytes.</summary>
    public int MaxPendingUploads { get; set; } = 20;

    internal static bool IsKnownMode(string? mode) =>
        string.Equals(mode, StrictMode, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, IndicativeMode, StringComparison.OrdinalIgnoreCase);
}
