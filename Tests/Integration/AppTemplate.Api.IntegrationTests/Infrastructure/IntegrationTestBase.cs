using System.Globalization;
//#if (localAuth)
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
//#endif
/*#if (externalAuth && todoLists)
using System.Net;
#endif*/
/*#if (externalAuth)
using System.Net.Http.Headers;
#endif*/
/*#if (externalAuth && todoLists)
using System.Net.Http.Json;
#endif*/
//#if (todoLists)
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
//#endif
/*#if (externalAuth)
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Services;
#endif*/
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
//#endif
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
//#if (todoLists || externalAuth)
using Microsoft.Extensions.DependencyInjection;
//#endif
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;
//#if (localAuth)

public sealed record TestUser(string Email, string Password);

public sealed record TestSession(Guid UserId, TokenResponse Tokens);
//#endif
/*#if (externalAuth)

/// <summary>A caller as the external provider names it: its subject, and the address its tokens carry.</summary>
public sealed record TestUser(string Subject, string Email);

/// <param name="UserId">The internal owner the API maps <see cref="TestUser.Subject"/> to.</param>
/// <param name="Tokens">What the provider issued, in the shape a signed-in test reads it from.</param>
public sealed record TestSession(Guid UserId, ProviderTokens Tokens);

/// <param name="AccessToken">Signed by <see cref="ExternalProvider"/>, so the API accepts it.</param>
public sealed record ProviderTokens(string AccessToken);
#endif*/

