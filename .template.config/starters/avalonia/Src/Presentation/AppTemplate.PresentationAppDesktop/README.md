# PresentationAppDesktop

The presentation project beside the API: an Avalonia desktop app that calls the API through the
.NET client of `Src/Clients`. [Presentation project](../../../docs/features/presentation.md)
describes it.

## Create the project

The folder starts with the project's own files: scaffold.json names the template package, its
version and the arguments of `dotnet new avalonia.mvvm`. From the project root:

```bash
dotnet run Tools/Tasks.cs presentation-scaffold AppTemplate.PresentationAppDesktop
```

`bootstrap` runs the same step. It needs the network once, to fetch the template package. It keeps
the files the template writes under `Assets`, `ViewModels` and `Views`, with `App.axaml`, its view
locator and `app.manifest`, where the folder holds none of its own; removes scaffold.json,
then adds the project, its tests and the .NET client to the solution. Run outside `bootstrap`, once
the OpenAPI reference exists, it then restores the tools and writes the client with
`dotnet run Tools/Tasks.cs api-client`. Commit the project: from then on its files are this
project's own, and the step leaves it alone.
<!--#if (externalAuth)-->

## Sign in

**Sign in** opens the system browser at the provider for the authorization code flow with PKCE,
which `Duende.IdentityModel.OidcClient` runs. The answer comes back to `http://127.0.0.1` on a port
the app opens for that one sign-in and closes after it: RFC 8252, section 7.3, the redirect a native
app uses, on the IP literal rather than `localhost` so that no name resolution or other interface
is involved. The app then redeems the code at the provider.

`DesktopSession` holds the tokens in memory only, so a restart signs out. It hands the access token
to the client, which sends it on every call, and refreshes it a minute before it expires when the
provider issued a refresh token; without one, the session ends with the access token. **Sign out**
forgets the tokens and redirects nowhere: the session at the provider stays open, so the next sign-in
may not ask for a password.

The name shown is read from the identity token (`name`, then `preferred_username`, `email`, `sub`);
the app calls no userinfo endpoint, so a provider without one signs in too. That token is not
verified: it comes straight from the provider's token endpoint over TLS, and it grants nothing; the
API verifies the access token.

Register the app at the provider before its first run:

- a public client, without a secret, since a desktop app cannot keep one, allowed the authorization
  code flow with PKCE;
- the redirect URI `http://127.0.0.1/` with any port, as RFC 8252 allows; a provider that cannot
  ignore the port does not suit this app as it is;
- the scopes of `SignIn:Scope`, with refresh tokens allowed for `offline_access`;
- the API's audience, `Authentication:External:Audience`, in the access token it issues. How depends
  on the provider: an API scope the client requests, added to `SignIn:Scope`; an audience mapper on
  the client; or a resource parameter.

Then set `SignIn:Authority`, the issuer the API trusts, and `SignIn:ClientId` in the `appsettings.json`
beside the app, its environment (`SignIn__ClientId`) or its command line: the app refuses to start
until both are set.
<!--#endif-->

## Joining a project generated without one

A project generated without this presentation project takes it later, at the project root:

```bash
<!--#if (hasClient)-->
dotnet new cleanarch-presentation -n PresentationAppDesktop --framework avalonia --project-name AppTemplate --has-client true
<!--#else-->
dotnet new cleanarch-presentation -n PresentationAppDesktop --framework avalonia --project-name AppTemplate
<!--#endif-->
```

Pass the project's `--auth`, `--examples`, `--files`, `--database` and `--agent-files` too when they
are not the defaults: the project's files are written for them. Pass `--has-client true` when the
project holds the .NET client already, generated with `--web blazor-wasm` or added with a Blazor
WebAssembly app. A project generated with `--auth none` publishes no route for it to call, and the
item template refuses it.

It writes this folder, its tests, the .NET client in `Src/Clients` with its tests,
`docs/features/presentation.md` and the client's decision; `--has-client true` leaves the client, its
tests and its decision out, and its edits below with them. The lines `--presentation` writes in
files the item template cannot edit are listed below, each placed by the lines it goes beside. One
task makes them all, or none when one of those lines is not found exactly once:

```bash
dotnet run Tools/Tasks.cs presentation-join AppTemplate.PresentationAppDesktop
```

Then create the project as above and commit it. `dnx CleanArch.Create presentation` runs the three
commands in turn, reading the options from the project's files. The edits, to make by hand where the
task stops:

<!--#if (!hasClient)-->
- in `.config/dotnet-tools.json`, replace the lines:

  ```json
        "rollForward": false
      }
    }
  }
  ```

  with:

  ```json
        "rollForward": false
      },
      "microsoft.openapi.kiota": {
        "version": "1.36.1",
        "commands": [
          "kiota"
        ],
        "rollForward": false
      }
    }
  }
  ```

- in `Directory.Packages.props`, after the lines:

  ```xml
      <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.11.0" />
    </ItemGroup>

  ```

  add:

  ```xml
    <!-- The version the pinned Kiota tool generates for; the two move together. -->
    <ItemGroup Label="API client">
      <PackageVersion Include="Microsoft.Kiota.Bundle" Version="2.1.2" />
    </ItemGroup>

  ```

