using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppDesktop.Core.Common.Hosting;

internal sealed class ApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiOptions options) =>
        options.BaseAddress is { IsAbsoluteUri: true, Scheme: "http" or "https" }
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{ApiOptions.SectionName}:BaseAddress is required: the API's absolute http or https address.");
}
