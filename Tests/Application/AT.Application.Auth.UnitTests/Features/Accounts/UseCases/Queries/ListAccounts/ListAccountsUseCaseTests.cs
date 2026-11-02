using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using FluentValidation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Queries.ListAccounts;

public sealed class ListAccountsUseCaseTests
{
    private readonly IAccountQueries _queries = Substitute.For<IAccountQueries>();
    private readonly IValidator<ListAccountsQuery> _validator = new ListAccountsQueryValidator();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public ListAccountsUseCaseTests() =>
        _queries.ListAsync(Arg.Any<string?>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<AccountSummary>([], 1, 20, 0));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoPrefix_ListsEveryAccount(string? email)
    {
        await UseCase().ExecuteAsync(new ListAccountsQuery(email, null, null), TestToken);

        await _queries.Received(1).ListAsync(null, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePrefix_IsTrimmed()
    {
        await UseCase().ExecuteAsync(new ListAccountsQuery("  alice ", null, null), TestToken);

        await _queries.Received(1).ListAsync("alice", Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APrefixLongerThanAnAddressCanBe_IsRefusedBeforeAnythingIsQueried()
    {
        string tooLong = new('a', AccountCollectionPolicy.MaxEmailPrefixLength + 1);

        var result = await UseCase().ExecuteAsync(new ListAccountsQuery(tooLong, null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("request.validationFailed");
        await _queries.DidNotReceive().ListAsync(
            Arg.Any<string?>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APageSizeAboveTheCeiling_IsRefused()
    {
        var result = await UseCase().ExecuteAsync(
            new ListAccountsQuery(null, 1, AccountCollectionPolicy.Instance.MaxPageSize + 1),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        await _queries.DidNotReceive().ListAsync(
            Arg.Any<string?>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAPageSize_ThePolicysDefaultApplies()
    {
        await UseCase().ExecuteAsync(new ListAccountsQuery(null, null, null), TestToken);

        await _queries.Received(1).ListAsync(
            null,
            Arg.Is<PageRequest>(paging => paging.PageSize == AccountCollectionPolicy.Instance.DefaultPageSize),
            Arg.Any<CancellationToken>());
    }

    private ListAccountsUseCase UseCase() => new(_queries, _validator);
}
