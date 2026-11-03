# Web workspace

The pnpm workspace holding every web app under `apps/` and every internal package under `packages/`,
with one lockfile. pnpm is the version `package.json` pins in `packageManager`, which Corepack runs;
Node is the version in `.node-version`. Nothing in the .NET build needs either.

## Commands

From this folder:

```bash
corepack enable          # once per machine
pnpm install --frozen-lockfile
pnpm lint
pnpm test
pnpm build
pnpm api                 # regenerate packages/api-client from the API's OpenAPI document
```

`dotnet run Tools/Tasks.cs web-scaffold` at the project root creates each framework app that still
holds its scaffold.json, with its framework's official CLI, then installs and runs `pnpm api`;
`bootstrap` runs the same step.

## Adding an app or a package

At the project root, `@app-template` being this workspace's scope:

```bash
dotnet new cleanarch-webapp -n BackOffice --framework vite-react --scope app-template --audience admin
dotnet run Tools/Tasks.cs web-join BackOffice --web vite-react --web-audience admin
dotnet new cleanarch-webpackage -n tokens --scope app-template
dotnet run Tools/Tasks.cs web-join tokens
```

Pass the project's `--auth` to `cleanarch-webapp` when it is not the default. For an app, `web-join`
first makes the lines its `README.md` lists under "Joining a project generated without one", its
`include:` line in `docker-compose.yml` and its port in `.env.example` among them, leaving those in
place already; then it records the app or the package in `cleanarch.json`, with the values the app
was given.

## Joining a project generated without one

A project generated with `--web none` has no workspace. Its first app comes with the workspace, at
the project root:

```bash
dotnet new cleanarch-webapp -n PlaceholderSite --framework <vue|vite-react|vite-vue|vite-svelte|react-router|angular|placeholder> --project-name <Project> --scope app-template --workspace
dotnet run Tools/Tasks.cs web-join PlaceholderSite --web <framework>
```

Pass the project's `--auth`, `--ci` and `--agent-files` too when they are not the defaults.

`--workspace` writes this folder, the shared packages, the API client, the web apps' end-to-end
tests, `docs/features/web-frontend.md`, `docs/decisions/0019-one-generated-api-client.md`, the
server-rendered app's decision under `--rendering ssr` and, unless `--ci false`,
`.github/workflows/web.yml`. The API side is in every project already. The lines `--web` writes in
files the item template cannot edit are listed below, then those of the app's own `README.md`.
`web-join`, the first time it joins an app to a project whose `cleanarch.json` records no
workspace, makes all of them, leaves those in place already, or makes none when one of the lines an
edit is placed by is not found exactly once; then it records the workspace and the app. Run
`dotnet run Tools/Tasks.cs web-scaffold` next for a framework app, or `pnpm install` here for a
placeholder, and commit the workspace with its `pnpm-lock.yaml`.

- in `docker-compose.yml`, before the line:

  ```yaml
  services:
  ```

  add:

  ```yaml
  # One line per web app: its compose.yml declares the service, built from Src/Web.
  include:

  ```

- in `README.md`, after the line:

  ```markdown
  [project layout](docs/reference/project-layout.md) says where a file goes.
  ```

  add:

  ```markdown
  The web apps live in the pnpm workspace `Src/Web`.
  ```

- in `README.md`, after the line:

  ```markdown
  You need the .NET SDK `global.json` pins and Docker with Compose v2.
  ```

  add:

  ```markdown
  The web workspace also needs Node.js and pnpm (`corepack enable`).
  ```

- in `README.md`, after the line:

  ```markdown
  | Mailpit | <http://localhost:8025> |
  ```

  add:

  ```markdown
<!--#if (webFramework)-->
  | Web app | <http://localhost:5173> |
<!--#endif-->
<!--#if (webPlaceholder)-->
  | Web app | <http://localhost:8081> |
<!--#endif-->
  ```