<!--#endif-->
- in `Directory.Packages.props`, before the line:

  ```xml
    <!-- One 1.18.x band. -->
  ```

  add:

  ```xml
    <!-- One Avalonia 12.1.x band, the version Avalonia.Templates writes. -->
    <ItemGroup Label="Desktop presentation">
      <PackageVersion Include="Avalonia" Version="12.1.4" />
      <PackageVersion Include="Avalonia.Desktop" Version="12.1.4" />
      <PackageVersion Include="Avalonia.Fonts.Inter" Version="12.1.4" />
      <PackageVersion Include="Avalonia.Themes.Fluent" Version="12.1.4" />
      <PackageVersion Include="AvaloniaUI.DiagnosticsSupport" Version="2.2.3" />
      <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
<!--#if (externalAuth)-->
      <PackageVersion Include="Duende.IdentityModel.OidcClient" Version="7.1.0" />
<!--#endif-->
    </ItemGroup>

  ```

<!--#if (agentFiles)-->
- in `AGENTS.md`, after the line:

  ```markdown
  the local tools and creates each context's initial migration, and the other tasks assume all four.
  ```

  add:

  ```markdown
  It first creates the presentation project's pages with `dotnet run Tools/Tasks.cs presentation-scaffold`.
<!--#if (!hasClient)-->
  It then writes the .NET client from the OpenAPI reference with `dotnet run Tools/Tasks.cs api-client`.
<!--#endif-->
  ```

<!--#if (!hasClient)-->
- in `AGENTS.md`, after the line:

  ```markdown
  | After changing the model | `dotnet run Tools/Tasks.cs migration-add <Name>` | the initial migration bootstrap creates |
  ```

  add:

  ```markdown
  | After changing a route or a contract | `dotnet run Tools/Tasks.cs openapi`, then `dotnet run Tools/Tasks.cs api-client` | the tools bootstrap restores |
  ```

<!--#endif-->
<!--#endif-->
- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
          ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
          ["Src/Presentation/AppTemplate.PresentationAppDesktop"] = null,
<!--#if (!hasClient)-->
          ["Src/Clients/AppTemplate.Client"] = null,
<!--#endif-->
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
              ["Jobs", "Localization", "Observability", "Outbound", "Security"],
  ```

  add:

  ```csharp
<!--#if (externalAuth)-->
          ["Src/Presentation/AppTemplate.PresentationAppDesktop"] = ["Hosting", "Sessions"],
<!--#else-->
          ["Src/Presentation/AppTemplate.PresentationAppDesktop"] = ["Hosting"],
<!--#endif-->
<!--#if (!hasClient)-->
          ["Src/Clients/AppTemplate.Client"] = ["Composition", "Errors", "Sessions"],