[Collection(ApiCollectionDefinition.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    //#if (localAuth)
    protected const string ValidPassword = "Integration!Test1";

    //#endif
    //#if (todoLists)
    protected const string TodoListsRoute = "/api/v1/todo-lists";
    //#endif
    //#if (localAuth)
    protected const string AuthRoute = "/api/v1/auth";
    //#endif
    //#if (signedInRoute)

    /*#if (todoLists)*/
    protected const string ProtectedRoute = TodoListsRoute;
    /*#elseif (localAuth)
    protected const string ProtectedRoute = AuthRoute + "/me";
    #elseif (notes)
    protected const string ProtectedRoute = "/api/v1/notes";
    #else
    protected const string ProtectedRoute = "/api/v1/files";
    #endif*/
    //#endif

    private static int _clientAddressCounter;

    private readonly List<HttpClient> _clients = [];

    protected IntegrationTestBase(ApiFixture fixture) => Fixture = fixture;

    protected ApiFixture Fixture { get; }

    protected FixedDateTimeProvider Clock => Fixture.Clock;

    protected RecordedEmails Emails => Fixture.Emails;

    protected TestDatabase Database => Fixture.Database;

    protected static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Database.ResetAsync(TestToken);

        Emails.Clear();
        Fixture.Logs.Clear();
        //#if (todoLists)
        Fixture.DomainEvents.Clear();
        //#endif

        var now = DateTimeOffset.UtcNow;
        Clock.Set(new DateTimeOffset(
            now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond),
            TimeSpan.Zero));
    }

    public ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _clients.Clear();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    protected HttpClient CreateClient()
    {
        int index = Interlocked.Increment(ref _clientAddressCounter);

        return CreateClientWithAddress(
            string.Create(CultureInfo.InvariantCulture, $"10.10.{(index / 250) % 250}.{(index % 250) + 1}"));
    }

    protected HttpClient CreateClientWithAddress(string address)
    {
        var client = Fixture.Factory.CreateClient();

        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, address);

        _clients.Add(client);

        return client;
    }

    //#if (localAuth)

    protected async Task<TestUser> RegisterConfirmedUserAsync(HttpClient client, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var user = await RegisterUserAsync(client, label);
        var (email, token) = ReadEmailLink(RequireLastEmailTo(user.Email));

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest(email, token),
            TestToken);

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"Confirming {user.Email} failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return user;
    }

    protected static async Task<TestUser> RegisterUserAsync(HttpClient client, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        string suffix = Guid.CreateVersion7().ToString("N")[..12];
        var request = new RegisterRequest($"{label ?? "user"}-{suffix}@integration.test", ValidPassword);

        using var response = await client.PostAsJsonAsync($"{AuthRoute}/register", request, TestToken);

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"Registering {request.Email} failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return new TestUser(request.Email, ValidPassword);
    }

    protected static async Task<TokenResponse> LoginAsync(HttpClient client, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(user);

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Logging {user.Email} in failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return (await ApiJson.ReadAsync<LoginResponse>(response, TestToken) as LoginResponse.Authenticated)?.Tokens
            ?? throw new InvalidOperationException($"Logging {user.Email} in did not produce a token pair.");
    }

    protected async Task<(HttpClient Client, TestUser User, TestSession Session)> SignInAsync(string? label = null)
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client, label);
        var tokens = await LoginAsync(client, user);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return (client, user, new TestSession(await ReadOwnUserIdAsync(client), tokens));
    }

    protected static async Task<Guid> ReadOwnUserIdAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            response.Headers.TryGetValues(ApiFactory.AuthFailureHeader, out var why);

            throw new InvalidOperationException(
                $"Reading the caller's own profile failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken) +
                $" | {ApiFactory.AuthFailureHeader}: {string.Join("; ", why ?? [])}");
        }

        return (await ApiJson.ReadAsync<CurrentUserResponse>(response, TestToken)).UserId;
    }

    protected SentEmail RequireLastEmailTo(string recipient) =>
        Emails.LastTo(recipient)
        ?? throw new InvalidOperationException(
            $"No email was recorded for '{recipient}'. Recorded: " +
            string.Join(", ", Emails.Snapshot().Select(sent => sent.Recipient)));

    protected static (string Email, string Token) ReadEmailLink(SentEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        const string marker = "href=\"";

        int start = email.HtmlBody.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"No link in the confirmation email: {email.HtmlBody}");
        }

        start += marker.Length;
        int end = email.HtmlBody.IndexOf('"', start);
        string href = WebUtility.HtmlDecode(email.HtmlBody[start..end]);
        string fragment = new Uri(href, UriKind.Absolute).Fragment.TrimStart('#');

        string? address = null;
        string? token = null;

        foreach (string pair in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            string name = pair[..separator];
            string value = Uri.UnescapeDataString(pair[(separator + 1)..]);

            if (string.Equals(name, "email", StringComparison.Ordinal))
            {
                address = value;
            }
            else if (string.Equals(name, "token", StringComparison.Ordinal))
            {
                token = value;
            }
        }

        return (
            address ?? throw new InvalidOperationException($"No email in the link fragment: {fragment}"),
            token ?? throw new InvalidOperationException($"No token in the link fragment: {fragment}"));
    }

    //#endif
    /*#if (externalAuth)

    /// <summary>
    /// A client carrying a token <see cref="ExternalProvider"/> signed for a subject of its own, the
    /// caller that subject is, and the internal owner the API maps it to. The mapping is made here
    /// through the API's own service, exactly as the first authenticated request would make it, since
    /// no endpoint publishes the owner id.
    /// </summary>
    /// <param name="label">Readable in the subject and the address, for a test that signs in several callers.</param>
    /// <param name="roles">The roles the token carries at the path the test host reads them from.</param>
    protected async Task<(HttpClient Client, TestUser User, TestSession Session)> SignInAsync(
        string? label = null,
        params string[] roles)
    {
        string subject = $"{label ?? "caller"}-{Guid.NewGuid():N}";
        var user = new TestUser(subject, $"{subject}@integration.test");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var owner = await scope.ServiceProvider.GetRequiredService<IExternalIdentityService>().ResolveAsync(
            new ExternalCallerDto(ExternalProvider.Issuer, subject, user.Email, label),
            TestToken);

        string token = ExternalProvider.Token(subject, user.Email, roles);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user, new TestSession(owner.Value, new ProviderTokens(token)));
    }

    #endif*/
    //#if (todoLists)

    protected static async Task<Guid> CreateTodoListAsync(HttpClient client, string name)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest(name),
            TestToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Creating the list '{name}' failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return (await ApiJson.ReadAsync<TodoListResponse>(response, TestToken)).Id;
    }

    protected static async Task<Guid> AddTodoItemAsync(
        HttpClient client,
        Guid todoListId,
        string title,
        IReadOnlyList<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new AddTodoItemRequest(title, null, tags);

        using var response = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{todoListId}/items",
            request,
            TestToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Adding the item '{title}' failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return (await ApiJson.ReadAsync<TodoItemResponse>(response, TestToken)).Id;
    }

    protected static async Task<TodoList> LoadTodoListAsync(IServiceProvider services, Guid listId)
    {
        ArgumentNullException.ThrowIfNull(services);

        return await services.GetRequiredService<ITodoListRepository>().GetAsync(listId, TestToken)
            ?? throw new InvalidOperationException($"No to-do list with id '{listId}' is stored.");
    }

    protected static async Task<string> ReadETagAsync(HttpClient client, Guid todoListId)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{todoListId}", UriKind.Relative),
            TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Reading the list '{todoListId}' failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return response.Headers.ETag?.ToString()
            ?? throw new InvalidOperationException(
                $"The read of '{todoListId}' published no ETag. Present: " +
                string.Join(", ", response.Headers.Select(header => header.Key)));
    }

    protected static Task<HttpResponseMessage> RenameAsync(
        HttpClient client,
        Guid todoListId,
        string name,
        string? ifMatch = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Put, $"{TodoListsRoute}/{todoListId}")
        {
            Content = JsonContent.Create(new RenameTodoListRequest(name)),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, TestToken);
    }

    //#endif
}