- in `docs/getting-started/overview.md`, after the line:

  ```markdown
  use cases, infrastructure modules behind ports, and a thin presentation layer.
  ```

  add:

  ```markdown
  A pnpm workspace beside it holds its web apps.
  ```

- in `docs/getting-started/overview.md`, after the line:

  ```markdown
  | Tools | `Tools/Tasks.cs`, the task launcher; `Tools/NewFeature.cs` and `Tools/RemoveFeature.cs`, the feature generator and its undo; `Tools/NewUseCase.cs`, one more use case; `Tools/NewSetting.cs` and `Tools/NewEvent.cs`, a validated setting and a domain event |
  ```

  add:

  ```markdown
  | Web | `Src/Web`, a pnpm workspace with one app, a generated API client and shared packages |
  ```

- in `.env.example`, after the line:

  ```bash
  API_HOST_PORT=8080
  ```

  add:

  ```bash

  # Web apps, loopback only: one port per app, named after its folder.
  ```

- in `.dockerignore`, after the lines:

  ```text
  **/charts

  ```

  add:

  ```text
  # ---------------------------------------------------------------------------
  # The web workspace: each web app builds from Src/Web with its own image.
  # ---------------------------------------------------------------------------
  Src/Web/

  ```

- in `.github/dependabot.yml`, before the lines:

  ```yaml
      schedule:
        interval: weekly
        day: monday
        time: "06:00"
        timezone: Europe/Paris
      open-pull-requests-limit: 5
      labels:
        - dependencies
        - docker
  ```

  add:

  ```yaml
        # Every web app's image, nginx or Node, by directory glob.
        - /Src/Web/apps/*
  ```

- in `.github/dependabot.yml`, before the line:

  ```yaml
    # docker-compose.yml's images, version updates only.
  ```

  add:

  ```yaml
    # The web workspace: the npm ecosystem reads Src/Web/pnpm-lock.yaml for every app and package.
    - package-ecosystem: npm
      directory: /Src/Web
      schedule:
        interval: weekly
        day: monday
        time: "06:00"
        timezone: Europe/Paris
      open-pull-requests-limit: 5
      labels:
        - dependencies
        - javascript
      commit-message:
        prefix: "build(deps)"
        prefix-development: "build(deps-dev)"
      groups:
        web-lint:
          applies-to: version-updates
          patterns:
            - eslint
            - "@eslint/*"
            - typescript-eslint
            - globals
        web-api-client:
          applies-to: version-updates
          patterns:
            - openapi-fetch
            - openapi-typescript
        web-tooling:
          applies-to: version-updates
          patterns:
            - typescript
            - vitest

  ```

<!--#if (ci)-->
- in `.github/workflows/codeql.yml`, after the lines:

  ```yaml
            - language: csharp
              build-mode: none
  ```

  add:

  ```yaml
            - language: javascript-typescript
              build-mode: none
  ```

<!--#endif-->
<!--#if (agentFiles)-->
- in `AGENTS.md`, after the line:

  ```markdown
  holding its own `AGENTS.md` adds what that folder needs: read it before changing a file there.
  ```

  add:

  ```markdown
  `Src/Web/AGENTS.md` is the one for the web workspace.
  ```

<!--#endif-->
<!--#if (agentFiles && accounts)-->
- in `AGENTS.md`, before the line:

  ```markdown
  | After adding a feature's setting | `dotnet run Tools/Tasks.cs new-setting <Features> <Section> --keys "<Key>:<type>(<min>..<max>)=<default>"` | the feature registered in a host |
  ```

  add:

  ```markdown
  | After changing a route or a contract, with the web workspace | `pnpm api` in `Src/Web`, after `openapi` | the .NET SDK, Node.js and pnpm |
  ```

<!--#endif-->
<!--#if (agentFiles && publicApi)-->
- in `AGENTS.md`, before the line:

  ```markdown
  | Every task and option | `dotnet run Tools/Tasks.cs help [<task>]`; `-m:<N>` caps build and test parallelism | nothing |
  ```

  add:

  ```markdown
  | After changing a route or a contract, with the web workspace | `pnpm api` in `Src/Web`, after `openapi` | the .NET SDK, Node.js and pnpm |
  ```

