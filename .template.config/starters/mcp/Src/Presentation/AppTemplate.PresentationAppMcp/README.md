﻿# PresentationAppMcp

The presentation project beside the API: an MCP server whose tools call the use cases in its own
process, for the caller its credential names. [Presentation project](../../../docs/features/presentation.md)
describes it.

## Create the project

The folder holds the whole project, written in the shape `dotnet new mcpserver --transport remote`
gives an MCP server at the version scaffold.json names; no template runs. From the project root:

```bash
dotnet run Tools/Tasks.cs presentation-scaffold AppTemplate.PresentationAppMcp
```

`bootstrap` runs the same step. It needs no network: it removes scaffold.json, adds the project and
its tests to the solution and formats them. Commit the project: from then on the step leaves it
alone.

## Connect a client

The server answers on `/mcp`, at `http://localhost:5190/mcp` under `dotnet run` and at the port
`PRESENTATION_PRESENTATION_APP_MCP_HOST_PORT` sets in Compose.
<!--#if (localAuth)-->
A client sends an access key a signed-in user created with `POST /api/v1/auth/access-keys`, as an
`Authorization: Bearer` header.
<!--#endif-->
<!--#if (externalAuth)-->
A client signs its user in at the provider the protected resource metadata names, for a token whose
audience is `Mcp:Resource`.
<!--#endif-->

## Joining a project generated without one

A project generated without an MCP server takes it later, at the project root:

```bash
dotnet new cleanarch-presentation -n Mcp --framework mcp --project-name AppTemplate
```

Pass the project's `--auth`, `--examples`, `--files`, `--database`, `--agent-files` and `--has-api` too
when they are not the defaults: the project's files are written for them.

It writes this folder, its tests, `docs/features/presentation.md` and its decision.
<!--#if (localAuth)-->
It also writes the access keys' own files, their tests and their decision.
<!--#endif-->
The lines `--presentation` writes in files the item template cannot edit are listed below, each placed by the
lines it goes beside. One task makes them all, or none when one of those lines is not found exactly
once:

```bash
dotnet run Tools/Tasks.cs presentation-join AppTemplate.PresentationAppMcp
```

Then create the project as above and commit it.
<!--#if (localAuth)-->
The edits bring the access keys the server's callers present, whose table changes the
authentication context's model: in a project whose migrations exist already, add one:

```bash
dotnet ef migrations add AddAccessKeys \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Auth \
  --output-dir Migrations
```

<!--#endif--> `dnx CleanArch.Create presentation` runs the three
commands in turn, reading the options from the project's files. The edits, to make by hand where the
task stops:

