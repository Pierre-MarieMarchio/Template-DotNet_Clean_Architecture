namespace AppTemplate.PresentationApp.Common.Hosting;

public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string? KeysDirectory { get; set; }
}