<!--#endif-->
- in `SECURITY.md`, replace the line:

  ```markdown
  | Every dependency pinned and audited; any known advisory fails the build | [Build and CI › Supply chain](docs/guides/build-and-ci.md#supply-chain) |
  ```

  with:

  ```markdown
  | Every dependency pinned and audited, NuGet by `NuGetAudit` and the web packages by `pnpm audit`; any known advisory fails the build | [Build and CI › Supply chain](docs/guides/build-and-ci.md#supply-chain) |
  ```

- in `docs/README.md`, after the line:

  ```markdown
  | [Observability](features/observability.md) | logs, metrics, traces and health checks |
  ```

  add:

  ```markdown
  | [Web front end](features/web-frontend.md) | the web apps beside the API, and signing users in from them |
  ```

- in `docs/architecture/README.md`, replace the line:

  ```markdown
  | A client contracts package | a client generates its types from the API's OpenAPI document | [HTTP API](http-api.md#versioning-and-the-openapi-document) |
  ```

  with:

  ```markdown
  | A shared contracts assembly | the web apps use a client generated from the API's OpenAPI document | [ADR 0019](../decisions/0019-one-generated-api-client.md) |
  ```

- in `docs/architecture/http-api.md`, after the line:

  ```markdown
  included when none declares a `400`; `OpenApiDocumentTests` holds both.
  ```

  add:

  ```markdown
  The web apps' API client is generated from the same document, written at build time
  ([ADR 0019](../decisions/0019-one-generated-api-client.md)).
  ```

- in `docs/architecture/http-api.md`, after the line:

  ```markdown
  - [0001. Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): MVC controllers, `Asp.Versioning`, the framework's OpenAPI generator.
  ```

  add:

  ```markdown
  - [0019. One generated API client](../decisions/0019-one-generated-api-client.md)
  ```

- in `docs/decisions/0001-adopt-the-generated-baseline.md`, after the line:

  ```markdown
  | The API image on alpine, running unprivileged | The healthcheck needs `wget`, which the chiseled images lack | [The image](../guides/deployment.md#the-image) | — |
  ```

  add:

  ```markdown
  | One pnpm workspace under `Src/Web`, Node and pnpm pinned | Apps and packages share one lockfile; nothing in the .NET build needs Node | [Web front end](../features/web-frontend.md) | — |
  ```

<!--#if (localAuth)-->
- in `docs/decisions/0016-browser-refresh-token-in-a-cookie.md`, before the line:

  ```markdown
  ## Consequences
  ```

  add:

  ```markdown
  In this project's single-page web apps, the client package's `createBrowserSession` keeps the
  access token in a variable only, never in storage, and calls `refresh()` once at start so a reload
  costs one request. Concurrent refreshes share one request, since two would present one token twice
  and end the session, and only a refused refresh signs the page out. A server-rendered app keeps both
  tokens on its server instead, behind a session cookie of its own.
<!--#if (webServerRendering)-->
  [ADR 0024](0024-a-server-rendered-web-app-runs-on-its-own-server.md) says why.
<!--#endif-->

  ```

<!--#endif-->
<!--#if (localAuth)-->
- in `docs/decisions/0016-browser-refresh-token-in-a-cookie.md`, after the line:

  ```markdown
  - `BrowserSessionTests` and `RefreshTokenCookieTests` hold the cookie, the header and the refusals.
  ```

  add:

  ```markdown
    `Src/Web/packages/api-client/test/session.test.ts` holds the browser side.
  ```

<!--#endif-->
<!--#if (hasClient)-->
- in `docs/decisions/0023-a-dotnet-client-generated-by-kiota.md`, after the line:

  ```markdown
  and problem answer, `code` and field errors included.
  ```

  add:

  ```markdown
  This is [ADR 0019](0019-one-generated-api-client.md) for .NET: one generated client and one thin
  wrapper, the same for every app.
  ```

