# Presentation project

How the presentation project beside the API is created, composed, and how it signs people in.

## What you get

<!--#if (presentationBlazorServer)-->
`Src/Presentation/<Project>.<Name>` is a Blazor Web App with server interactivity. It runs on the
server and calls the use cases in its own process, with its own composition in its `Program.cs`:
the same features and modules as the API, its own caller, its own sign-in, and neither jobs nor
migrations ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
Its tests are in `Tests/Presentation/<Project>.<Name>.UnitTests`.
<!--#endif-->
<!--#if (presentationAvalonia)-->
`Src/Presentation/<Project>.<Name>` is an Avalonia desktop app on the MVVM pattern, with
CommunityToolkit.Mvvm. It runs on the user's machine and calls the API over HTTP through the .NET
client of `Src/Clients/<Project>.Client`, generated from the API's OpenAPI document
([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)). It references that client
alone, never the domain, the application layer or a module, so it composes no use case of its own
([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
Its tests are in `Tests/Presentation/<Project>.<Name>.UnitTests`.
<!--#endif-->
<!--#if (presentationWpf)-->
`Src/Presentation/<Project>.<Name>` is a WPF desktop app for Windows on the MVVM pattern, with
CommunityToolkit.Mvvm. It runs on the user's machine and calls the API over HTTP through the .NET
client of `Src/Clients/<Project>.Client`, generated from the API's OpenAPI document
([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)). The app targets
`net10.0-windows` and holds its windows alone. Its view models, its settings, its host and its
session are in `Src/Presentation/<Project>.<Name>.Core`, on `net10.0`, which references that client
alone, never the domain, the application layer or a module, so it composes no use case of its own
([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
The app builds on every system, with `EnableWindowsTargeting`, and runs on Windows. Its tests are in
`Tests/Presentation/<Project>.<Name>.Core.UnitTests`, against the `.Core` project, and run on every
system.
<!--#endif-->

## Creating the project

<!--#if (presentationBlazorServer)-->
The project's own files come with the project: its project file, `Program.cs`, its settings, its
image, and the wiring under `Common/` and `Features/`. The pages come from Microsoft's own
template: `bootstrap` first runs `dotnet run Tools/Tasks.cs presentation-scaffold`, which runs
`dotnet new blazor` from the template package and version the project's descriptor pins, in a
template hive of its own, and keeps what it writes under `Components` and `wwwroot`. It then adds
the project and its tests to the solution and formats them. The step needs the network once.
<!--#endif-->
<!--#if (presentationAvalonia)-->
The project's own files come with the project: its project file, `Program.cs`, `App.axaml.cs`,
its settings, the main window and its view model, and the wiring under `Common/`. The rest comes
from Avalonia's own template: `bootstrap` first runs `dotnet run Tools/Tasks.cs presentation-scaffold`,
which runs `dotnet new avalonia.mvvm` from the template package and version the project's
descriptor pins, in a template hive of its own, and keeps what it writes under `Assets`,
`ViewModels` and `Views`, with `App.axaml`, its view locator and `app.manifest`; a file the project
already holds is kept. It then adds the project and its tests to the solution and formats them. The
step needs the network once. `bootstrap` later writes the client from the API's document with
`dotnet run Tools/Tasks.cs api-client`.
<!--#endif-->
<!--#if (presentationWpf)-->
The app and its `.Core` project come whole with the project. `dotnet new wpf` ships with the .NET
SDK alone, so no version of it can be pinned: the app is written in the shape it gives, a `WinExe`
on `net10.0-windows` with `UseWPF`, `App.xaml`, `MainWindow.xaml` and their code-behind. `App.xaml`
names no start-up window: `App.xaml.cs` builds the host and shows the main window with its view
model. The generator's assembly information file is left out, its theme attribute serving custom
control themes the app has none of. `bootstrap` first runs `dotnet run Tools/Tasks.cs presentation-scaffold`, which
runs no template: it adds the app, its `.Core` project and their tests to the solution with the .NET
client, and formats them, without the network. `bootstrap` later writes the client from the API's
document with `dotnet run Tools/Tasks.cs api-client`.
<!--#endif-->

Commit the project after bootstrap: from then on its pages are this project's own, and
`presentation-scaffold` leaves it alone.

A project generated without one takes it later from `dotnet new cleanarch-presentation`, which
writes the project, its tests and this page. `dotnet run Tools/Tasks.cs presentation-join <Project>.<Name>`
then makes the lines the `README.md` in the project's folder lists outside them, every one or none,
and `presentation-scaffold` creates the project the same way.

<!--#if (presentationBlazorServer)-->
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
<!--#endif-->
<!--#if (presentationAvalonia || presentationWpf)-->
## Calling the API

<!--#if (presentationAvalonia)-->
The app builds a generic host in `Common/Hosting/AppHost.cs` before Avalonia starts.
<!--#else-->
`App.xaml.cs` builds the generic host of the `.Core` project's `Common/Hosting/AppHost.cs` before it
shows the main window, and disposes it when the app exits.
<!--#endif-->
<!--#if (localAuth)-->
The host binds and validates the `Api` settings, registers the client with `AddApiClient`, and the
view models. A view model asks for `ApiClient` and calls a route as the API's document names it:

```csharp
var me = await api.Api.V1.Auth.Me.GetAsync(cancellationToken: cancellationToken);
```
<!--#else-->
The host binds and validates the `Api` and `SignIn` settings, registers the session, the client with
`AddApiClient`, and the view models. A view model asks for `ApiClient` and calls a route as the
API's document names it:

```csharp
await api.Api.V1.ExternalIdentities[userId].DeleteAsync(cancellationToken: cancellationToken);
```
<!--#endif-->

A problem answer arrives as an `ApiException`; `ApiProblem.From` gives its code and its field
errors, the code the [API reference](../reference/api.md) lists. The client retries a transient
failure with the standard resilience handler.

## Signing in

<!--#if (localAuth)-->
The main window signs in with the API's own routes. `MainViewModel` posts the email and the password
to `POST /api/v1/auth/login`, hands the tokens to `ApiSession`, then reads `GET /api/v1/auth/me`.
`ApiSession` keeps the access token in memory and puts it on every call; on a `401` it refreshes
once, shared by every call waiting on it, and replays the call. The refresh token travels in the
request body, as a client outside a browser does, and is held in memory, so a restart signs out.
Signing out posts it to `POST /api/v1/auth/logout`. An account with a second factor is not signed
in: the window does not ask for it.
<!--#if (presentationWpf)-->
WPF binds no password box, so `MainWindow.xaml.cs` hands what is typed to the view model and clears
the box once the view model empties the password.
<!--#endif-->
<!--#endif-->
<!--#if (externalAuth)-->
The main window signs its user in at your OpenID Connect provider, the one the API trusts.
`DesktopSession`, in `Common/Sessions`, runs the authorization code flow with PKCE through
`Duende.IdentityModel.OidcClient`: it opens the system browser at the provider and receives the
answer on a loopback port it opens for that one sign-in, the redirect RFC 8252 gives a native app.
It holds the tokens in memory, so a restart signs out, and as the client's `IAccessTokenSource` it
puts the access token on every call, refreshed a minute before it expires when the provider issued
a refresh token. The window shows the name the identity token carries; the API, which has no
`/me` route under an external provider, is not asked. Signing out forgets the tokens and leaves the
session at the provider open. The README beside the app says how to register it at the provider: a
public client, the redirect URI `http://127.0.0.1/` on any port, and the API's audience in the
access token.
<!--#endif-->

## Configure

<!--#if (localAuth)-->
The app reads one section, `Api`, from the `appsettings.json` beside it: `BaseAddress`, the API's
address, which it validates before showing a window. The [configuration reference](../reference/configuration.md#api)
has the row.
<!--#else-->
The app reads two sections from the `appsettings.json` beside it, and validates both before showing
a window: `Api`, the API's address, and `SignIn`, its client at the provider. The generated file
leaves `SignIn:Authority` and `SignIn:ClientId` empty, so the app refuses to start until they are
set. The [configuration reference](../reference/configuration.md#api) has the rows.
<!--#endif--> Run the API, with `dotnet run Tools/Tasks.cs compose-up` or from its project, then the
app with `dotnet run --project Src/Presentation/<Project>.<Name>`.
<!--#if (presentationWpf)-->
The app runs on Windows alone; elsewhere the solution still builds it and runs the `.Core` tests.
<!--#endif-->

## Tests

`AppHostTests` builds the app's host and proves it refuses to start without an absolute API
address, and that the view model and the client compose against it; `ApiOptionsValidatorTests`
holds the setting.
<!--#if (localAuth)-->
`MainViewModelTests` drives the view model against a stub of the API: the sign-in, a refused one
with its problem code, an account with a second factor, and the sign-out.
<!--#else-->
`DesktopSessionTests` runs the code flow against a stub of the provider, through the real loopback
listener: PKCE, the redeemed code, a refused sign-in, an unreachable provider, the refresh ahead of
expiry shared by the calls waiting on it, and the sign-out. `LoopbackBrowserTests` sends the
browser's request to the listener; `AppHostTests` proves the client sends the session's token;
`MainViewModelTests` drives the window's sign-in and sign-out; `SignInOptionsValidatorTests` holds
the setting. No test reaches a real provider.
<!--#endif-->
<!--#if (presentationAvalonia)-->
The views are not tested. `EveryClient_ReachesTheCoreThroughTheApiAlone` keeps the app
to the client.
<!--#else-->
They test the `.Core` project, and run on every system; the windows and their code-behind are not
tested. `EveryClient_ReachesTheCoreThroughTheApiAlone` keeps the app to its `.Core` project, and
that project to the client.
<!--#endif-->
<!--#endif-->

## Decisions

- [0022. A presentation project reaches the core through the API or the application layer](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)