<!--#if (localAuth)-->
- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/AuthModule.cs`, before the line:

  ```csharp
  using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
  ```

  add:

  ```csharp
  using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/AuthModule.cs`, before the line:

  ```csharp
  using AppTemplate.Infrastructure.Auth.Features.Accounts.Access.Queries;
  ```

  add:

  ```csharp
  using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Access.Tables;
  using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Adapters.Services;
  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/AuthModule.cs`, after the line:

  ```csharp
          services.TryAddScoped<ISpentPasskeyCeremonyTable, SpentPasskeyCeremonyTable>();
  ```

  add:

  ```csharp
          services.TryAddScoped<IAccessKeyTable, AccessKeyTable>();
  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/AuthModule.cs`, after the line:

  ```csharp
          services.AddScoped<IPasskeyService, PasskeyService>();
  ```

  add:

  ```csharp
          services.AddScoped<IAccessKeysService, AccessKeysService>();
  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Common/Contexts/AuthDbContext.cs`, before the line:

  ```csharp
  using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Configurations;
  ```

  add:

  ```csharp
  using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Configurations;
  using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Models;
  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Common/Contexts/AuthDbContext.cs`, after the lines:

  ```csharp
      internal DbSet<SpentPasskeyCeremonyRecord> SpentPasskeyCeremonies => Set<SpentPasskeyCeremonyRecord>();

  ```

  add:

  ```csharp
      internal DbSet<AccessKeyRecord> AccessKeys => Set<AccessKeyRecord>();

  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Common/Contexts/AuthDbContext.cs`, after the line:

  ```csharp
          builder.ApplyConfiguration(new SpentPasskeyCeremonyConfiguration());
  ```

  add:

  ```csharp
          builder.ApplyConfiguration(new AccessKeyConfiguration());
  ```

- in `Src/Application/AppTemplate.Application.Auth/Features/SecurityEvents/Ports/SecurityEventLog/SecurityEventKind.cs`, after the line:

  ```csharp
      PasskeySignInFailed,
  ```

  add:

  ```csharp

      AccessKeyCreated,

      AccessKeyRevoked,
  ```

- in `Src/Application/AppTemplate.Application.Auth/Features/SecurityEvents/Ports/SecurityEventLog/SecurityEvent.cs`, after the line:

  ```csharp
          new(SecurityEventKind.PasskeySignInFailed, userId);
  ```

  add:

  ```csharp

      public static SecurityEvent AccessKeyCreated(Guid userId) =>
          new(SecurityEventKind.AccessKeyCreated, userId);

      public static SecurityEvent AccessKeyRevoked(Guid userId) =>
          new(SecurityEventKind.AccessKeyRevoked, userId);
  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/SecurityEvents/Adapters/Services/SecurityEventLog.cs`, after the lines:

  ```csharp
                  LogPasskeySignInFailed(logger, securityEvent.UserId);
                  break;

  ```

  add:

  ```csharp
              case SecurityEventKind.AccessKeyCreated:
                  LogAccessKeyCreated(logger, securityEvent.UserId);
                  break;

              case SecurityEventKind.AccessKeyRevoked:
                  LogAccessKeyRevoked(logger, securityEvent.UserId);
                  break;

  ```

- in `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/SecurityEvents/Adapters/Services/SecurityEventLog.cs`, after the line:

  ```csharp
      private static partial void LogPasskeySignInFailed(ILogger logger, Guid? userId);
  ```

  add:

  ```csharp

      [LoggerMessage(Level = LogLevel.Information, Message = "An access key was created for user {UserId}.")]
      private static partial void LogAccessKeyCreated(ILogger logger, Guid? userId);

      [LoggerMessage(Level = LogLevel.Information, Message = "An access key of user {UserId} was revoked.")]
      private static partial void LogAccessKeyRevoked(ILogger logger, Guid? userId);
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/PortConventionTests.cs`, before the line:

  ```csharp
          "Accounts: 15",
  ```

  add:

  ```csharp
          "AccessKeys: 4",
  ```

- in `docs/reference/api.md`, after the line:

  ```markdown
  | DELETE | `/passkeys/{credentialId}` | 204 | Removes one; ends every other session. |
  ```

  add:

  ```markdown
  | POST | `/access-keys` † | 201 | A name, `read` and/or `write` scopes, an optional `expiresAt` and the current password; the only response that carries the key. |
  | GET | `/access-keys` | 200 | The caller's live access keys, newest first, without their secrets. |
  | DELETE | `/access-keys/{accessKeyId}` | 204 | Revokes one of the caller's access keys. |
  ```

- in `docs/reference/api.md`, replace the line:

  ```markdown
  | `auth.account.reAuthenticationRequired` | 403 | Deleting the account or registering a passkey needs a proof, or an access token under five minutes old |
  ```

  with:

  ```markdown
  | `auth.account.reAuthenticationRequired` | 403 | Deleting the account, registering a passkey or creating an access key needs a proof, or an access token under five minutes old |
  ```

- in `docs/reference/api.md`, after the line:

  ```markdown
  | `auth.passkey.registrationRejected` | 400 | The ceremony verified, but the store refused the credential |
  ```

  add:

  ```markdown
  | `auth.accessKey.notFound` | 404 | An access key that is not the caller's, or is already revoked or expired |
  | `auth.accessKey.limitReached` | 409 | Creating an access key while the account holds 25 live ones |
  | `auth.accessKey.invalid` | 401 | Answered by a host that accepts access keys, never by this API: a malformed, unknown, revoked or expired key, or one whose account may not sign in |
  ```

- in `docs/features/authentication.md`, before the line:

  ```markdown
  ### Signing in with Google, Microsoft or Apple
  ```

  add:

  ```markdown
  ### Access keys

  An access key stands for its owner where a token cannot be refreshed: at this project's MCP server,
  whose clients keep one credential for weeks. `POST /api/v1/auth/access-keys`
  creates one from a name, its scopes (`read`, `write` or both) and an optional expiry, and
  re-authenticates the caller as registering a passkey does. The response carries the key, `ak_` and
  43 characters, once: only its SHA-256 hash is stored. `GET /api/v1/auth/access-keys` lists the
  live keys with their first characters and when each was last used, to the minute;
  `DELETE /api/v1/auth/access-keys/{accessKeyId}` revokes one. An account holds at most 25 live keys;
  a password change leaves them alive, a lockout refuses them and deleting the account deletes them.

  This API never accepts an access key: its bearer scheme reads JWTs only. The MCP server validates
  the key through `IAuthenticateAccessKeyUseCase` and acts as its owner. A malformed, unknown,
  revoked or expired key, and one whose account may not sign in, all answer `auth.accessKey.invalid`
  ([ADR 0026](../decisions/0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md)).

  ```

- in `docs/features/authentication.md`, after the line:

  ```markdown
  - [0016 Browser refresh token in a cookie](../decisions/0016-browser-refresh-token-in-a-cookie.md)
  ```

  add:

  ```markdown
  - [0026 An access key stands for its owner where a token cannot be refreshed](../decisions/0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md)
  ```

<!--#endif-->
- in `.dockerignore`, after the line:

  ```text
  **/appsettings*.json
  ```

  add:

  ```text
  !Src/Presentation/AppTemplate.PresentationAppMcp/appsettings.json
  ```

- in `.env.example`, before the line:

  ```bash
  # Mailpit host ports, loopback only: the web UI is where the mails land.
  ```

  add:

  ```bash
  # The MCP server, loopback only.
  PRESENTATION_PRESENTATION_APP_MCP_HOST_PORT=8091

  ```

- in `docs/getting-started/first-run.md`, before the line:

  ```markdown
  | Mailpit, where mails land | <http://localhost:8025> |
  ```

  add:

  ```markdown
  | The MCP server | <http://localhost:8091/mcp> |
  ```

<!--#if (externalAuth)-->
- in `.env.example`, after the line:

  ```bash
  EXTERNAL_AUTH_ADMINISTRATOR_ROLE=Admin
  ```

  add:

  ```bash
  # Mcp section: the address MCP clients connect to, the audience of the tokens the provider issues for the MCP server.
  MCP_RESOURCE=http://localhost:8091/mcp
  ```

<!--#endif-->
- in `Directory.Packages.props`, before the line:

  ```xml
    <!-- One 1.18.x band. -->
  ```

  add:

  ```xml
    <ItemGroup Label="MCP server">
      <PackageVersion Include="ModelContextProtocol.AspNetCore" Version="2.3.0" />
    </ItemGroup>

  ```

- in `docker-compose.yml`, after the mailpit service's last lines:

  ```yaml
        - "127.0.0.1:${MAILPIT_SMTP_PORT:-1025}:1025"
      networks: [backend]

  ```

  add:

  ```yaml
    presentation-presentation-app-mcp:
      # The MCP server reads the hosts' settings with its own composition, and runs neither jobs nor migrations.
      extends:
        file: docker-compose.database.yml
        service: database-client
      image: app-template-presentation-app-mcp:dev
      build:
        context: .
        dockerfile: Src/Presentation/AppTemplate.PresentationAppMcp/Dockerfile
      restart: unless-stopped
      environment:
        <<: *host-environment
