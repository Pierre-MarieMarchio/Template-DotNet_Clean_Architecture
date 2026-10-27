# Presentation project

How the presentation project beside the API is created, composed, and how it signs people in.

## What you get

`Src/Presentation/<Project>.<Name>` is a Blazor Web App with server interactivity. It runs on the
server and calls the use cases in its own process, with its own composition in its `Program.cs`:
the same features and modules as the API, its own caller, its own sign-in, and neither jobs nor
migrations ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
Its tests are in `Tests/Presentation/<Project>.<Name>.UnitTests`.

## Creating the project

The project's own files come with the project: its project file, `Program.cs`, its settings, its
image, and the wiring under `Common/` and `Features/`. The pages come from Microsoft's own
template: `bootstrap` first runs `dotnet run Tools/Tasks.cs presentation-scaffold`, which runs
`dotnet new blazor` from the template package and version the project's descriptor pins, in a
template hive of its own, and keeps what it writes under `Components` and `wwwroot`. It then adds
the project and its tests to the solution and formats them. The step needs the network once.

Commit the project after bootstrap: from then on its pages are this project's own, and
`presentation-scaffold` leaves it alone.

A project generated without one takes it later from `dotnet new cleanarch-presentation`, which
writes the project, its tests and this page; the `README.md` in the project's folder lists the lines
to add by hand outside them, and `presentation-scaffold` then creates it the same way.

## Calling a use case

A Blazor Server circuit lives for as long as its browser tab, and so does the dependency-injection
scope it was opened with. A use case resolved from it would keep its database context, its unit of
work and its tracked aggregates for that long, and two events of the same circuit would share
them. A component therefore runs each operation in a scope of its own, through `OperationScope`:

```razor
@inject OperationScope Operations

@code {
    private async Task SaveAsync() =>
        _result = await Operations.RunAsync<I<UseCase>UseCase, Result<<Output>>>(
            useCase => useCase.ExecuteAsync(_command));
}
```

The scope it opens carries the circuit's caller, so `ICurrentUser` names the person signed in and
ownership stays in the query ([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)). A page
rendered statically, a sign-in form among them, can inject a use case directly: its request is the
scope, and its user is the caller. A use case resolved anywhere else refuses to answer who calls
it, rather than guess.

## Signing in

No page, and no authorization by default: a page asks for a signed-in user with `[Authorize]`, or
`[Authorize(Policy = AuthorizationPolicies.Administrator)]`, and the router shows it only then
once the routes component uses `AuthorizeRouteView` in place of `RouteView`.

<!--#if (localAuth)-->
The sign-in is the application's own use cases, kept in a cookie. A page rendered statically posts
the form, calls `ILoginUseCase`, then `IVerifyTwoFactorUseCase` with the challenge when two-factor
is on, and hands the `LoginOutcome.Authenticated` it gets to `CookieSession.SignInAsync`:

```razor
@page "/account/sign-in"
@inject ILoginUseCase Login
@inject CookieSession Session
@inject NavigationManager Navigation

<EditForm Model="Input" method="post" OnValidSubmit="SignInAsync" FormName="sign-in">
    <InputText @bind-Value="Input.Email" />
    <InputText type="password" @bind-Value="Input.Password" />
    <button type="submit">Sign in</button>
</EditForm>

@code {
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm] private SignInForm Input { get; set; } = new();

    private async Task SignInAsync()
    {
        var outcome = await Login.ExecuteAsync(new LoginCommand(Input.Email, Input.Password));

        if (outcome is { IsSuccess: true, Value: LoginOutcome.Authenticated session }
            && await Session.SignInAsync(HttpContext, session))
        {
            Navigation.NavigateTo("/");
        }
    }

    private sealed class SignInForm
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}
```

`SignInAsync` validates the access token the use case issued with the API's own bearer settings, so
the cookie holds exactly the claims the API would see: the user, the session, the roles and the
time of the sign-in. It keeps the refresh token, encrypted in the cookie, for the sign-out, and
expires with it; it never rotates it, since two requests of the same page would present it twice.
The cookie is the session the API lists and ends
([ADR 0013](../decisions/0013-a-session-is-a-refresh-token-family.md)): every five minutes, a
request and an open circuit ask whether that session is still listed, and sign out the moment it is
not, after a password change, a sign-out everywhere or an administrator's lock. A form posting to
`/account/sign-out`, with its antiforgery token, ends it.

Passkeys need script in the page and are left to it.
<!--#endif-->
<!--#if (externalAuth)-->
The sign-in is your provider's, with the OpenID Connect code flow: `/account/sign-in` sends the
browser there and back, and the subject the provider returns is mapped to this project's owner as
the API maps it ([ADR 0017](../decisions/0017-external-subjects-map-to-internal-owners.md)). Roles
come from the same source as the API's: with `Roles:Source` set to `Claim`, the provider must put
them in the ID token, which is the token this host reads. A form posting to `/account/sign-out`,
with its antiforgery token, ends the cookie session; the provider's own session stays.
<!--#endif-->
<!--#if (publicApi)-->
Nobody signs in: this project authenticates nobody, and `ICurrentUser` names no caller.
<!--#endif-->

## Configure

The project reads the same sections as the API for what it composes, from the same environment in
Compose.
<!--#if (externalAuth)-->
Its client at the provider is `Authentication:External:Interactive`.
<!--#endif-->
<!--#if (!localAuth)-->
The keys that protect its cookie and its forms are kept in `DataProtection:KeysDirectory`, which
Compose mounts on a volume.
<!--#endif-->
<!--#if (localAuth)-->
The keys that protect its cookie and its forms are the identity module's key ring, in the database
the API shares with it.
<!--#endif-->
The [configuration reference](../reference/configuration.md) has each row. In Compose the project
answers on `127.0.0.1`, on the port `PRESENTATION_<NAME>_HOST_PORT` sets in `.env`.

## Tests

`HostCompositionTests` builds the host as Development does, with every registration validated,
resolves every use case it composes in an operation's scope, proves that scope carries the caller
and that a use case resolved outside one refuses to name it, and that the host runs no background
service of its own.
<!--#if (localAuth)-->
`CookieSessionTests` and `ActiveSessionProviderTests` hold the sign-in, the sign-out and the
revalidation.
<!--#endif-->
<!--#if (externalAuth)-->
`ExternalSignInMappingTests` holds the mapping of the provider's subject and both role sources,
`ConfigureExternalOpenIdConnectOptionsTests` the code flow and its client.
<!--#endif-->
<!--#if (!localAuth)-->
`DataProtectionOptionsValidatorTests` holds the key ring's setting.
<!--#endif-->
`NoHost_ReferencesAnotherHost` keeps it from reaching the API's jobs and migrations.

## Decisions

- [0022. A presentation project reaches the core through the API or the application layer](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)
