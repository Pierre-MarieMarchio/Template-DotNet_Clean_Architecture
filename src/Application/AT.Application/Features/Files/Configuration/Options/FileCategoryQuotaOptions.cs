namespace AppTemplate.Application.Features.Files.Configuration.Options;

public sealed class FileCategoryQuotaOptions
{
    public const string StrictMode = "Strict";

    public const string IndicativeMode = "Indicative";

    public string Mode { get; set; } = StrictMode;

    public int MaxFiles { get; set; } = 1_000;

    public long MaxBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    public int MaxPendingUploads { get; set; } = 20;

    public bool IsStrict => string.Equals(Mode, StrictMode, StringComparison.OrdinalIgnoreCase);

    internal static bool IsKnownMode(string? mode) =>
        string.Equals(mode, StrictMode, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, IndicativeMode, StringComparison.OrdinalIgnoreCase);
}