<!--#endif-->
- in `docs/decisions/README.md`, before the line:

  ```markdown
  | [0020](0020-one-risk-one-type-of-test.md) | One risk, one type of test | Testing |
  ```

  add:

  ```markdown
  | [0019](0019-one-generated-api-client.md) | One generated API client | Web front end |
  ```

<!--#if (webServerRendering)-->
<!--#if (hasClient)-->
- in `docs/decisions/README.md`, after the line:

  ```markdown
  | [0023](0023-a-dotnet-client-generated-by-kiota.md) | A .NET client generated by Kiota | Clients |
  ```

  add:

  ```markdown
  | [0024](0024-a-server-rendered-web-app-runs-on-its-own-server.md) | A server-rendered web app runs on its own server | Web front end |
  ```

<!--#endif-->
<!--#if (!hasClient)-->
- in `docs/decisions/README.md`, after the line:

  ```markdown
  | [0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) | A presentation project reaches the core through the API or the application layer | Architecture |
  ```

  add:

  ```markdown
  | [0024](0024-a-server-rendered-web-app-runs-on-its-own-server.md) | A server-rendered web app runs on its own server | Web front end |
  ```

<!--#endif-->
<!--#endif-->
<!--#if (localAuth)-->
- in `docs/features/accounts.md`, after the lines:

  ```markdown
  Route names are free: the settings take any absolute page URL.

  ```

  add:

  ```markdown
  For a web app of this project, the steps are in
  [Web front end](web-frontend.md#the-pages-the-mailed-links-open).

  ```

<!--#endif-->
<!--#if (localAuth)-->
- in `docs/features/authentication.md`, after the lines:

  ```markdown
  or not, and a client that never sends it works as above ([ADR 0016](../decisions/0016-browser-refresh-token-in-a-cookie.md)).

  ```

  add:

  ```markdown
  The single-page web apps of this project use this mode through the `api-client` package: the access
  token stays in a variable, the cookie carries the refresh token, and one refresh runs at a time. A
  server-rendered app uses neither: its server signs in in body mode, keeps both tokens, and gives the
  browser a session cookie of its own.
<!--#if (webServerRendering)-->
  [ADR 0024](../decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md) says why.
<!--#endif-->
  How an app signs in is in [Web front end](web-frontend.md#signing-in).

  ```

<!--#endif-->
<!--#if (externalAuth)-->
- in `docs/features/authentication.md`, after the line:

  ```markdown
  Authorization Code flow with PKCE, and sends its access token as `Authorization: Bearer`.
  ```

  add:

  ```markdown
  A server-rendered web app runs that flow on its server, keeps the tokens there, and gives the
  browser a session cookie of its own.
<!--#if (webServerRendering)-->
  [ADR 0024](../decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md) says why.
<!--#endif-->
  How an app signs in is in [Web front end](web-frontend.md#signing-in).
  ```

<!--#endif-->
<!--#if (ci)-->
- in `docs/getting-started/existing-repository.md`, before the line:

  ```markdown
     A workflow runs from the git root, so prefix every path its steps name (the solution,
  ```

  add:

  ```markdown
     Move `web.yml` the same way.

  ```

<!--#endif-->
- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  | Docker with Compose v2 | any recent | the local stack and the integration tests |
  ```

  add:

  ```markdown
  | Node.js | the version in `Src/Web/.node-version` | the web workspace |
  | pnpm | the version `Src/Web/package.json` pins, run by Corepack | the web workspace |
  ```

- in `docs/getting-started/first-run.md`, after the lines:

  ```markdown
  says, and run `git config --global core.longpaths true`.

  ```

  add:

  ```markdown
  Run `corepack enable` once per machine so the pinned pnpm is the one that runs.

  ```

- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  the project's from the commit on ([migrations](../guides/migrations.md#the-initial-migration)).
  ```

  add:

  ```markdown
<!--#if (webFramework)-->
  It then writes the .NET client where the web app calls the API through it, and creates the app as the
  scaffold file in its folder describes, with the generator that file names.
<!--#endif-->
  It last installs the web workspace and generates its TypeScript API client, which needs Node.js and
  pnpm.
  ```

