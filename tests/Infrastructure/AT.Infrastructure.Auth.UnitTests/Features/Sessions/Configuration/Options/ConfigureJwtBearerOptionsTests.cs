using System.Security.Claims;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Sessions.Configuration.Options;

public sealed class ConfigureJwtBearerOptionsTests
{
    private const string _issuer = "https://localhost/app-template";
    private const string _audience = "app-template-api";

    private const string _currentKey = "current-signing-key-0123456789-abcdefghij";
    private const string _previousKey = "previous-signing-key-0123456789-abcdefghij";
    private const string _retiredKey = "retired-signing-key-0123456789-abcdefghij";

    private static readonly Guid _userId = Guid.CreateVersion7();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATokenSignedWithAPreviousKey_IsStillAccepted_WhileTheKeyIsListed()
    {
        string inCirculation = await IssueSignedWith(_previousKey);

        (await IsAccepted(inCirculation, _currentKey, _previousKey)).ShouldBeTrue();
        (await IsAccepted(inCirculation, _currentKey)).ShouldBeFalse(
            "Once the previous key leaves 'Jwt:PreviousKeys', the tokens it signed are refused.");
    }

    [Fact]
    public async Task ATokenSignedWithAKeyNoLongerListed_IsRefused()
    {
        string signedWithARetiredKey = await IssueSignedWith(_retiredKey);

        (await IsAccepted(signedWithARetiredKey, _currentKey, _previousKey)).ShouldBeFalse();
        (await IsAccepted(await IssueSignedWith(_currentKey), _currentKey, _previousKey)).ShouldBeTrue();
    }

    [Fact]
    public async Task ANewToken_IsSignedWithTheCurrentKey_NeverWithAPreviousOne()
    {
        string issuedDuringTheRotation = await IssueSignedWith(_currentKey, _previousKey);

        (await IsAccepted(issuedDuringTheRotation, _currentKey)).ShouldBeTrue();
        (await IsAccepted(issuedDuringTheRotation, _previousKey)).ShouldBeFalse();
    }

    private static async Task<string> IssueSignedWith(string key, params string[] previousKeys)
    {
        var directory = Substitute.For<IAppUserDirectory>();
        directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new AppUser { Id = _userId, UserName = "someone", Email = "someone@example.com" });
        directory.GenerateClaimsAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns([new Claim(ClaimTypes.NameIdentifier, _userId.ToString())]);

        var options = new JwtOptions { Key = key, Issuer = _issuer, Audience = _audience };
        foreach (var previous in previousKeys)
        {
            options.PreviousKeys.Add(previous);
        }

        var issued = await new AccessTokenIssuer(directory, new OptionsWrapper<JwtOptions>(options), TimeProvider.System)
            .IssueAsync(_userId, Guid.CreateVersion7(), TestToken);

        return issued.Value;
    }

    private static async Task<bool> IsAccepted(string token, string key, params string[] previousKeys)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] = DatabaseProvider.DesignTimeConnectionString,
            ["Jwt:Key"] = key,
            ["Jwt:Issuer"] = _issuer,
            ["Jwt:Audience"] = _audience,
        };
        for (int index = 0; index < previousKeys.Length; index++)
        {
            settings[$"Jwt:PreviousKeys:{index}"] = previousKeys[index];
        }

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(environment);
        services.AddAuthModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var result = await bearer.TokenHandlers[0].ValidateTokenAsync(token, bearer.TokenValidationParameters);

        return result.IsValid;
    }
}
