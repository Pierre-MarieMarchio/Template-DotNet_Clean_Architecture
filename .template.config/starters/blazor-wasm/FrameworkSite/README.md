# FrameworkSite

<!--#if (adminAudience)-->
A Blazor WebAssembly back office over the API, for administrators.
<!--#else-->
A Blazor WebAssembly front end over the API.
<!--#endif-->
It runs in the browser and calls the API through the .NET client of `Src/Clients`, on the page's
own origin: nginx serves the app and relays `/api/` to the API, so the API keeps CORS closed.

The app itself is what `dotnet new blazorwasm` writes; this folder adds the project file,
`Program.cs`, which composes the client, and the wiring around it.

## Create the app

The folder starts with the wiring only: scaffold.json names the template package, the version it is
pinned to and the arguments of `dotnet new blazorwasm`. From the project root:

```bash
dotnet run Tools/Tasks.cs web-scaffold FrameworkSite
```

`bootstrap` runs the same step. It needs the network once, to fetch the template package. It keeps
what the template writes under `Layout`, `Pages`, `Properties` and `wwwroot`, with `App.razor` and
`_Imports.razor`, removes scaffold.json, then adds the project to the solution. Commit the app: from
then on its pages are this project's own, and the step leaves it alone.

## The client and the session

`Program.cs` registers the client with `AddApiClient`, at the page's own address.
<!--#if (localAuth)-->
It keeps the refresh token where a browser keeps it, in the API's `HttpOnly` cookie: the client asks
for the cookie mode on the session routes, holds the access token in memory, and refreshes it once
on a `401`.
<!--#endif-->
<!--#if (externalAuth)-->
Nobody signs in: the app holds no token from your provider, so the API answers `401` to every route
it calls.
<!--#endif-->

## Work on it

`dotnet watch --project Src/Web/apps/FrameworkSite` serves the pages alone, without the `/api`
relay. A page that calls the API runs through the stack: `docker compose up --build` builds the
app's image and serves it on the port below.

## What this folder holds

| File | What it is |
|---|---|
| `nginx.conf` | the server: the page security headers, caching, the `index.html` fallback, the `/api/` and `/health` relay; its Content-Security-Policy takes scripts from the app's origin only, plus `'wasm-unsafe-eval'`, which WebAssembly needs, and the SHA-256 of the import map the published page carries inline |
| `Dockerfile` | the image: the .NET SDK publishes the app from the repository root, and nginx serves `wwwroot`; `_framework/`, whose every file name carries its fingerprint, is cached for a year |
| `compose.yml` | the `web-framework-site` service, built from the repository root |

## Join the stack

`docker compose up` starts the app once `docker-compose.yml` includes it, under `include:`:

```yaml
  - path: Src/Web/apps/FrameworkSite/compose.yml
```

and `.env.example` names its port:

```bash
WEB_FRAMEWORK_SITE_HOST_PORT=8081
```

## Joining a project generated without one

A project generated with another `--web` value, or with a workspace from `cleanarch-webapp
--workspace`, takes a WebAssembly app later, at the project root:

```bash
<!--#if (hasClient)-->
dotnet new cleanarch-webapp -n FrameworkSite --framework blazor-wasm --project-name AppTemplate --scope app-template --has-client true
<!--#else-->
dotnet new cleanarch-webapp -n FrameworkSite --framework blazor-wasm --project-name AppTemplate --scope app-template
<!--#endif-->
```

Pass the project's `--auth`, `--examples`, `--files` and `--agent-files` too when they are not the
defaults: the client's files are written for them. Pass `--has-client true` when the project holds the
.NET client already, generated with `--presentation avalonia` or added with an Avalonia presentation
project. A project generated with `--auth none` publishes no route for it to call, and the item
template refuses it.

It writes this folder, the .NET client in `Src/Clients` with its tests, and the client's decision;
`--has-client true` leaves the client, its tests and its decision out, and its edits below with them.
The lines `--web blazor-wasm` writes in files the item template cannot edit are listed below, each
placed by the lines it goes beside. One task makes them all, or none when one of those lines is not
found exactly once:

```bash
dotnet run Tools/Tasks.cs web-join FrameworkSite
```

