namespace AppTemplate.PresentationApp.Common.Hosting;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public Uri? BaseAddress { get; set; }
}
