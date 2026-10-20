using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.ExternalIdentity.Model.Errors;

public static class ExternalIdentityErrors
{
    public static Error MappingNotFound { get; } = Error.NotFound(
        "externalIdentity.mapping.notFound",
        "No account is known under this id.");
}
