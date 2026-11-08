//#if (localAuth)
using System.Net;
using System.Text.RegularExpressions;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Core.Common.Results;
using Mediator;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppPortal.UnitTests.Common.Security;

public sealed partial class SignInPageTests
{
    private const string _path = "/account/sign-in";

    [Fact]
    public async Task ACallerWhoIsNotSignedIn_ReceivesTheSignInForm()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(_path, UriKind.Relative), TestContext.Current.CancellationToken);
        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.ShouldContain("<form");
        page.ShouldContain("type=\"password\"");
    }

    [Fact]
    public async Task AWrongPassword_DoesNotSignIn_AndTheFormComesBackWithAnError()
    {
        var sender = new RefusingSender();
        using var host = new PresentationHost(configure: services =>
        {
            services.RemoveAll<ISender>();
            services.AddScoped<ISender>(_ => sender);
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        string form = await client.GetStringAsync(new Uri(_path, UriKind.Relative), TestContext.Current.CancellationToken);
        var fields = Inputs().Matches(form).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value);
        fields[fields.Keys.Single(name => name.EndsWith("Email", StringComparison.Ordinal))] = "someone@example.com";
        fields[fields.Keys.Single(name => name.EndsWith("Password", StringComparison.Ordinal))] = "not-the-password";

        using var response = await client.PostAsync(
            new Uri(_path, UriKind.Relative),
            new FormUrlEncodedContent(fields),
            TestContext.Current.CancellationToken);
        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.ShouldContain("role=\"alert\"");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse("A refused sign-in sets no session cookie.");
        sender.Attempts.ShouldBe([new LoginCommand("someone@example.com", "not-the-password")]);
    }

    private sealed class RefusingSender : ISender
    {
        internal List<LoginCommand> Attempts { get; } = [];

        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        {
            if (command is not LoginCommand login)
            {
                throw new NotSupportedException(command.GetType().Name);
            }

            Attempts.Add(login);

            var refusal = Result.Failure<LoginOutcome>(Error.Unauthorized("auth.invalidCredentials", "Invalid credentials."));

            return new ValueTask<TResponse>((TResponse)(object)refusal);
        }

        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [GeneratedRegex("<input[^>]*?name=\"(?<name>[^\"]+)\"(?:[^>]*?value=\"(?<value>[^\"]*)\")?", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 5000)]
    private static partial Regex Inputs();
}
//#endif
