using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Ports.StoredFileQueries;

public sealed class StoredFileFilterTests
{
    [Fact]
    public void NothingSent_FiltersNothing()
    {
        var result = StoredFileFilter.Create(null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Search.ShouldBeNull();
        result.Value.State.ShouldBeNull();
    }

    [Theory]
    [InlineData("pending", StoredFileState.Pending)]
    [InlineData("available", StoredFileState.Available)]
    [InlineData("  PENDING  ", StoredFileState.Pending)]
    public void ARecognisedState_IsParsed(string raw, StoredFileState expected)
    {
        var result = StoredFileFilter.Create(null, raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("deleted")]
    public void AnUnrecognisedStateOrANumber_IsRefused(string raw)
    {
        var result = StoredFileFilter.Create(null, raw);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public void ASearchTerm_IsCarried()
    {
        var result = StoredFileFilter.Create("holiday", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Search!.Value.ShouldBe("holiday");
    }

    [Fact]
    public void AnOverlongSearchTerm_IsRefused()
    {
        var result = StoredFileFilter.Create(new string('x', 1_000), null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TheEmptyFilter_IsShared() => StoredFileFilter.None.Search.ShouldBeNull();
}
