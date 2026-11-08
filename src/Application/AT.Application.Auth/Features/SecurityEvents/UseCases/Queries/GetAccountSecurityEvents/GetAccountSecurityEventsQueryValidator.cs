using FluentValidation;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public sealed class GetAccountSecurityEventsQueryValidator : AbstractValidator<GetAccountSecurityEventsQuery>
{
    public GetAccountSecurityEventsQueryValidator() => RuleFor(x => x.UserId).NotEmpty();
}
