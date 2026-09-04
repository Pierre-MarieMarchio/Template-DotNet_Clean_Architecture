using AppTemplate.Api.Features.Maintenance.Mapping;
using AppTemplate.Application.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Mapping;

public sealed class MaintenanceResponseMappingTests
{
    [Fact]
    public void ToPurgeResponse_CarriesTheDeletedCount()
    {
        var response = MaintenanceResponseMapping.ToPurgeResponse(Result.Success(17));

        response.IsSuccess.ShouldBeTrue();
        response.Value.Deleted.ShouldBe(17);
    }

    /// <summary>
    /// Nothing to purge is a purge that succeeded, so zero has to travel as a body — not as a failure,
    /// and not as an empty response a caller would have to guess at.
    /// </summary>
    [Fact]
    public void ToPurgeResponse_TreatsZeroDeleted_AsASuccessCarryingZero()
    {
        var response = MaintenanceResponseMapping.ToPurgeResponse(Result.Success(0));

        response.IsSuccess.ShouldBeTrue();
        response.Value.Deleted.ShouldBe(0);
    }

    [Fact]
    public void ToPurgeResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var response = Should.NotThrow(() => MaintenanceResponseMapping.ToPurgeResponse(Result.Failure<int>(_someError)));

        response.IsFailure.ShouldBeTrue();
        response.Error.ShouldBe(_someError);
    }

    private static readonly Error _someError = Error.Conflict("maintenance.purgeFailed", "could not purge");
}