<!--#if (externalAuth)-->
        Mcp__Resource: ${MCP_RESOURCE:-http://localhost:8091/mcp}
<!--#endif-->
      ports:
        - "127.0.0.1:${PRESENTATION_PRESENTATION_APP_MCP_HOST_PORT:-8091}:8080"
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

<!--#if (agentFiles)-->
- in `AGENTS.md`, after the line:

  ```markdown
  the local tools and creates each context's initial migration, and the other tasks assume all four.
  ```

  add:

  ```markdown
  It first adds the MCP server to the solution with `dotnet run Tools/Tasks.cs presentation-scaffold`.
  ```

<!--#endif-->
- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
          ["Src/Infrastructure/AppTemplate.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
<!--#if (localAuth)-->
          ["Src/Presentation/AppTemplate.PresentationAppMcp"] = ["Composition", "Tools"],
<!--#endif-->
<!--#if (externalAuth)-->
          ["Src/Presentation/AppTemplate.PresentationAppMcp"] = ["Composition", "Configuration/Options", "Tools"],
<!--#endif-->
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the
  line:

  ```csharp
              ["Jobs", "Localization", "Observability", "Outbound", "Security"],
  ```

  add:

  ```csharp
          ["Src/Presentation/AppTemplate.PresentationAppMcp"] = ["Hosting", "Security", "Tools"],
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/FeatureFolderVocabularyTests.cs`, after
  the line:

  ```csharp
          ["Templates"] = [],
  ```

  add:

  ```csharp
          ["Tools"] = ["Tools"],
  ```

- in `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/OutboundHttpTests.cs`, after the
  lines:

  ```csharp
          string[] hosts =
          [
  ```

  add:

  ```csharp
              "AppTemplate.PresentationAppMcp",
  ```

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
  ## The presentation project

  `Src/Presentation/<Project>.<Name>` is a second host: an MCP server whose tools call the use cases
  in its own process, one use case each. It references the application projects and the modules it
  composes, like the API, and never the API itself (`NoHost_ReferencesAnotherHost`); under `Common/`
  and `Features/` it keeps its own caller, its authentication, its tools and the adapters a host
  supplies. [Presentation project](../features/presentation.md) describes it.

  ```

- in `docs/decisions/0001-adopt-the-generated-baseline.md`, before the lines:

  ```markdown

  ## Consequences
  ```

  add:

  ```markdown
  | An MCP server beside the API, in the shape of `dotnet new mcpserver --transport remote`, stateless on Streamable HTTP, each tool one use case called in process | An agent's client reaches the use cases as tools, acting for the caller its credential names | [Presentation project](../features/presentation.md) | — |
  ```

- in `docs/decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md`,
  after the line:

  ```markdown
    host, go to `Presentation.Core` while it stays free of ASP.NET Core; the rest each host keeps.
  ```

  add:

  ```markdown
  - **The presentation project of this repository** is an MCP server. It composes the use cases in
    process, one per tool, and answers each request in a scope of its own, whose caller is the one
    its credential names ([ADR 0027](0027-an-mcp-server-acts-for-the-caller-its-credential-names.md)).
  ```

- in `docs/decisions/README.md`, before the lines:

  ```markdown

  Add this project's own ADRs as rows at the end of the table, numbered from 1000.
  ```

  add:

  ```markdown
<!--#if (localAuth)-->
  | [0026](0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md) | An access key stands for its owner where a token cannot be refreshed | Authentication |
<!--#endif-->
  | [0027](0027-an-mcp-server-acts-for-the-caller-its-credential-names.md) | An MCP server acts for the caller its credential names | Architecture |
  ```

- in `docs/features/authentication.md`, before the line:

  ```markdown
  ## Roles and policies
  ```

  add:

  ```markdown
  ## The MCP server

  The MCP server beside the API authenticates its callers itself and acts for the owner its
  credential names: [presentation project](presentation.md#calling-it) describes the credential a
  client sends and which tools it reaches
  ([ADR 0027](../decisions/0027-an-mcp-server-acts-for-the-caller-its-credential-names.md)).

  ```

- in `docs/features/background-jobs.md`, after the lines:

  ```markdown
  Every pass calls an application use case, exactly as a controller would; a job adds a trigger, not
  logic.
  ```

  add:

  ```markdown
  The jobs run in the API alone: the presentation project composes the same use cases and runs none
  of them on a timer.
  ```

- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  Commit what it changes before anything else.
  ```

  add:

  ```markdown
  It first adds the MCP server, which comes written whole, and its tests to the solution
  ([Presentation project](../features/presentation.md#creating-the-project)).
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  It is a starting point. The modules and mechanisms it carries are meant to stay; the product's own
  ```

  add:

  ```markdown
  An MCP server beside it offers the same use cases to an agent's client, as tools.
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  To change a value, remove the part by hand or add it:
  ```

  add:

  ```markdown
  | `--presentation` | `mcp` | an MCP server in `Src/Presentation`, written in the shape of `dotnet new mcpserver`, whose tools call the use cases in process ([Presentation project](../features/presentation.md)) |
  ```

- in `docs/guides/adding-a-feature.md`, after the line:

  ```markdown
  for the account-deletion sweep it brings with it.
  ```

  add:

  ```markdown
  Each host composes itself, so the call goes in the presentation project's `Program.cs` too, and a first
  owner feature writes and registers that host's account directory beside the API's; the printed edits
  name both ([ADR 0022](../decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)).
  The feature brings no tool: the MCP server offers a use case only once its tool is declared by hand,
  as the [presentation project](../features/presentation.md#declaring-a-tool) shows.
  ```

<!--#if (todoLists || notes)-->
- in `docs/guides/removing-features.md`, after the line:

  ```markdown
  | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs` | the set, the schema constant, the `ApplyConfiguration` calls |
  ```

  add:

  ```markdown
  | the presentation project's `Program.cs` | the `Add<Features>()` line |
  | the presentation project's `Program.cs`, `Features/<Features>/Tools` and its tests, and `TestSupport/ToolCatalogue.cs` | the feature's `WithTools` line, its tools, their tests and their names |
  ```

<!--#endif-->
- in `docs/README.md`, before the lines:

  ```markdown

  ## Guides
  ```

  add:

  ```markdown
  | [Presentation project](features/presentation.md) | the MCP server beside the API, its tools, and who may call them |
  ```

<!--#if (externalAuth)-->
- in `docs/reference/configuration.md`, after the lines:

  ```markdown
  | `Roles:AdministratorRole` | string | `Admin` | The role the `Administrator` policy requires. |

  ```

  add:

  ```markdown
  The MCP server reads this section too, for the provider whose tokens it accepts; it validates them
  for its own audience, `Mcp:Resource`, never `Audience`.

  ### `Mcp`

  Read by the MCP server alone; the API never reads it.

  | Key | Type | Default | Notes |
  |---|---|---|---|
  | `Resource` | string | `""` | **Required.** The absolute address MCP clients connect to, ending in `/mcp`: the audience of the tokens the provider issues for the server, and the resource its metadata names. |

  ```

<!--#endif-->
- in `docs/reference/project-layout.md`, after the line:

  ```markdown
  `…Module.cs`) and the folders `Common/`, `Features/`, `Migrations/` and `Properties/`.
  ```

  add:

  ```markdown
  The MCP server keeps each feature's tools under `Features/<F>/Tools`, one class named
  `<F>Tools`, and what every tool shares under `Common/Tools`.
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```text
                                                        Controllers,Jobs}
  ```

  add:

  ```text
<!--#if (localAuth)-->
    <Project>.<Name>                      Common/{Hosting,Security,Tools}
                                          Features/<F>/{Composition,Tools}
<!--#endif-->
<!--#if (externalAuth)-->
    <Project>.<Name>                      Common/{Hosting,Security,Tools}
                                          Features/<F>/{Composition,Configuration/Options,Tools}
<!--#endif-->
  ```

- in `docs/reference/project-layout.md`, after the line:

  ```markdown
  | `…Composition`, a host-supplied `…Directory`, `…Provider` or `…Session` | `Composition/` |
  ```

  add:

  ```markdown
  | `…Tools`, the MCP server's tools of one feature | `Tools/` |
  ```
