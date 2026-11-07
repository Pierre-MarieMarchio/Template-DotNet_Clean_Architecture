# PresentationAppPortal

The presentation project beside the API: a Blazor Web App with server interactivity that calls the
use cases in its own process. [Presentation project](../../../docs/features/presentation.md)
describes it.

## Create the project

The folder starts with the project's own files: scaffold.json names the template package, its
version and the arguments of `dotnet new blazor`. From the project root:

```bash
dotnet cleanarch add presentation --name PresentationAppPortal
```

`bootstrap` runs the same step. It needs the network once, to fetch the template package. It keeps
the pages the template writes under `Components` and `wwwroot`, removes scaffold.json, then adds
the project and its tests to the solution. Commit the project: from then on its pages are this
project's own, and the step leaves it alone.

## Joining a project generated without one

A project generated without this presentation project takes it later, at the project root:

```bash
dotnet new cleanarch-presentation -n PresentationAppPortal --framework blazor-server --project-name AppTemplate
```

Pass the project's `--auth`, `--examples`, `--files`, `--database`, `--agent-files` and `--has-api` too
when they are not the defaults: the project's files are written for them.

It writes this folder, its tests and `docs/features/presentation.md`. The lines `--presentation`
writes in files the item template cannot edit are listed below, each placed by the lines it goes
beside. One command makes them all, leaving those in place already, or none when one of those lines is
not found exactly once:

```bash
dotnet cleanarch add presentation --name PresentationAppPortal
```

It then creates the project as above; commit it. `dotnet cleanarch add presentation` alone also
runs the `dotnet new`, reading the options from `cleanarch.json`. The edits, to make by hand where
the command stops:

- in `.dockerignore`, after the line:

  ```text
  **/appsettings*.json
  ```

  add:

  ```text
  !Src/Presentation/AT.PresentationAppPortal/appsettings.json
  ```

- in `.env.example`, before the line:

  ```bash
  # Mailpit host ports, loopback only: the web UI is where the mails land.
  ```

  add:

  ```bash
  # The Blazor presentation project, loopback only.
  PRESENTATION_PRESENTATION_APP_PORTAL_HOST_PORT=8090

  ```

- in `docs/getting-started/first-run.md`, before the line:

  ```markdown
  | Mailpit, where mails land | <http://localhost:8025> |
  ```

  add:

  ```markdown
  | The portal | <http://localhost:8090> |
  ```

- in `README.md`, before the line:

  ```markdown
  | Mailpit | <http://localhost:8025> |
  ```

  add:

  ```markdown
  | The portal | <http://localhost:8090> |
  ```

<!--#if (externalAuth)-->
- in `.env.example`, after the line:

  ```bash
  EXTERNAL_AUTH_ADMINISTRATOR_ROLE=Admin
  ```

  add:

  ```bash
  # The client at the provider that signs people in with the code flow, for the presentation project (Authentication:External:Interactive) and a server-rendered web app alike; a public client leaves the secret empty.
  EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID=
  EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET=
  ```

- in `Directory.Packages.props`, after the line:

  ```xml
      <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.14" />
  ```

  add:

  ```xml
      <PackageVersion Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.14" />
  ```

<!--#endif-->
- in `docker-compose.yml`, after the mailpit service's last lines:

  ```yaml
        - "127.0.0.1:${MAILPIT_SMTP_PORT:-1025}:1025"
      networks: [backend]

  ```

  add:

  ```yaml
    presentation-presentation-app-portal:
      # The presentation project reads the hosts' settings with its own composition, and runs neither jobs nor migrations.
      extends:
        file: docker-compose.database.yml
        service: database-client
      image: app-template-presentation-app-portal:dev
      build:
        context: .
        dockerfile: Src/Presentation/AT.PresentationAppPortal/Dockerfile
      restart: unless-stopped
      environment:
        <<: *host-environment
  <!--#if (!localAuth)-->
        DataProtection__KeysDirectory: /data-protection-keys
  <!--#endif-->
  <!--#if (externalAuth)-->
        Authentication__External__Interactive__ClientId: ${EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID:-}
        Authentication__External__Interactive__ClientSecret: ${EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET:-}
  <!--#endif-->
  <!--#if (!localAuth)-->
      volumes:
        - presentation-app-portal-keys:/data-protection-keys
  <!--#endif-->
      ports:
        - "127.0.0.1:${PRESENTATION_PRESENTATION_APP_PORTAL_HOST_PORT:-8090}:8080"
      depends_on:
  <!--#if (api)-->
        api:
          condition: service_healthy
  <!--#endif-->
  <!--#if (!api && serverDatabase)-->
        db:
          condition: service_healthy
  <!--#endif-->
  <!--#if (!api)-->
        mailpit:
          condition: service_started
  <!--#endif-->
  <!--#if (!api && files)-->
        objectstore:
          condition: service_healthy
  <!--#endif-->
      healthcheck:
        test: ["CMD-SHELL", "wget --spider --quiet http://127.0.0.1:8080/health || exit 1"]
        interval: 15s
        timeout: 3s
        retries: 5
        start_period: 30s
      networks: [backend]

  ```