- in `docs/getting-started/first-run.md`, before the lines:

  ```markdown

  Every port binds to `127.0.0.1`. The API speaks plain HTTP on 8080: TLS terminates upstream, as
  ```

  add:

  ```markdown
<!--#if (webFramework)-->
  | Web app | <http://localhost:5173> |
<!--#endif-->
<!--#if (webPlaceholder)-->
  | Web app | <http://localhost:8081> |
<!--#endif-->
  ```

- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  [deployment](../guides/deployment.md#behind-a-proxy) describes.
  ```

  add:

  ```markdown
  The web app relays `/api` to the API, so a browser talks to one origin.
  ```

- in `docs/getting-started/first-run.md`, replace the lines:

  ```markdown
  To add a web app or a presentation project later, run `dnx CleanArch.Create -- web-app` or
  `dnx CleanArch.Create -- presentation` at the project's root.
  ```

  with:

  ```markdown
  To add a web app, a shared web package or a presentation project later, run
  `dnx CleanArch.Create -- web-app`, `dnx CleanArch.Create -- web-package` or
  `dnx CleanArch.Create -- presentation` at the project's root.
  ```

- in `docs/guides/adding-a-feature.md`, after the lines:

  ```markdown
  The migration is empty unless the rule needs a column or an index.

  ```

  add:

  ```markdown
  ## Web pages for it

  `new-feature` and `new-use-case` run `pnpm api` in `Src/Web` once the API serves the routes; run it
  yourself after a route you change by hand. The generated client then holds their types, and an app
  calls them through it. [The web front end](../features/web-frontend.md#calling-the-api)
  shows the call.
  ```

- in `docs/guides/build-and-ci.md`, replace the line:

  ```markdown
  | `bootstrap` | format, write the OpenAPI reference, restore the local tools, then `migration-initial`; once, after generation |
  ```

  with:

  ```markdown
  | `bootstrap` | format, write the OpenAPI reference, restore the local tools, `migration-initial`, then `web-scaffold`; once, after generation |
  ```

- in `docs/guides/build-and-ci.md`, replace the line:

  ```markdown
  | `web-scaffold` | create the framework app a first `cleanarch-webapp --workspace` adds |
  ```

  with:

  ```markdown
  | `web-scaffold` | create each web app still holding its scaffold.json |
  ```

- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  change without its regenerated client fails.
  ```

  add:

  ```markdown
  With a web workspace it then runs `pnpm install --frozen-lockfile` and compares
  `Src/Web/packages/api-client/src/generated/schema.ts` with what `openapi-typescript` writes from the
  OpenAPI reference, so a contract change without `pnpm api` fails and the message names that command;
  that step needs Node.js and pnpm.
  ```

<!--#if (ci)-->
- in `docs/guides/build-and-ci.md`, replace the line:

  ```markdown
  | `.github/workflows/codeql.yml` | push and pull request to `main`, and weekly | CodeQL code scanning of the C# sources, read without a build; alerts land in the repository's Security tab |
  ```

  with:

  ```markdown
  | `.github/workflows/codeql.yml` | push and pull request to `main`, and weekly | CodeQL code scanning of the C# sources, read without a build, and of the TypeScript in `Src/Web`; alerts land in the repository's Security tab |
  ```

<!--#endif-->
<!--#if (ci)-->
- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  | `.github/workflows/codeql.yml` | push and pull request to `main`, and weekly | CodeQL code scanning of the C# sources, read without a build, and of the TypeScript in `Src/Web`; alerts land in the repository's Security tab |
  ```

  add:

  ```markdown
  | `.github/workflows/web.yml` | push and pull request to `main` | in `Src/Web`: frozen install, lint, test, build, and a failure when the API client differs from the API's document; one image per app |
  ```

<!--#endif-->
- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  | Container images and actions | their digest or commit SHA, beside the tag |
  ```

  add:

  ```markdown
  | Node, pnpm and the web packages | `Src/Web/.node-version`, `Src/Web/package.json`, `Src/Web/pnpm-lock.yaml` |
  ```

- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  zero whatever it finds, so `verify` and CI read what it prints.
  ```

  add:

  ```markdown

  `pnpm audit` holds the web packages to the same rule: `verify` and the web workflow run it, and any
  advisory fails, whatever its severity. The answer is the same too: raise the dependency, or pin a
  patched release under `overrides` in `Src/Web/pnpm-workspace.yaml` with a comment saying when it can go;
  a package that cannot take the pinned release as it is gets a pnpm patch in `Src/Web/patches/`.
  ```

- in `docs/guides/deployment.md`, after the lines:

  ```markdown
  needs its `COPY` line there too; the build passes locally without it, and only the image fails.

  ```

  add:

  ```markdown
  Each web app has its own image, built from `Src/Web`: nginx serves a single-page app, and a
  server-rendered app runs on its own Node server, as one instance; [the web front end](../features/web-frontend.md#images-and-headers)
  describes them.

  ```

- in `docs/guides/testing.md`, after the line:

  ```markdown
  variable set before the task moves the stack and the suite together.
  ```

  add:

  ```markdown
  It finds every web app by its `WEB_<APP>_HOST_PORT` line.
  ```

- in `docs/guides/testing.md`, before the lines:

  ```markdown

  The project sets `IsTestingPlatformApplication` to `false`, so `dotnet test` over the solution, and
  ```

  add:

  ```markdown
  | `WebAppTests` | each web app serves its page and relays `/api` to the API |
  ```

<!--#if (externalAuth)-->
- in `docs/reference/configuration.md`, before the line:

  ```markdown
  ## Mail
  ```

  add:

  ```markdown
  ## A server-rendered web app

  ### Its environment

  Read by the Node server of a web app rendered on the server (`--web-rendering ssr`, or
  `cleanarch-webapp --rendering ssr`) alone, as the client it signs people in with at the provider,
  from its environment, which its `compose.yml` sets from `.env`; the API never reads it. The server
  refuses to start until each required one is set, and names those that are not; the framework's
  build, which loads the server, needs none of them.

  | Variable | Default | Notes |
  |---|---|---|
  | `EXTERNAL_AUTH_AUTHORITY` | — | **Required.** The provider's `https` issuer URL, the one `Authentication:External:Authority` names. Its discovery document is read at the first sign-in, not at start. |
  | `EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID` | — | **Required.** The client the provider registered for the app, allowed the authorization code flow with PKCE. |
  | `EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET` | — | Its secret, for a confidential client; a public client sends none and relies on PKCE. |
  | `EXTERNAL_AUTH_INTERACTIVE_SCOPE` | `openid profile email offline_access` | Must include `openid`. `offline_access` asks for the refresh token that keeps the session past the first access token; the API's own scope goes here when the provider issues its audience that way. |
  | `PUBLIC_ORIGIN` | — | **Required.** The origin browsers reach the app at, such as `https://app.example.com`: `https`, or `http` on `localhost`, since the session cookie is `Secure`. The redirect URI is `<origin>/session/callback`. Compose sets it from `WEB_<NAME>_ORIGIN`, `http://localhost` on the app's port by default. |

  ```

<!--#endif-->
- in `docs/reference/project-layout.md`, before the line:

  ```text
  Tests/                                  one test project per source project, mirroring its folders,
  ```

  add:

  ```text
  Src/Web/                                the pnpm workspace: apps/ and packages/
  ```
