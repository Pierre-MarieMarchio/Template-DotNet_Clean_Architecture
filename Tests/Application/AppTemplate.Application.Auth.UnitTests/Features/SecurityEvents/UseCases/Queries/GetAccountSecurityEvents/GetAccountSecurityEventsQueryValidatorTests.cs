using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public sealed class GetAccountSecurityEventsQueryValidatorTests
{
    private static readonly GetAccountSecurityEventsQueryValidator _validator = new();

    [Fact]
    public void AnEmptyAccountId_IsRefused() =>
        _validator.Validate(new GetAccountSecurityEventsQuery(Guid.Empty, 1, 20)).IsValid.ShouldBeFalse();

    [Fact]
    public void ANamedAccountId_IsAccepted() =>
        _validator.Validate(new GetAccountSecurityEventsQuery(Guid.CreateVersion7(), 1, 20)).IsValid.ShouldBeTrue();
}