Then create the app and join the stack as above, and commit it. The edits, to make by hand where the
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
    <ItemGroup Label="Web app on WebAssembly">
      <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.14" />
      <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.14" />
    </ItemGroup>

  ```

- in `.dockerignore`, after the line:

  ```text
  Src/Web/
  ```

  add:

  ```text
  # The WebAssembly app builds from here, beside the solution's props and the .NET client.
  !Src/Web/apps/FrameworkSite/
  Src/Web/apps/FrameworkSite/bin/
  Src/Web/apps/FrameworkSite/obj/
  ```

<!--#if (agentFiles && !hasClient)-->
- in `AGENTS.md`, after the line:

  ```markdown
  the local tools and creates each context's initial migration, and the other tasks assume all four.
  ```

  add:

  ```markdown
  It then writes the .NET client from the OpenAPI reference with `dotnet run Tools/Tasks.cs api-client`.
  ```

- in `AGENTS.md`, after the line:

  ```markdown
  | After changing the model | `dotnet run Tools/Tasks.cs migration-add <Name>` | the initial migration bootstrap creates |
  ```

  add:

  ```markdown
  | After changing a route or a contract | `dotnet run Tools/Tasks.cs openapi`, then `dotnet run Tools/Tasks.cs api-client` | the tools bootstrap restores |
  ```

<!--#endif-->
<!--#if (agentFiles)-->
- in `Src/Web/AGENTS.md`, before the line:

  ```markdown
  | After a change to the API's routes or bodies | `pnpm api` |
  ```

  add:

  ```markdown
  | While you work on the WebAssembly app | `dotnet watch --project apps/<Name>`, its pages without the `/api` relay |
  ```

- in `Src/Web/AGENTS.md`, after the lines:

  ```markdown
  ## Rules

  ```

  add:

  ```markdown
  - The WebAssembly app starts from `dotnet new blazorwasm`, which `dotnet run Tools/Tasks.cs web-scaffold`
    runs once; it is a .NET project of the solution, built and formatted with it, and calls the API
    through the .NET client of `Src/Clients`, never through `@app-template/api-client`.
  ```

<!--#endif-->
- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
          ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
          ["Src/Web/apps/FrameworkSite"] = null,
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
          ["Src/Web/apps/FrameworkSite"] = null,
<!--#if (!hasClient && localAuth)-->
          ["Src/Clients/AppTemplate.Client"] = ["Composition", "Errors", "Sessions"],
<!--#elseif (!hasClient)-->
          ["Src/Clients/AppTemplate.Client"] = ["Composition", "Errors"],
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
          ["AppTemplate.FrameworkSite"] = ["Layout", "Pages", "wwwroot"],
<!--#if (!hasClient)-->
          ["AppTemplate.Client"] = ["Generated"],
<!--#endif-->
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

- in `docs/decisions/README.md`, after the line:

  ```markdown
  | [0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) | A presentation project reaches the core through the API or the application layer | Architecture |
  ```

  add:

  ```markdown
  | [0023](0023-a-dotnet-client-generated-by-kiota.md) | A .NET client generated by Kiota | Clients |
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```markdown
  `…Module.cs`) and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
  ```

  add:

  ```markdown
  The .NET client's root also holds `Generated/`, which `dotnet run Tools/Tasks.cs api-client`
  writes and nobody edits ([ADR 0023](../decisions/0023-a-dotnet-client-generated-by-kiota.md)).
  ```

- in `docs/reference/project-layout.md`, before the line:

  ```text
  Src/Web/                                the pnpm workspace: apps/ and packages/
  ```

  add:

  ```text
  Src/Clients/
<!--#if (localAuth)-->
    AppTemplate.Client                    Common/{Composition,Errors,Sessions}
<!--#else-->
    AppTemplate.Client                    Common/{Composition,Errors}
<!--#endif-->
                                          no Features/
                                          Generated/: Kiota's, from the OpenAPI reference
  ```

<!--#endif-->
- in `docs/reference/project-layout.md`, after the line:

  ```text
  Src/Web/                                the pnpm workspace: apps/ and packages/
  ```

  add:

  ```text
    apps/FrameworkSite                    the WebAssembly app, a .NET project: no Common/, no Features/
                                          Layout/, Pages/, wwwroot/: dotnet new blazorwasm's, at bootstrap
  ```