<!--#if (!localAuth)-->
- in `docker-compose.yml`, after the line:

  ```yaml
    db-data:
  ```

  add:

  ```yaml
    presentation-app-portal-keys:
  ```

<!--#endif-->
<!--#if (agentFiles)-->
- in `AGENTS.md`, after the line:

  ```markdown
  migration, and the other commands assume it.
  ```

  add:

  ```markdown
  It first creates the presentation project's pages.
  ```

<!--#endif-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
          ["Src/Infrastructure/AT.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
  <!--#if (localAuth)-->
          ["Src/Presentation/AT.PresentationAppPortal"] = ["Composition"],
  <!--#endif-->
  <!--#if (externalAuth)-->
          ["Src/Presentation/AT.PresentationAppPortal"] = ["Composition", "Configuration/Options"],
  <!--#endif-->
  <!--#if (publicApi)-->
          ["Src/Presentation/AT.PresentationAppPortal"] = null,
  <!--#endif-->
  ```

- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
              ["Jobs", "Localization", "Observability", "Outbound", "Security"],
  ```

  add:

  ```csharp
          ["Src/Presentation/AT.PresentationAppPortal"] = ["Hosting", "Security"],
  ```

- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  lines:

  ```csharp
      private static readonly Dictionary<string, string[]> _generatorRootFolders = new(StringComparer.Ordinal)
      {
  ```

  add:

  ```csharp
          ["AppTemplate.PresentationAppPortal"] = ["Components", "wwwroot"],
  ```

- in `Tests/Architecture/AT.Architecture.Tests/Rules/OutboundHttpTests.cs`, after the
  lines:

  ```csharp
          string[] hosts =
          [
  ```

  add:

  ```csharp
              "AppTemplate.PresentationAppPortal",
  ```

<!--#if (api)-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/HttpSurfaceTests.cs`, replace the line:

  ```csharp
      private const int _fluentlyAnonymousEndpoints = 4;
  ```

  with:

  ```csharp
  <!--#if (externalAuth)-->
      private const int _fluentlyAnonymousEndpoints = 8;
  <!--#endif-->
  <!--#if (!externalAuth)-->
      private const int _fluentlyAnonymousEndpoints = 7;
  <!--#endif-->
  ```

<!--#endif-->
<!--#if (api)-->
- in `docs/architecture/layers-and-projects.md`, replace the line:

  ```markdown
  `AppTemplate.Api` is the one project on the Web SDK. It holds the controllers, the background jobs,
  ```

  with:

  ```markdown
  `AppTemplate.Api` is the host of the HTTP contract. It holds the controllers, the background jobs,
  ```

<!--#endif-->
- in `docs/architecture/layers-and-projects.md`, before the line:

  ```markdown
  ## Composition
  ```

  add:

  ```markdown
  ## The portal

  `Src/Presentation/<Initials>.Portal` is a host: a Blazor Web App that calls the use cases in
  its own process. It references the application projects and the modules it composes, and never another
  host (`NoHost_ReferencesAnotherHost`); under `Common/` and `Features/` it keeps
  its own caller, its sign-in and the adapters a host supplies. [Presentation projects](../features/presentation.md)
  describes it.

  ```

- in `docs/decisions/0001-adopt-the-generated-baseline.md`, before the lines:

  ```markdown

  ## Consequences
  ```

  add:

  ```markdown
  | A Blazor Web App with server interactivity beside the API, its pages from `dotnet new blazor` at bootstrap, composing the use cases in process | A server-rendered site needs no client of the API and no token per user | [Presentation project](../features/presentation.md) | — |
  ```

- in `docs/decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md`,
  after the line:

  ```markdown
    host, go to `Presentation.Core` while it stays free of ASP.NET Core; the rest each host keeps.
  ```

  add:

  ```markdown
  - **The presentation project of this repository** composes the use cases in process. Its caller is
    the circuit's or the request's principal, given to each operation's own scope, so a use case
    never shares a database context across a circuit's lifetime.
  ```

<!--#if (accounts)-->
- in `docs/features/authentication.md`, before the line:

  ```markdown
  ## Roles and policies
  ```

  add:

  ```markdown
  ## The portal

  The portal signs people in to itself, with a cookie, over the same accounts: the
  [presentation projects](presentation.md#portal-sign-in) page describes its sign-in and its session.

  ```

<!--#endif-->
- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  Commit what it changes before anything else.
  ```

  add:

  ```markdown
  It creates the portal's pages with `dotnet new blazor` and adds the project to the solution
  ([Presentation projects](../features/presentation.md#creating-the-project)).
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  It is a starting point. The modules and mechanisms it carries are meant to stay; the product's own
  ```

  add:

  ```markdown
  A Blazor Web App beside it calls the same use cases in its own process.
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  To change a value, remove the part by hand or add it:
  ```

  add:

  ```markdown
  | `--presentation` | `blazor-server` | a Blazor Web App with server interactivity in `Src/Presentation`, its pages created by `dotnet new blazor` at bootstrap ([Presentation projects](../features/presentation.md)) |
  ```

- in `docs/guides/adding-a-feature.md`, after the line:

  ```markdown
  make by hand and names the one that failed. An edit already in place counts as made, never repeated.
  ```

  add:

  ```markdown
  Each host composes itself, so the call goes in the `Program.cs` of the portal or the MCP server too, and a first
  owner feature writes and registers each host's account directory beside the API's; the edits name
  both ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
  ```

<!--#if (todoLists || notes)-->
- in `docs/guides/removing-features.md`, after the line:

  ```markdown
  | `Src/Infrastructure/AT.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs` | the set, the schema constant, the `ApplyConfiguration` calls |
  ```

  add:

  ```markdown
  | the portal's `Program.cs` | the `Add<Features>()` line |
  ```

<!--#endif-->
- in `SECURITY.md`, after the line:

  ```markdown
  | An unprivileged image exposing 8080 only | [Deployment › The image](docs/guides/deployment.md#the-image) |
  ```

  add:

  ```markdown
  | The portal sends the API's security headers and a policy that allows no script but its own and forbids framing; where the project authenticates, every page asks for a signed-in user unless it opts out | [Presentation projects › Portal security headers](docs/features/presentation.md#portal-security-headers) |
  ```

- in `docs/reference/configuration.md`, replace the line:

  ```markdown
  | `ContentSecurityPolicy` | string | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | Sent on every API response. |
  ```

  with:

  ```markdown
  | `ContentSecurityPolicy` | string | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | Sent on every API response, and on every portal response, which starts from a default of its own ([portal security headers](../features/presentation.md#portal-security-headers)). A blank value, or one without `frame-ancestors`, stops the host. |
  ```

<!--#if (externalAuth)-->
- in `docs/reference/configuration.md`, after the lines:

  ```markdown
  | `Roles:AdministratorRole` | string | `Admin` | The role the `Administrator` policy requires. |

  ```

  add:

  ```markdown
  The portal reads this section too, as the provider it signs people in with.

  ### `Authentication:External:Interactive`

  The portal's client at the provider, for the OpenID Connect code flow; the API never reads it.

  | Key | Type | Default | Notes |
  |---|---|---|---|
  | `ClientId` | string | `""` | **Required.** The client the provider registered for the portal. |
  | `ClientSecret` | string | — | Its secret, for a confidential client; a public client sends none and relies on PKCE. |

  ```

<!--#endif-->
<!--#if (!localAuth && files)-->
- in `docs/reference/configuration.md`, before the line:

  ```markdown
  ## Files
  ```

<!--#endif-->
<!--#if (!localAuth && !files)-->
- in `docs/reference/configuration.md`, before the line:

  ```markdown
  ## Mail
  ```

<!--#endif-->
<!--#if (!localAuth)-->
  add:

  ```markdown
  ## Portal

  ### `DataProtection`

  Read by the portal alone, for the keys that protect its session cookie and its forms.

  | Key | Type | Default | Notes |
  |---|---|---|---|
  | `KeysDirectory` | string | — | **Required outside Development.** An absolute folder every instance shares and that outlives a restart; Compose mounts a volume at `/data-protection-keys`. |

  ```

<!--#endif-->
- in `docs/reference/project-layout.md`, after the line:

  ```markdown
  `…Module.cs`) and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
  ```

  add:

  ```markdown
  The portal's root also holds `Components/` and `wwwroot/`, which its framework's
  template writes and which are its own from then on.
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```text
                                                        Controllers,Jobs}
  ```

  add:

  ```text
  <!--#if (localAuth)-->
    <Initials>.Portal                     Common/{Hosting,Security}
                                          Features/<F>/Composition
                                          Components/, wwwroot/: dotnet new blazor's, at bootstrap
  <!--#endif-->
  <!--#if (externalAuth)-->
    <Initials>.Portal                     Common/{Hosting,Security}
                                          Features/<F>/{Composition,Configuration/Options}
                                          Components/, wwwroot/: dotnet new blazor's, at bootstrap
  <!--#endif-->
  <!--#if (publicApi)-->
    <Initials>.Portal                     Common/{Hosting,Security}
                                          no Features/
                                          Components/, wwwroot/: dotnet new blazor's, at bootstrap
  <!--#endif-->
  ```
