using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;
using AppTemplate.Infrastructure.Core.Common.Saving;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Features.Sessions.Adapters.Services;

public sealed class SessionTokensServiceCommitTests(GrantTableFixture fixture) : IClassFixture<GrantTableFixture>
{
    private const string _stagedPhoneNumber = "+33 1 23 45 67 89";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IssueRefreshToken_CommitsWhateverElseTheRequestStaged()
    {
        var user = await fixture.CreateUserAsync(TestToken);

        await using (var request = fixture.Services.CreateAsyncScope())
        {
            await StageAnUnrelatedChangeAsync(request.ServiceProvider, user.Id);

            await CreateGrants(request.ServiceProvider, user)
                .IssueRefreshTokenAsync(user.Id, SessionClient.Unknown, TestToken);
        }

        (await ReadPhoneNumberAsync(user.Id)).ShouldBe(
            _stagedPhoneNumber,
            "issuing a grant commits the request's unit of work, so a change the caller staged on the " +
            "same context is committed with it.");
    }

    [Fact]
    public async Task RotateRefreshToken_CommitsWhateverElseTheRequestStaged()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user);

        await using (var request = fixture.Services.CreateAsyncScope())
        {
            await StageAnUnrelatedChangeAsync(request.ServiceProvider, user.Id);

            var rotation = await CreateGrants(request.ServiceProvider, user)
                .RotateRefreshTokenAsync(issued.Value, SessionClient.Unknown, TestToken);

            rotation.Succeeded.ShouldBeTrue();
        }

        (await ReadPhoneNumberAsync(user.Id)).ShouldBe(
            _stagedPhoneNumber,
            "a rotation commits the request's unit of work inside its transaction, so a change the " +
            "caller staged on the same context is committed with the successor.");
    }

    [Fact]
    public async Task RevokeAllRefreshTokens_CommitsWhateverElseTheRequestStaged()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        await IssueAsync(user);

        await using (var request = fixture.Services.CreateAsyncScope())
        {
            await StageAnUnrelatedChangeAsync(request.ServiceProvider, user.Id);

            await CreateGrants(request.ServiceProvider, user)
                .RevokeAllRefreshTokensAsync(user.Id, cancellationToken: TestToken);
        }

        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(0);
        (await ReadPhoneNumberAsync(user.Id)).ShouldBe(
            _stagedPhoneNumber,
            "ending every session commits the request's unit of work, so a change the caller staged " +
            "on the same context is committed with the revocation.");
    }

    private static async Task StageAnUnrelatedChangeAsync(IServiceProvider request, Guid userId)
    {
        var context = request.GetRequiredService<AuthDbContext>();
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId, TestToken);

        user.PhoneNumber = _stagedPhoneNumber;

        context.ChangeTracker.HasChanges().ShouldBeTrue();
    }

    private async Task<IssuedRefreshToken> IssueAsync(AppUser user)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        return await CreateGrants(scope.ServiceProvider, user)
            .IssueRefreshTokenAsync(user.Id, SessionClient.Unknown, TestToken);
    }

    private async Task<string?> ReadPhoneNumberAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.PhoneNumber)
            .SingleAsync(TestToken);
    }

    private static SessionTokensService CreateGrants(IServiceProvider request, AppUser user)
    {
        var directory = Substitute.For<IAppUserDirectory>();
        directory.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        return new(
            new AccessTokenIssuer(
                directory,
                new OptionsWrapper<JwtOptions>(new JwtOptions()),
                request.GetRequiredService<IDateTimeProvider>()),
            request.GetRequiredService<IRefreshTokenTable>(),
            request.GetRequiredService<IRefreshTokenFamilyTable>(),
            directory,
            request.GetRequiredService<IContextUnitOfWork<AuthDbContext>>(),
            request.GetRequiredService<AuthDbContext>(),
            request.GetRequiredService<IDateTimeProvider>(),
            new OptionsWrapper<RefreshTokenOptions>(new RefreshTokenOptions()),
            Substitute.For<ISecurityEventLog>(),
            NullLogger<SessionTokensService>.Instance);
    }
}
