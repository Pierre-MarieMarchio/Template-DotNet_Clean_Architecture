# PresentationAppDesktop

The presentation project beside the API: an Avalonia desktop app that calls the API through the
.NET client of `src/Presentation/AT.Presentation.Client`. [Presentation project](../../../docs/features/presentation.md)
describes it.

## Create the project

The folder starts with the project's own files: scaffold.json names the template package, its
version and the arguments of `dotnet new avalonia.mvvm`. From the project root:

```bash
dotnet cleanarch add presentation --name PresentationAppDesktop
```

`bootstrap` runs the same step. It needs the network once, to fetch the template package. It keeps
the files the template writes under `Assets`, `ViewModels` and `Views`, with `App.axaml`, its view
locator and `app.manifest`, where the folder holds none of its own; removes scaffold.json,
then adds the project, its tests and the .NET client to the solution. Run outside `bootstrap`, once
the OpenAPI reference exists, it then writes the client as
`dotnet cleanarch openapi` does. Commit the project: from then on its files are this
project's own, and the step leaves it alone.

The template's `Program.cs` turns on the AvaloniaUI Developer Tools; this folder's own replaces it
and leaves them out, with their `AvaloniaUI.DiagnosticsSupport` package, whose NuGet page declares
no licence. To use them, add the package and the `WithDeveloperTools()` call as the
[Avalonia documentation](https://docs.avaloniaui.net/) shows.
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

It writes this folder, its tests, the .NET client in `src/Presentation/AT.Presentation.Client` with its tests,
and `docs/features/presentation.md`; `--has-client true` leaves the client and its tests out, and its
edits below with them. The lines `--presentation` writes in
files the item template cannot edit are listed below, each placed by the lines it goes beside. One
command makes them all, leaving those in place already, or none when one of those lines is not found
exactly once:

```bash
dotnet cleanarch add presentation --name PresentationAppDesktop
```

It then creates the project as above; commit it. `dotnet cleanarch add presentation` alone also
runs the `dotnet new`, reading the options from `cleanarch.json`. The edits, to make by hand where
the command stops:

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
        "version": "1.38.0",
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
      <PackageVersion Include="Microsoft.Kiota.Bundle" Version="2.1.6" />
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
      <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  <!--#if (externalAuth)-->
      <PackageVersion Include="Duende.IdentityModel.OidcClient" Version="7.1.0" />
  <!--#endif-->
    </ItemGroup>

  ```

- in `tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
          ["src/Infrastructure/AT.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
          ["src/Presentation/AT.PresentationAppDesktop"] = null,
  <!--#if (!hasClient)-->
          ["src/Presentation/AT.Presentation.Client"] = null,
  <!--#endif-->
  ```

- in `tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
              ["Jobs", "Localization", "Observability", "Outbound", "Security"],
  ```

  add:

  ```csharp
  <!--#if (externalAuth)-->
          ["src/Presentation/AT.PresentationAppDesktop"] = ["Hosting", "Sessions"],
  <!--#else-->
          ["src/Presentation/AT.PresentationAppDesktop"] = ["Hosting"],
  <!--#endif-->
  <!--#if (!hasClient)-->
          ["src/Presentation/AT.Presentation.Client"] = ["Composition", "Errors", "Sessions"],
  <!--#endif-->
  ```

- in `tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  lines:

  ```csharp
      private static readonly Dictionary<string, string[]> _generatorRootFolders = new(StringComparer.Ordinal)
      {
  ```

  add:

  ```csharp
          ["AppTemplate.PresentationAppDesktop"] = ["Assets", "ViewModels", "Views"],
  <!--#if (!hasClient)-->
          ["AppTemplate.Presentation.Client"] = ["Generated"],
  <!--#endif-->
  ```

- in `tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
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
  The .NET client of `src/Presentation/AT.Presentation.Client` is generated from the reference itself:
  `dotnet cleanarch openapi` rewrites its `Generated/` folder with the Kiota the tool
  manifest pins, and `dotnet cleanarch openapi --check`, which `verify` runs, fails while
  the folder differs from what Kiota writes. Run it after `openapi`, and the review reads both
  ([the .NET client](layers-and-projects.md#the-net-client)).
  ```

<!--#endif-->
- in `docs/architecture/layers-and-projects.md`, after the lines:

  ```markdown
  as it is, so a field added inside the application is never published by that edit alone.

  ```

  add:

  ```markdown
  ## The desktop app

  `src/Presentation/<Initials>.Desktop` is a desktop client: an Avalonia app that calls the API over
  HTTP through the .NET client of `src/Presentation/<Initials>.Presentation.Client`. It references
  that client alone (`EveryClient_ReachesTheCoreThroughTheApiAlone`), so it is no host: it composes
  no use case and no module. [Presentation projects](../features/presentation.md) describes it.

  ```

<!--#if (!hasClient)-->
- in `docs/architecture/layers-and-projects.md`, before the line:

  ```markdown
  ## Composition
  ```

  add:

  ```markdown
  ## The .NET client

  `AppTemplate.Presentation.Client` is the .NET client of the API, beside
  `AppTemplate.Presentation.Core`: Kiota writes its `Generated/` folder from the OpenAPI reference,
  and `Common/` adds the base address, the resilience handler, the session and the reading of a
  problem's `code`. Only a presentation that runs on the user's machine calls it, so it is a
  presentation project and not a layer of its own. It references no project, and a presentation that
  calls the API through it references it alone (`EveryClient_ReachesTheCoreThroughTheApiAlone`).

  A folder of its own beside the four layers would read as a fifth layer, though nothing but a
  presentation calls it, and the rule that keeps it from the core names the project, not a folder.
  Inside `Presentation.Core`, which references the application layer, it would let a desktop or
  WebAssembly app ship the server's code and reach the use cases in process. Types written by hand
  would drift from what the API returns, with nothing to say when a change was missed. NSwag's C#
  output does not build here and reads its types by reflection, which trimming breaks; Refitter adds a
  third-party generator with telemetry on by default and problems parsed by hand. Every file Kiota
  writes opens with its `// <auto-generated/>` header and `.editorconfig` marks `**/Generated/**.cs` as
  generated, so the analyzers and `dotnet format` leave it alone, and the `#pragma` lines Kiota writes
  stay in `Generated/`; `openapi --check` fails when the folder drifts from the OpenAPI reference, as
  [the gate](../guides/build-and-ci.md#the-gate) says. A change to the contract is then a compile error
  in every .NET client that relied on the old one.

  ```

<!--#endif-->
- in `docs/architecture/README.md`, before the lines:

  ```markdown

  A row's reason stops holding when the platform moves, when its cost shows up in an incident or a
  ```

  add:

  ```markdown
  | An Avalonia desktop app, its views from `dotnet new avalonia.mvvm` at bootstrap, calling the API through the .NET client | A desktop app runs on the user's machine, where only the HTTP contract reaches the use cases | [Presentation projects](../features/presentation.md) | — |
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
  | `--presentation` | `avalonia` | an Avalonia desktop app in `src/Presentation`, its views created by `dotnet new avalonia.mvvm` at bootstrap, calling the API through the .NET client of `src/Presentation/AT.Presentation.Client` ([Presentation projects](../features/presentation.md)) |
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
  The .NET client's root also holds `Generated/`, which `dotnet cleanarch openapi`
  writes and nobody edits ([the .NET client](../architecture/layers-and-projects.md#the-net-client)).
  <!--#endif-->
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```text
                                                        Controllers,Jobs}
  ```

  add:

  ```text
  <!--#if (externalAuth)-->
    <Initials>.Desktop                    Common/{Hosting,Sessions}
  <!--#else-->
    <Initials>.Desktop                    Common/{Hosting}
  <!--#endif-->
                                          no Features/
                                          Assets/, ViewModels/, Views/: dotnet new avalonia.mvvm's, at bootstrap
  ```

<!--#if (!hasClient)-->
- in `docs/reference/project-layout.md`, after the lines:

  ```text
    AT.Presentation.Core                  Common/{Jobs,Localization,Observability,Outbound,Security}
                                          no Features/
  ```

  add:

  ```text
    AT.Presentation.Client                Common/{Composition,Errors,Sessions}
                                          no Features/
                                          Generated/: Kiota's, from the OpenAPI reference
  ```

<!--#endif-->