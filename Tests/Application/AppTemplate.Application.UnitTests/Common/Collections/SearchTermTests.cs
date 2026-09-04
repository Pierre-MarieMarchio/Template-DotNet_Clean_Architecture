using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Collections;

public sealed class SearchTermTests
{
    [Fact]
    public void Create_Trims()
    {
        var result = SearchTerm.Create("  groceries  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("groceries");
    }

    [Fact]
    public void Create_Accepts_ATermAtExactlyTheMaxLength()
    {
        string atLimit = new string('a', SearchTerm.MaxLength);

        SearchTerm.Create(atLimit).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_Rejects_ATermOneOverTheMaxLength()
    {
        string overLimit = new string('a', SearchTerm.MaxLength + 1);

        var result = SearchTerm.Create(overLimit);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<ArgumentNullException>(() => SearchTerm.Create(null!));

    [Fact]
    public void Create_Accepts_AnEmptyValue() =>
        SearchTerm.Create(string.Empty).IsSuccess.ShouldBeTrue();
}
