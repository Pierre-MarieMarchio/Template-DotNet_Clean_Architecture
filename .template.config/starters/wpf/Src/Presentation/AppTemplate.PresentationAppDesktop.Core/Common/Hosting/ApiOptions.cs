namespace AppTemplate.PresentationApp.Core.Common.Hosting;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public Uri? BaseAddress { get; set; }
}
