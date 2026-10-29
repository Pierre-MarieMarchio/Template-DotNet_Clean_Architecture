using System.Buffers.Text;
using System.Security.Cryptography;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;

public sealed class AuthenticateAccessKeyUseCaseTests
{
    private const string _invalid = "auth.accessKey.invalid";

    private static readonly Guid _ownerId = Guid.CreateVersion7();
    private static readonly Guid _accessKeyId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;
    private static readonly string _key = AKey();

    private readonly IAccessKeysService _accessKeys = Substitute.For<IAccessKeysService>();
    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALiveKey_OfAnAccountThatMaySignIn_NamesItsOwner_AndItsScopes()
    {
        GivenTheKey(AGrant());
        GivenTheOwnerMaySignIn(true);

        var result = await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(_ownerId);
        result.Value.AccessKeyId.ShouldBe(_accessKeyId);
        result.Value.Scopes.ShouldBe(["read"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ak_")]
    [InlineData("not-an-access-key")]
    [InlineData("Bearer ak_")]
    public async Task AMalformedKey_IsInvalid_AndNeverReachesThePort(string key)
    {
        var result = await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(key), TestToken);

        ShouldBeInvalid(result);
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownKey_IsInvalid()
    {
        GivenTheKey(null);
        GivenTheOwnerMaySignIn(true);

        ShouldBeInvalid(await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken));
    }

    [Fact]
    public async Task ARevokedKey_IsInvalid()
    {
        GivenTheKey(AGrant() with { RevokedAt = _now.AddDays(-1) });
        GivenTheOwnerMaySignIn(true);

        ShouldBeInvalid(await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken));
        await _accessKeys.DidNotReceiveWithAnyArgs().RecordUseAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AnExpiredKey_IsInvalid(int secondsFromNow)
    {
        GivenTheKey(AGrant() with { ExpiresAt = _now.AddSeconds(secondsFromNow) });
        GivenTheOwnerMaySignIn(true);

        ShouldBeInvalid(await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken));
    }

    [Fact]
    public async Task AKeyExpiringAfterNow_IsStillLive()
    {
        GivenTheKey(AGrant() with { ExpiresAt = _now.AddSeconds(1) });
        GivenTheOwnerMaySignIn(true);

        (await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AKeyWhoseAccountMayNotSignIn_IsInvalid_AndItsUseIsNotRecorded()
    {
        GivenTheKey(AGrant());
        GivenTheOwnerMaySignIn(false);

        ShouldBeInvalid(await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken));
        await _accessKeys.DidNotReceiveWithAnyArgs().RecordUseAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryRefusal_ReadsTheSame()
    {
        var malformed = await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand("ak_"), TestToken);

        GivenTheKey(null);
        var unknown = await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken);

        GivenTheKey(AGrant() with { RevokedAt = _now });
        var revoked = await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken);

        GivenTheKey(AGrant());
        GivenTheOwnerMaySignIn(false);
        var refusedAccount = await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken);

        new[] { unknown.Error, revoked.Error, refusedAccount.Error }.ShouldAllBe(error => error == malformed.Error);
    }

    [Fact]
    public async Task AKeyNeverUsed_RecordsThisUse()
    {
        GivenTheKey(AGrant() with { LastUsedAt = null });
        GivenTheOwnerMaySignIn(true);

        await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken);

        await _accessKeys.Received(1).RecordUseAsync(_accessKeyId, _now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AKeyUsedWithinTheResolution_DoesNotRecordAgain()
    {
        GivenTheKey(AGrant() with { LastUsedAt = _now - AccessKeyPolicy.LastUseResolution + TimeSpan.FromSeconds(1) });
        GivenTheOwnerMaySignIn(true);

        (await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken)).IsSuccess.ShouldBeTrue();

        await _accessKeys.DidNotReceiveWithAnyArgs().RecordUseAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AKeyUsedAResolutionAgo_RecordsThisUse()
    {
        GivenTheKey(AGrant() with { LastUsedAt = _now - AccessKeyPolicy.LastUseResolution });
        GivenTheOwnerMaySignIn(true);

        await UseCase().ExecuteAsync(new AuthenticateAccessKeyCommand(_key), TestToken);

        await _accessKeys.Received(1).RecordUseAsync(_accessKeyId, _now, Arg.Any<CancellationToken>());
    }

    private static void ShouldBeInvalid(Result<AccessKeyCaller> result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(_invalid);
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
    }

    private void GivenTheKey(AccessKeyGrant? grant) =>
        _accessKeys.FindAsync(_key, Arg.Any<CancellationToken>()).Returns(grant);

    private void GivenTheOwnerMaySignIn(bool maySignIn) =>
        _accounts.CanSignInAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(maySignIn);

    private static AccessKeyGrant AGrant() =>
        new(_accessKeyId, _ownerId, ["read"], ExpiresAt: null, RevokedAt: null, LastUsedAt: null);

    private static string AKey() =>
        AccessKeyPolicy.KeyPrefix
        + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(AccessKeyPolicy.SecretSizeInBytes));

    private AuthenticateAccessKeyUseCase UseCase() => new(_accessKeys, _accounts, new StubDateTimeProvider());
}
