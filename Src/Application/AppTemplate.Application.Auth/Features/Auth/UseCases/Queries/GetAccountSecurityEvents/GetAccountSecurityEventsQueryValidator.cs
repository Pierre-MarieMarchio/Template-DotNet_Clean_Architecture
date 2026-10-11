using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetAccountSecurityEvents;

/// <summary>An empty id is refused here so it never reaches the store as a lookup that misses.</summary>
public sealed class GetAccountSecurityEventsQueryValidator : AbstractValidator<GetAccountSecurityEventsQuery>
{
    /// <summary>The target id is required.</summary>
    public GetAccountSecurityEventsQueryValidator() => RuleFor(x => x.UserId).NotEmpty();
}
