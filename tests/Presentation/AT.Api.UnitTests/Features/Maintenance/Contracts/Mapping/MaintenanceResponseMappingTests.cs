using AppTemplate.Api.Features.Maintenance.Contracts.Mapping;
using AppTemplate.Application.Core.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Contracts.Mapping;

public sealed class MaintenanceResponseMappingTests
{
    [Fact]
    public void ToPurgeResponse_CarriesTheDeletedCount()
    {
        var response = MaintenanceResponseMapping.ToPurgeResponse(Result.Success(17));

        response.IsSuccess.ShouldBeTrue();
        response.Value.Deleted.ShouldBe(17);
    }
}
