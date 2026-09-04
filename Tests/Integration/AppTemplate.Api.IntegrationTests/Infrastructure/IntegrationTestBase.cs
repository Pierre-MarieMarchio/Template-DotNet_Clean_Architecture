using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.InMemory.Email;
using AppTemplate.Infrastructure.InMemory.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// An account, as far as a caller that has only signed up can know it. It carries no id: nothing in
/// the sign-up journey publishes one — that is <see cref="TestSession.UserId"/>'s job.
/// </summary>
/// <param name="Password">Kept in plaintext so the test can log in again.</param>
public sealed record TestUser(string UserName, string Email, string Password);

/// <param name="UserId">Read from <c>GET /auth/me</c>, the only endpoint that publishes a profile.</param>
public sealed record TestSession(Guid UserId, TokenResponse Tokens);

/// <summary>
/// What every test class shares: the fixture, a clean database, a clean mailbox, a clock parked at a
/// known instant, and clients that the rate limiter treats as separate callers.
/// </summary>
/// <remarks>
/// <para>
/// <b>State reset.</b> Before each test every table in both module schemas is truncated, the
/// recorded mailbox and captured log are emptied, and the clock is placed at a fixed instant. The
/// host itself is not recycled — rebuilding it per test would cost more than the whole suite.
/// </para>
/// <para>
/// <b>Why the clock is set to real time rather than to
/// <see cref="FixedDateTimeProvider.DefaultInstant"/>.</b> Access tokens are stamped from the
/// injected clock, but the bearer handler validates <c>exp</c> and <c>nbf</c> against the machine
/// clock with zero skew. A clock parked in the past would mint tokens that are already expired by
/// the time they are validated, so every authenticated request would 401 for a reason no test is
/// about. It is set once per test and never moves on its own, so it is still exactly comparable
/// against a stored timestamp — which is what the auditing test relies on.
/// </para>
/// </remarks>
[Collection(ApiCollectionDefinition.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    /// <summary>Satisfies every rule in the <c>Identity</c> policy the test host configures.</summary>
    protected const string ValidPassword = "Integration!Test1";

    protected const string TodoListsRoute = "/api/v1/todo-lists";
    protected const string AuthRoute = "/api/v1/auth";

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
        Fixture.DomainEvents.Clear();

        // Truncated to whole milliseconds so an assertion against a PostgreSQL timestamptz — which
        // keeps microseconds, not the .NET tick — can compare for exact equality.
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

    /// <summary>
    /// A client the host sees as a caller of its own. Each call hands out a fresh address, so one
    /// test's authentication attempts cannot consume another's per-IP rate-limit budget. A test that
    /// wants several requests to share a budget reuses the same client.
    /// </summary>
    protected HttpClient CreateClient()
    {
        int index = Interlocked.Increment(ref _clientAddressCounter);

        return CreateClientWithAddress(
            string.Create(CultureInfo.InvariantCulture, $"10.10.{(index / 250) % 250}.{(index % 250) + 1}"));
    }

    /// <summary>
    /// A client pinned to a caller-chosen address instead of the next one <see cref="CreateClient"/>
    /// would hand out. For the one kind of test that needs two distinct callers to look like the same
    /// caller to the rate limiter — everywhere else a shared address would be a bug, not a fixture.
    /// </summary>
    protected HttpClient CreateClientWithAddress(string address)
    {
        var client = Fixture.Factory.CreateClient();

        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, address);

        _clients.Add(client);

        return client;
    }

    #region Accounts

    /// <summary>
    /// Registers an account and confirms its email address, going through the real endpoints and
    /// taking the confirmation token out of the recorded email — the same route a user's browser
    /// takes. Sign-in requires a confirmed address, so almost every test needs this.
    /// </summary>
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
        string userName = $"{label ?? "user"}-{suffix}";
        var request = new RegisterRequest(userName, $"{userName}@integration.test", ValidPassword);

        using var response = await client.PostAsJsonAsync($"{AuthRoute}/register", request, TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Registering {request.Email} failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        var registered = await ApiJson.ReadAsync<RegisterResponse>(response, TestToken);

        return new TestUser(registered.UserName, registered.Email, ValidPassword);
    }

    /// <summary>
    /// Fails loudly on any other outcome: a helper that every test builds on must not quietly hand
    /// back a session that was never established.
    /// </summary>
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

    /// <summary>A confirmed account plus a client already carrying its access token.</summary>
    /// <remarks>
    /// Three requests against the authentication endpoints' per-address budget — register, confirm, log
    /// in — so a test that goes on to spend that budget on the same client has seven permits left, not
    /// ten. Reading the profile costs nothing here: <c>GET /auth/me</c> is on the global limiter.
    /// </remarks>
    protected async Task<(HttpClient Client, TestUser User, TestSession Session)> SignInAsync(string? label = null)
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client, label);
        var tokens = await LoginAsync(client, user);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return (client, user, new TestSession(await ReadOwnUserIdAsync(client), tokens));
    }

    /// <summary>
    /// The account id of whoever <paramref name="client"/> is carrying a token for. Read from the
    /// profile endpoint because that is the only place the id is published — signing up and signing
    /// in both answer without it.
    /// </summary>
    protected static async Task<Guid> ReadOwnUserIdAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            // The product's 401 body says only that a token was required, by design. ApiFactory
            // publishes the actual reason in a header so a 401 nobody expected can be diagnosed.
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

    /// <summary>
    /// Pulls the address and the single-use token out of a confirmation or password-reset email, by
    /// parsing the link the same way the browser page it points to would: both templates carry the
    /// values in the URL fragment, HTML-encoded, in the same shape.
    /// </summary>
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

    #endregion

    #region Todo lists

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

    /// <summary>
    /// Loads a stored aggregate the way a use case does: through the application port, inside a scope of
    /// the caller's own.
    /// </summary>
    /// <remarks>
    /// It deliberately does not reach for a <c>DbSet</c>. The persistence models are internal and EF does
    /// not map the domain types, so a test that queried rows would be asserting on storage rather than on
    /// what the application layer can actually see — and would pass even if the mapper never handed the
    /// values back. Going through <see cref="ITodoListRepository"/> exercises the query, the mapper and
    /// the identity map together.
    /// <para>
    /// The scope belongs to the caller, because an aggregate is only meaningful while the scope that
    /// loaded it is alive: that scope owns the context holding the tracked rows behind it.
    /// </para>
    /// </remarks>
    protected static async Task<TodoList> LoadTodoListAsync(IServiceProvider services, Guid listId)
    {
        ArgumentNullException.ThrowIfNull(services);

        return await services.GetRequiredService<ITodoListRepository>().GetAsync(listId, TestToken)
            ?? throw new InvalidOperationException($"No to-do list with id '{listId}' is stored.");
    }

    #endregion

    #region Conditional requests

    /// <summary>
    /// Reads a list and returns the strong validator the read published, which is what a conditional
    /// write has to send back.
    /// </summary>
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

    /// <summary>
    /// A rename carrying whatever the caller wants in <c>If-Match</c>, including nothing at all.
    /// </summary>
    /// <remarks>
    /// The header is added without validation on purpose: a test that could only send values
    /// <see cref="HttpClient"/> considers well-formed could never exercise the malformed case, which
    /// is one of the two the server has to distinguish.
    /// </remarks>
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

    #endregion
}