<!--#endif-->
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  lines:

  ```csharp
      private static readonly Dictionary<string, string[]> _generatorRootFolders = new(StringComparer.Ordinal)
      {
  ```

  add:

  ```csharp
          ["AppTemplate.PresentationAppDesktop"] = ["Assets", "ViewModels", "Views"],
<!--#if (!hasClient)-->
          ["AppTemplate.Client"] = ["Generated"],
<!--#endif-->
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  lines:

  ```csharp
      private static readonly Dictionary<string, string[]> _generatorRootFiles = new(StringComparer.Ordinal)
      {
  ```

  add:

  ```csharp
          ["AppTemplate.PresentationAppDesktop"] = ["App.axaml.cs", "ViewLocator.cs"],
  ```

<!--#if (!hasClient)-->
- in `docs/architecture/http-api.md`, before the lines:

  ```markdown

  ## Errors and codes
  ```

  add:

  ```markdown
  The .NET client of `Src/Clients/AppTemplate.Client` is generated from the reference itself:
  `dotnet run Tools/Tasks.cs api-client` rewrites its `Generated/` folder with the Kiota the tool
  manifest pins, and `dotnet run Tools/Tasks.cs api-client --check`, which `verify` runs, fails while
  the folder differs from what Kiota writes. Run it after `openapi`, and the review reads both
  ([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)).
  ```

<!--#endif-->
- in `docs/architecture/layers-and-projects.md`, after the lines:

  ```markdown
  as it is, so a field added inside the application is never published by that edit alone.

  ```

  add:

  ```markdown
  ## The desktop app

  `Src/Presentation/<Project>.Desktop` is a desktop client: an Avalonia app that calls the API over
  HTTP through the .NET client of `Src/Clients/<Project>.Client`. It references that client alone
  (`EveryClient_ReachesTheCoreThroughTheApiAlone`), so it is no host: it composes no use
  case and no module. [Presentation projects](../features/presentation.md) describes it.

  ```

<!--#if (!hasClient)-->
- in `docs/decisions/README.md`, after the line:

  ```markdown
  | [0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) | A presentation project reaches the core through the API or the application layer | Architecture |
  ```

  add:

  ```markdown
  | [0023](0023-a-dotnet-client-generated-by-kiota.md) | A .NET client generated by Kiota | Clients |
  ```

<!--#endif-->
- in `docs/decisions/0001-adopt-the-generated-baseline.md`, before the lines:

  ```markdown

  ## Consequences
  ```

  add:

  ```markdown
  | An Avalonia desktop app beside the API, its views from `dotnet new avalonia.mvvm` at bootstrap, calling the API through the .NET client | A desktop app runs on the user's machine, where only the HTTP contract reaches the use cases | [Presentation project](../features/presentation.md) | [0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) |
  ```

- in `docs/decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md`,
  after the line:

  ```markdown
    host, go to `Presentation.Core` while it stays free of ASP.NET Core; the rest each host keeps.
  ```

  add:

  ```markdown
  - **The presentation project of this repository** is a desktop client. It calls the API through
    the .NET client and composes only that client, its settings and its view models; its caller is
    whoever the API's token names.
  ```

- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  Commit what it changes before anything else.
  ```

  add:

  ```markdown
  It creates the desktop app's views with `dotnet new avalonia.mvvm` and adds the project to the
  solution ([Presentation projects](../features/presentation.md#creating-the-project)), and last
  writes the .NET client from the OpenAPI reference.
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  It is a starting point. The modules and mechanisms it carries are meant to stay; the product's own
  ```

  add:

  ```markdown
  An Avalonia desktop app beside it calls the API through a .NET client generated from its document.
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  To change a value, remove the part by hand or add it:
  ```

  add:

  ```markdown
  | `--presentation` | `avalonia` | an Avalonia desktop app in `Src/Presentation`, its views created by `dotnet new avalonia.mvvm` at bootstrap, calling the API through the .NET client of `Src/Clients` ([Presentation projects](../features/presentation.md)) |
  ```

- in `docs/README.md`, before the lines:

  ```markdown

  ## Guides
  ```

  add:

  ```markdown
  | [The desktop app](features/presentation.md#desktop-app) | the desktop app, how it is created, and signing in to the API from it |
  ```

<!--#if (files)-->
- in `docs/reference/configuration.md`, before the line:

  ```markdown
  ## Files
  ```

<!--#else-->
- in `docs/reference/configuration.md`, before the line:

  ```markdown
  ## Mail
  ```

<!--#endif-->
  add:

  ```markdown
  ## Desktop app

  ### `Api`

  Read by the desktop app alone, from the `appsettings.json` beside it, its environment variables
  (`Api__BaseAddress`) or its command line (`--Api:BaseAddress=<address>`); the API never reads it.

  | Key | Type | Default | Notes |
  |---|---|---|---|
  | `BaseAddress` | absolute URI | `http://localhost:8080/` | **Required.** The API's `http` or `https` address, the API Compose publishes by default; the app refuses to start without it. |

<!--#if (externalAuth)-->
  ### `SignIn`

  Read by the desktop app alone, as the public client it signs its user in with at the provider, from
  the same places as `Api` (`SignIn__ClientId`, `--SignIn:ClientId=<id>`); the API never reads it.

  | Key | Type | Default | Notes |
  |---|---|---|---|
  | `Authority` | absolute URI | `""` | **Required.** The provider's issuer URL, the one `Authentication:External:Authority` names; `https` unless `RequireHttpsMetadata` is `false`. The app refuses to start without it. |
  | `ClientId` | string | `""` | **Required.** The public client the provider registered for the app: no secret, the redirect URI `http://127.0.0.1/` on any port. |
  | `Scope` | string | `openid profile email offline_access` | Must include `openid`. `offline_access` asks for the refresh token that keeps the session past the first access token; the API's own scope goes here when the provider issues its audience that way. |
  | `RequireHttpsMetadata` | bool | `true` | `false` for a provider on a developer's machine only. |

<!--#endif-->
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```markdown
  `…Module.cs`) and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
  ```

  add:

  ```markdown
  The desktop app's root also holds `Assets/`, `ViewModels/`, `Views/`, `App.axaml.cs` and
  the view locator, which its framework's template writes and which are its own from then on; a file
  named `<Type>.axaml.cs` declares `<Type>`.
<!--#if (!hasClient)-->
  The .NET client's root also holds `Generated/`, which `dotnet run Tools/Tasks.cs api-client`
  writes and nobody edits ([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)).
<!--#endif-->
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```text
                                                        Controllers,Jobs}
  ```

  add:

  ```text
<!--#if (externalAuth)-->
    <Project>.Desktop                     Common/{Hosting,Sessions}
<!--#else-->
    <Project>.Desktop                     Common/{Hosting}
<!--#endif-->
                                          no Features/
                                          Assets/, ViewModels/, Views/: dotnet new avalonia.mvvm's, at bootstrap
<!--#if (!hasClient)-->
  Src/Clients/
    AppTemplate.Client                    Common/{Composition,Errors,Sessions}
                                          no Features/
                                          Generated/: Kiota's, from the OpenAPI reference
<!--#endif-->
  ```
