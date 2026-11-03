# Presentation projects

How each presentation project this project holds is created and composed, and how it signs people in or is called. Each has a section of its own after the shared ones.

## What you get

<!--#if (presentationBlazorServer)-->
`Src/Presentation/<Initials>.Portal` is the portal, a Blazor Web App with server interactivity. It runs on the
server and calls the use cases in its own process, with its own composition in its `Program.cs`:
the same features and modules as the other hosts, its own caller, its own sign-in, and neither jobs nor
migrations ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
Its tests are in `Tests/Presentation/<Initials>.Portal.UnitTests`.

<!--#endif-->
<!--#if (presentationAvalonia)-->
`Src/Presentation/<Initials>.Desktop` is the desktop app, an Avalonia app on the MVVM pattern, with
CommunityToolkit.Mvvm. It runs on the user's machine and calls the API over HTTP through the .NET
client of `Src/Clients/<Initials>.Client`, generated from the API's OpenAPI document
([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)). It references that client
alone, never the domain, the application layer or a module, so it composes no use case of its own
([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
Its tests are in `Tests/Presentation/<Initials>.Desktop.UnitTests`.

<!--#endif-->
<!--#if (presentationWpf)-->
`Src/Presentation/<Initials>.Desktop` is the desktop app, a WPF app for Windows on the MVVM pattern, with
CommunityToolkit.Mvvm. It runs on the user's machine and calls the API over HTTP through the .NET
client of `Src/Clients/<Initials>.Client`, generated from the API's OpenAPI document
([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)). The app targets
`net10.0-windows` and holds its windows alone. Its view models, its settings, its host and its
session are in `Src/Presentation/<Initials>.Desktop.Core`, on `net10.0`, which references that client
alone, never the domain, the application layer or a module, so it composes no use case of its own
([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
The app builds on every system, with `EnableWindowsTargeting`, and runs on Windows. Its tests are in
`Tests/Presentation/<Initials>.Desktop.Core.UnitTests`, against the `.Core` project, and run on every
system.

<!--#endif-->
<!--#if (presentationMcp)-->
`Src/Presentation/<Initials>.Mcp` is the MCP server, which speaks the Model Context Protocol: an agent's client, an
assistant or an editor, connects to it over Streamable HTTP and calls its tools, each of which runs
one use case in the server's own process. It has its own composition in its `Program.cs`: the same
features and modules as the other hosts, its own caller, its own authentication, and neither jobs nor
migrations ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
Every tool acts for the caller its credential names ([ADR 0027](../decisions/0027-an-mcp-server-acts-for-the-caller-its-credential-names.md)).
Its tests are in `Tests/Presentation/<Initials>.Mcp.UnitTests`.

<!--#endif-->
## Creating the project

`bootstrap` first runs `dotnet run Tools/Tasks.cs presentation-scaffold`, which creates each
presentation project the solution does not hold yet, adds it and its tests to the solution, and
formats what it wrote.

<!--#if (presentationBlazorServer)-->
The portal's own files come with this project: its project file, `Program.cs`, its settings, its
image, and the wiring under `Common/` and `Features/`. The pages come from Microsoft's own
template: the step runs `dotnet new blazor` from the template package and version the project's
descriptor pins, in a template hive of its own, and keeps what it writes under `Components` and
`wwwroot`. A file this project already holds is kept, which is why `Components/App.razor` and, under
local accounts, `Components/Pages/SignIn.razor` are the portal's own. It needs the network once.

<!--#endif-->
<!--#if (presentationAvalonia)-->
The desktop app's own files come with this project: its project file, `Program.cs`,
`App.axaml.cs`, its settings, the main window and its view model, and the wiring under `Common/`.
The rest comes from Avalonia's own template: the step runs `dotnet new avalonia.mvvm` from the
template package and version the project's descriptor pins, in a template hive of its own, and
keeps what it writes under `Assets`, `ViewModels` and `Views`, with `App.axaml`, its view locator
and `app.manifest`; a file the project already holds is kept. It needs the network once.
`bootstrap` later writes the client from the API's document with `dotnet run Tools/Tasks.cs api-client`.

<!--#endif-->
<!--#if (presentationWpf)-->
The desktop app and its `.Core` project come whole with this project. `dotnet new wpf` ships with
the .NET SDK alone, so no version of it can be pinned: the app is written in the shape it gives, a
`WinExe` on `net10.0-windows` with `UseWPF`, `App.xaml`, `MainWindow.xaml` and their code-behind.
`App.xaml` names no start-up window: `App.xaml.cs` builds the host and shows the main window with
its view model. The generator's assembly information file is left out, its theme attribute serving
custom control themes the app has none of. The step runs no template and adds the .NET client
beside the app, without the network. `bootstrap` later writes the client from the API's document
with `dotnet run Tools/Tasks.cs api-client`.

<!--#endif-->
<!--#if (presentationMcp)-->
The MCP server comes whole with this project, written in the shape `dotnet new mcpserver --transport remote`
gives a server: its project file, `Program.cs`, its settings, its image, its tools and the wiring
under `Common/` and `Features/`. No template runs: the step works offline.

<!--#endif-->
Commit what `bootstrap` adds: from then on `presentation-scaffold` leaves each project alone, and
the pages a framework's template wrote are this project's own.

A project generated without one takes it later from `dotnet new cleanarch-presentation`, which
writes the project, its tests and this page. `dotnet run Tools/Tasks.cs presentation-join <Project>.<Name>`
then makes the lines the `README.md` in the project's folder lists outside them, every one or none,
and records the project in `cleanarch.json`; `presentation-scaffold` creates it the same way.

<!--#if (presentationBlazorServer)-->
## Portal

### Calling a use case

A Blazor Server circuit lives for as long as its browser tab, and so does the dependency-injection
scope it was opened with. A handler resolved from it would keep its database context, its unit of
work and its tracked aggregates for that long, and two events of the same circuit would share
them. A component therefore sends each message in a scope of its own, through `OperationScope`:

```razor
@inject OperationScope Operations

@code {
    private async Task SaveAsync() =>
        _result = await Operations.RunAsync<ISender, Result<<Output>>>(
            sender => sender.Send(_command).AsTask());
}
```

The scope it opens carries the circuit's caller, so `ICurrentUser` names the person signed in and
ownership stays in the query ([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)). A page
rendered statically, a sign-in form among them, can inject `ISender` directly: its request is the
scope, and its user is the caller. A use case sent from anywhere else refuses to answer who calls
it, rather than guess.

### Portal sign-in

<!--#if (!publicApi)-->
A page needs no attribute to be protected: see [the default deny](#portal-security-headers) below. `[Authorize(Policy = AuthorizationPolicies.Administrator)]`
narrows a page to the administrators, and the router shows it only to them once the routes component
uses `AuthorizeRouteView` in place of `RouteView`.
<!--#endif-->

<!--#if (localAuth)-->
The sign-in is the application's own use cases, kept in a cookie. `Components/Pages/SignIn.razor`, at
`/account/sign-in`, is the page: rendered statically, anonymous with `[AllowAnonymous]`, it posts a
form, sends `LoginCommand`, then `VerifyTwoFactorCommand` with the challenge when two-factor is on, and
hands the `LoginOutcome.Authenticated` it gets to `CookieSession.SignInAsync`. A failure shows one
message for every cause, so the page reveals nothing about which accounts exist, and a `ReturnUrl` is
followed only when it is a path of the portal. It is the project's own to restyle.

`SignInAsync` validates the access token the use case issued with the bearer settings every host shares, so
the cookie holds exactly the claims a bearer token carries: the user, the session, the roles and the
time of the sign-in. It keeps the refresh token, encrypted in the cookie, for the sign-out, and
expires with it; it never rotates it, since two requests of the same page would present it twice.
The cookie is a session of the identity module
([ADR 0013](../decisions/0013-a-session-is-a-refresh-token-family.md)): every five minutes, a
request and an open circuit ask whether that session is still listed, and sign out the moment it is
not, after a password change, a sign-out everywhere or an administrator's lock. A form posting to
`/account/sign-out`, with its antiforgery token, ends it.

Passkeys need script in the page and are left to it.
<!--#endif-->
<!--#if (externalAuth)-->
The sign-in is your provider's, with the OpenID Connect code flow: `/account/sign-in` sends the
browser there and back, and the subject the provider returns is mapped to this project's owner as
every host maps it ([ADR 0017](../decisions/0017-external-subjects-map-to-internal-owners.md)). Roles
come from the same source in every host: with `Roles:Source` set to `Claim`, the provider must put
them in the ID token, which is the token this host reads. A form posting to `/account/sign-out`,
with its antiforgery token, ends the cookie session; the provider's own session stays.
<!--#endif-->
<!--#if (publicApi)-->
Nobody signs in: this project authenticates nobody, and `ICurrentUser` names no caller.
<!--#endif-->

### Portal security headers

Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer` and a `Content-Security-Policy`, and no `X-Powered-By`: the headers every
HTTP host of the project sends. Their values, and the `SecurityHeaders` options with the validator that refuses a blank policy or
one without `frame-ancestors`, are `Presentation.Core`'s (`SecurityHeaderValues`,
`AddSecurityHeaderOptions`), which each host registers. The middleware that writes them stays in each
host, because `Presentation.Core` holds no reference to ASP.NET Core
(`ThePortableProjects_DoNotReachTheAspNetFramework`).

The portal's default policy is its own, since a Blazor page needs what the API's `default-src 'none'`
refuses. `frame-ancestors 'none'` and `X-Frame-Options: DENY` both forbid framing.

| Directive | Value | Why |
|---|---|---|
| `default-src` | `'self'` | Nothing loads from another origin. |
| `script-src` | `'self'` | `blazor.web.js` and the project's own scripts. Never `'unsafe-inline'` nor `'unsafe-eval'`: an injected script does not run. |
| `style-src` | `'self' 'unsafe-inline'` | The pages `dotnet new blazor` write no `style` attribute, but the component libraries and the framework's own error and validation UI may, and a nonce cannot cover an attribute. A style cannot run code. A portal that has none tightens it to `'self'` in `SecurityHeaders:ContentSecurityPolicy`. |
| `connect-src` | `'self'` | The circuit's SignalR connection, over WebSockets or long polling, back to the portal. |
| `img-src` | `'self' data:` | Images of the portal and inline ones. |
| `font-src`, `object-src`, `base-uri`, `form-action` | `'self'`, `'none'`, `'self'`, `'self'` | Nothing else is allowed to be a font, a plug-in, a base or a form target. |

A script written inline in a page is refused by the browser, which logs it, so `App.razor` leaves out the
`<ImportMap />` component that `dotnet new blazor` writes, and every script is loaded by `@Assets[...]`: put a
script of your own in `wwwroot`. `ServedPageTests` holds that a served page has no inline script.
`SecurityHeaders:ContentSecurityPolicy` replaces the whole policy
([the configuration reference](../reference/configuration.md)).

<!--#if (!publicApi)-->
The default deny is a fallback authorization policy that asks for a signed-in user: a page or an
endpoint that does not say otherwise answers a caller who is not signed in with a challenge.
<!--#if (localAuth)-->
The challenge sends the browser to `/account/sign-in`.
<!--#endif-->
<!--#if (externalAuth)-->
The challenge sends the browser to `/account/sign-in`, which sends it on to the provider.
<!--#endif-->
What opts out is `/health` and `/health/ready`, the static assets
<!--#if (localAuth)-->
and the sign-in page, with `[AllowAnonymous]`, and the pages `/Error` and `/not-found`, by `AnonymousPages`.
<!--#endif-->
<!--#if (externalAuth)-->
and the sign-in route, with `.AllowAnonymous()`, and the pages `/Error` and `/not-found`, by `AnonymousPages`.
<!--#endif-->
<!--#if (api)-->
`HttpSurfaceTests` counts the opt-outs written with `.AllowAnonymous()`, so a new one is counted there.
<!--#endif-->
An interactive page for a caller who is not signed in is not supported: the circuit's own connection is
behind the policy too.
<!--#endif-->
<!--#if (publicApi)-->
This project authenticates nobody, so the portal sets no fallback policy: a policy that asks for a
caller would make every page unreachable.
<!--#endif-->

### Portal settings

The portal reads the same sections as the other hosts for what it composes, from the same environment in
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
The [configuration reference](../reference/configuration.md) has each row. In Compose the portal
answers on `127.0.0.1`, on the port `PRESENTATION_PORTAL_HOST_PORT` sets in `.env`.

### Portal tests

`HostCompositionTests` builds the portal's host as Development does, with every registration validated,
resolves every use case it composes in an operation's scope, proves that scope carries the caller
and that a use case resolved outside one refuses to name it, and that the host runs no background
service of its own.
<!--#if (localAuth)-->
Its test host keeps the data-protection key ring in memory: the host loads the ring as it starts,
and from the database it composes, which a unit test does not run, the execution strategy's retries
would hold each host for some twenty seconds.
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
`ServedPageTests` holds that a served page has no inline script.
<!--#if (localAuth)-->
`SignInPageTests` holds that a caller who is not signed in receives the form, and that a wrong password
sets no cookie.
<!--#endif-->
`SecurityHeaderTests` holds the headers and the policy's script, framing and circuit directives.
<!--#if (!publicApi)-->
`DefaultDenyTests` holds the fallback policy, and that every endpoint that does not opt out
challenges a caller who is not signed in.
<!--#endif-->
<!--#if (publicApi)-->
`DefaultDenyTests` holds that no fallback policy is set.
<!--#endif-->
`NoHost_ReferencesAnotherHost` keeps the portal from reaching the jobs and migrations of another host.
<!--#endif-->
<!--#if (presentationAvalonia || presentationWpf)-->
## Desktop app

### Calling the API

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

### Desktop sign-in

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

### Desktop settings

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
app with `dotnet run --project Src/Presentation/<Initials>.Desktop`.
<!--#if (presentationWpf)-->
The app runs on Windows alone; elsewhere the solution still builds it and runs the `.Core` tests.
<!--#endif-->

### Desktop tests

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
<!--#if (presentationMcp)-->
## MCP server

### The tools

<!--#if (anyFeature)-->
| Tool | Use case | Reads only |
|---|---|---|
<!--#if (todoLists)-->
| `list_todo_lists` | the caller's to-do lists, a page at a time | yes |
| `get_todo_list` | one list with its items | yes |
| `create_todo_list` | a new, empty list | no |
| `add_todo_item` | an item added to a list | no |
<!--#endif-->
<!--#if (reminders)-->
| `list_reminders` | the reminders of one item | yes |
| `schedule_reminder` | a reminder on one item | no |
<!--#endif-->
<!--#if (files)-->
| `list_files` | the caller's stored files, a page at a time, never their content | yes |
| `get_file` | one file's details | yes |
<!--#endif-->
<!--#if (notes)-->
| `list_notes` | the caller's notes, a page at a time | yes |
| `get_note` | one note with its body | yes |
| `create_note` | a new note | no |
<!--#endif-->

<!--#endif-->
<!--#if (!anyFeature)-->
This project has no feature yet, so the server offers no tool until one is declared.

<!--#endif-->
No tool deletes, updates or replaces. A tool answers its result as JSON, in the result's structured
content and as text. A failure is an error result carrying the problem's `code`, its `detail` and,
for a refused input, its field `errors`: a record of another caller answers exactly as one that does not exist
([ADR 0009](../decisions/0009-ownership-is-in-the-query.md)).
<!--#if (api)-->
The codes are the ones the [API reference](../reference/api.md) lists.
<!--#endif-->

### Declaring a tool

A tool is declared by hand, never because a use case exists. Each feature's tools are one class
under `Features/<Feature>/Tools`, whose constructor takes the `ISender` its tools send their messages
through; each method is one tool, named and described for the agent, and says whether it only reads:

```csharp
[McpServerToolType]
internal sealed class <Feature>Tools(ISender sender)
{
    [McpServerTool(Name = "get_<aggregate>", Title = "Get a <aggregate>", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Gets one of the caller's <aggregates>. One that is not the caller's answers as one that does not exist.")]
    public async Task<CallToolResult> Get<Aggregate>Async(
        [Description("The <aggregate>'s id.")] Guid <aggregate>Id,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await sender.Send(new Get<Aggregate>Query(<aggregate>Id), cancellationToken),
            <aggregate> => <aggregate>.Value);
}
```

`Program.cs` registers the class with `server.WithTools<<Feature>Tools>();`. A tool that creates
or changes takes `ToolPolicies.Write`, and `ReadOnly = false`. `ToolAnswer.From` turns the use
case's result into the tool's answer. Each tool joins the catalogue the tests read,
`TestSupport/ToolCatalogue.cs`, which `HostCompositionTests` and `McpAuthorizationTests` hold the
server to.

### Calling it

The server runs stateless on Streamable HTTP, at `/mcp`: each request is answered in a scope of its
own that carries its caller, and the server never sends the client a request of its own, so a tool
cannot ask the user anything mid-call.
<!--#if (localAuth)-->
A client authenticates with an access key: a signed-in user creates one with
`POST /api/v1/auth/access-keys`, choosing its scopes, `read`, `write` or both, with its current
password, and copies the key the answer shows once ([Authentication](authentication.md#access-keys)). The client sends it as a
bearer credential, which in an editor's MCP configuration is a header:

```json
{
  "servers": {
    "app-template": {
      "type": "http",
      "url": "http://localhost:8091/mcp",
      "headers": { "Authorization": "Bearer ak_..." }
    }
  }
}
```

Every request presents the key again, so revoking it, its expiry, or an account the API would no
longer sign in stops the next call. A key that grants `read` alone sees and calls the tools that
only read. A request without a valid key is a `401`.
<!--#endif-->
<!--#if (externalAuth)-->
A client authenticates with a token your provider issued for this server, whose audience is
`Mcp:Resource`, the address clients connect to; a token issued for the API is refused. A request
without one is a `401` whose `WWW-Authenticate` names the server's protected resource metadata,
`/.well-known/oauth-protected-resource/mcp`, which names the provider: a client that follows the
protocol's authorization signs its user in there and comes back with a token. The subject is mapped
to this project's owner as every host maps it
([ADR 0017](../decisions/0017-external-subjects-map-to-internal-owners.md)). The provider registers
the server as a resource with that audience, and each client as a public client with a loopback
redirect, unless it registers clients dynamically.
<!--#endif-->

### MCP settings

The server reads the same sections as the other hosts for what it composes, from the same environment in
Compose.
<!--#if (externalAuth)-->
It reads the provider from `Authentication:External` and its own address from `Mcp:Resource`,
which is required. The [configuration reference](../reference/configuration.md#mcp) has the row.
<!--#endif-->
<!--#if (localAuth)-->
It needs no section of its own: an access key is checked against the accounts' own database.
<!--#endif-->
In Compose the server answers on `127.0.0.1`, on the port `PRESENTATION_MCP_HOST_PORT` sets in
`.env`, at `/mcp`; `/health` and `/health/ready` answer anyone.

### MCP tests

`HostCompositionTests` builds the host as Development does, with every registration validated,
resolves every use case it composes and every tool type, holds the tools to their catalogue, and
proves the host runs no background service of its own. `McpAuthorizationTests` connects the
protocol's own client to the host in memory: a call without a credential is refused,
<!--#if (externalAuth)-->
and so is a token issued for the API; the resource metadata names the provider;
<!--#endif-->
<!--#if (localAuth)-->
and so is an unknown or revoked key; a key that only reads sees and calls no tool that writes;
<!--#endif-->
every tool says whether it only reads, and two callers are never one to the tools.
Those two guarantees, the refusal of a read-only key and the caller's identity, are proved on a probe
tool (`TestSupport/ProbeTools.cs`) the test host registers, so they hold in a project generated
without examples; with notes, a record of another caller also answers as an absent one.
`ToolAnswerTests` holds the answers and their problems, each tool's own tests the use case it calls
and what it passes.
<!--#if (externalAuth)-->
`McpOptionsValidatorTests` holds the server's address.
<!--#endif-->
`NoHost_ReferencesAnotherHost` keeps the server from reaching the jobs and migrations of another host.
<!--#if (localAuth)-->
Its test host keeps the data-protection key ring in memory: the host loads the ring as it starts,
and from the database it composes, which a unit test does not run, the execution strategy's retries
would hold each host for some twenty seconds.
<!--#endif-->
<!--#endif-->

## Decisions

- [0022. A presentation project reaches the core through the API or the application layer](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)
<!--#if (presentationMcp)-->
- [0027. An MCP server acts for the caller its credential names](../decisions/0027-an-mcp-server-acts-for-the-caller-its-credential-names.md)
<!--#endif-->
