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
pnpm --filter "*browser-smoke" smoke   # drive every app in Chromium; needs the stack, as below
```

`bootstrap` creates each framework app that still holds its scaffold.json, with its framework's
official CLI, then installs and runs `pnpm api`; `dotnet cleanarch add web-app --name <App>` at the
project root does the same for one app.

## Lint

`packages/eslint-config` holds the lint rules of the workspace's own packages, those
`dotnet new cleanarch-webpackage` adds among them, and `pnpm lint` runs them; a web app keeps the
lint its framework's CLI gave it. The rules are typescript-eslint's type-checked ones and one rule of
`eslint-plugin-sonarjs`, `sonarjs/cognitive-complexity`, which fails on a function whose cognitive
complexity is over 15. It is the measure of Sonar's S3776 for C#, with the same ceiling: each break
in the linear flow costs, more when nested, and length costs nothing, so a function that nests deeply
is split while a long flat one stays whole. A finding is fixed in the code, never silenced.

## The browser smoke

`packages/browser-smoke` drives every app `cleanarch.json` records in Chromium, against the running
Compose stack: its page loads, the browser reports no Content-Security-Policy violation, console
error or uncaught exception, and a call through its `/api/` relay reaches the API. It finds each
app's port from `WEB_<APP>_HOST_PORT` in the environment, then in `.env`, then in `.env.example`
(`SMOKE_ENV_FILE` names another file), so a new app needs no edit. `dotnet cleanarch end-to-end`
runs it after the end-to-end suite. `pnpm test` runs only the unit tests of how the apps are found.

Chromium comes apart from `pnpm install`; install it once per machine, adding `--with-deps` on a
Linux that lacks the system libraries:

```bash
pnpm --filter "*browser-smoke" exec playwright install --only-shell chromium
```

## Adding an app or a package

At the project root, `@app-template` being this workspace's scope:

```bash
dotnet cleanarch add web-app --name BackOffice --framework vite-react --audience admin
dotnet cleanarch add web-package --name tokens
```

Each runs `dotnet new cleanarch-webapp` or `dotnet new cleanarch-webpackage` with the options
`cleanarch.json` records. For an app, it first makes the lines its `README.md` lists under "Joining a project generated without one", its
`include:` line in `docker-compose.yml` and its port in `.env.example` among them, leaving those in
place already; then it records the app or the package in `cleanarch.json`, with the values the app
was given, and creates a framework app with its framework's official CLI. After a `dotnet new` of
its own, the same command with the item's name completes it.

## Joining a project generated without one

A project generated with `--web none` has no workspace. Its first app comes with the workspace, at
the project root:

```bash
dotnet new cleanarch-webapp -n PlaceholderSite --framework <vue|vite-react|vite-vue|vite-svelte|react-router|angular|placeholder> --project-name <Project> --scope app-template --workspace
dotnet cleanarch add web-app --name PlaceholderSite --framework <framework>
```

Pass the project's `--auth`, `--ci` and `--agent-files` too when they are not the defaults.

`--workspace` writes this folder, the shared packages, the browser smoke among them, the API client,
the web apps' end-to-end tests, `docs/features/web-frontend.md`,
`docs/decisions/0019-one-generated-api-client.md`, `docs/decisions/0037-a-web-app-is-deployed-apart-behind-a-relay-to-the-api.md`, the server-rendered app's decision under
`--rendering ssr` and, unless `--ci false`, `.github/workflows/web.yml`. The API side is in every project already. The lines `--web` writes in
files the item template cannot edit are listed below, then those of the app's own `README.md`.
`dotnet cleanarch add web-app`, the first time it joins an app to a project whose `cleanarch.json`
records no workspace, makes all of them, leaves those in place already, or makes none when one of
the lines an edit is placed by is not found exactly once; then it records the workspace and the app,
and creates a framework app. Run `pnpm install` here for a placeholder, and commit the workspace with
its `pnpm-lock.yaml`.

- in `docker-compose.yml`, before the line:

  ```yaml
  services:
  ```

  add:

  ```yaml
  # One line per web app: its compose.yml declares the service, built from src/Web.
  include:

  ```

- in `README.md`, after the line:

  ```markdown
  [project layout](docs/reference/project-layout.md) says where a file goes.
  ```

  add:

  ```markdown
  The web apps live in the pnpm workspace `src/Web`.
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
  | Tools | `dotnet cleanarch`, pinned in `.config/dotnet-tools.json`: the build, the gates, the feature generator and its undo, one more use case, a validated setting, a domain event, and what a newer version of the package changes; `tools/` holds the commands you add or eject |
  ```

  add:

  ```markdown
  | Web | `src/Web`, a pnpm workspace with one app, a generated API client and shared packages |
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
  # The web workspace: each web app builds from src/Web with its own image.
  # ---------------------------------------------------------------------------
  src/Web/

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
        - /src/Web/apps/*
  ```

- in `.github/dependabot.yml`, before the line:

  ```yaml
    # docker-compose.yml's images, version updates only.
  ```

  add:

  ```yaml
    # The web workspace: the npm ecosystem reads src/Web/pnpm-lock.yaml for every app and package.
    - package-ecosystem: npm
      directory: /src/Web
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
            - "@playwright/*"

  ```

<!--#if (ci)-->
- in `.github/workflows/ci.yml`, before the line:

  ```yaml
        - name: Start the stack, run the suite, remove the stack
  ```

  add:

  ```yaml
        # The browser smoke that closes the task drives every web app in Chromium: the workspace is installed for it, and the browser is cached by the Playwright release the lockfile resolves.
        - name: Set up pnpm (from package.json)
          uses: pnpm/action-setup@ea17c68df8912ef543352723c149a84f56e3d413 # v6.1.0
          with:
            package_json_file: src/Web/package.json
            cache: true
            cache_dependency_path: src/Web/pnpm-lock.yaml

        - name: Set up Node.js (from .node-version)
          uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
          with:
            node-version-file: src/Web/.node-version

        - name: Install the web workspace (frozen lockfile)
          working-directory: src/Web
          run: pnpm install --frozen-lockfile

        - name: Find the Playwright release
          id: playwright
          working-directory: src/Web
          run: echo "version=$(pnpm --filter '*browser-smoke' exec playwright --version | awk '{print $2}')" >> "$GITHUB_OUTPUT"

        - name: Cache Chromium
          uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0
          with:
            path: ~/.cache/ms-playwright
            key: playwright-${{ runner.os }}-${{ steps.playwright.outputs.version }}

        # Runs on a cache hit too, for the system libraries, and downloads nothing the cache holds.
        - name: Install Chromium
          working-directory: src/Web
          run: pnpm --filter '*browser-smoke' exec playwright install --only-shell --with-deps chromium

  ```

<!--#endif-->
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
  `src/Web/AGENTS.md` is the one for the web workspace.
  ```

<!--#endif-->
<!--#if (agentFiles && accounts)-->
- in `AGENTS.md`, before the line:

  ```markdown
  | After adding a field to a feature `add feature --fields` generated | `dotnet cleanarch add field <Features> "<Name>:<type>=<default>"` | the feature's manifest, which records its fields |
  ```

  add:

  ```markdown
  | After changing a route or a contract, with the web workspace | `dotnet cleanarch openapi`, which also runs `pnpm api <reference>` | the .NET SDK, Node.js and pnpm |
  ```

<!--#endif-->
<!--#if (agentFiles && publicApi)-->
- in `AGENTS.md`, before the line:

  ```markdown
  | Every command and option | `dotnet cleanarch help`, `dotnet cleanarch <command> --help`; `-m:<N>` caps build and test parallelism | nothing |
  ```

  add:

  ```markdown
  | After changing a route or a contract, with the web workspace | `dotnet cleanarch openapi`, which also runs `pnpm api <reference>` | the .NET SDK, Node.js and pnpm |
  ```

<!--#endif-->
<!--#if (agentFiles)-->
- in `AGENTS.md`, replace the line:

  ```markdown
  | Before calling a change done | `dotnet cleanarch verify`, then `dotnet cleanarch coverage` | Docker, the tools `dotnet tool restore` restores |
  ```

  with:

  ```markdown
  | Before calling a change done | `dotnet cleanarch verify`, then `dotnet cleanarch coverage` | Docker, the tools `dotnet tool restore` restores, Node.js and pnpm |
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
  | One pnpm workspace under `src/Web`, Node and pnpm pinned | Apps and packages share one lockfile; nothing in the .NET build needs Node | [Web front end](../features/web-frontend.md) | — |
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
    `src/Web/packages/api-client/test/session.test.ts` holds the browser side.
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
- in `docs/decisions/0020-one-risk-one-type-of-test.md`, before the line:

  ```markdown
  - **Coverage is a tripwire.** The floor in `coverage.minimum` catches an area shipped with no test.
  ```

  add:

  ```markdown
  - **What the browser blocks or breaks in a served web app is held by a browser smoke**: one Playwright
    test per app, in Chromium alone, run by `end-to-end` against the stack. No lower type sees a
    Content-Security-Policy violation, a console error or a call through the relay from inside a page,
    and a request that reads headers and bytes does not run what the page loads.
  <!--#if (externalAuth)-->
    Against a provider under `.invalid`, which never resolves, the smoke stands in for its discovery
    document and expects an app that signs in from the page to read it there; it exempts no violation
    and no error.
  <!--#endif-->
  ```

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
  | Node.js | the version in `src/Web/.node-version` | the web workspace |
  | pnpm | the version `src/Web/package.json` pins, run by Corepack | the web workspace |
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
  To add a web app or a presentation project later, run `dotnet cleanarch add web-app` or
  `dotnet cleanarch add presentation` at the project's root.
  ```

  with:

  ```markdown
  To add a web app, a shared web package or a presentation project later, run
  `dotnet cleanarch add web-app`, `dotnet cleanarch add web-package` or
  `dotnet cleanarch add presentation` at the project's root.
  ```

- in `docs/getting-started/first-run.md`, after the line:

  ```markdown
  whole stack of its own on the same ports, so stop yours first with `docker compose down`.
  ```

  add:

  ```markdown
  It then drives each web app in Chromium, which [testing](../guides/testing.md#the-browser-smoke)
  shows how to install.
  ```

- in `docs/guides/adding-a-feature.md`, after the line:

  ```markdown
  The migration is empty unless the rule needs a column or an index.
  ```

  add:

  ```markdown

  ## Web pages for it

  `add feature` and `add use-case` run `openapi`, which runs `pnpm api <reference>`, once the API serves the routes; run it
  yourself after a route you change by hand. The generated client then holds their types, and an app
  calls them through it. [The web front end](../features/web-frontend.md#calling-the-api)
  shows the call.
  ```

- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  change without its regenerated client fails.
  ```

  add:

  ```markdown
  With a web workspace it then runs `pnpm install --frozen-lockfile` and compares
  `src/Web/packages/api-client/src/generated/schema.ts` with what `openapi-typescript` writes from the
  OpenAPI reference, so a contract change without `pnpm api` fails and the message names that command,
  then `pnpm audit`, and, once `cleanarch.json` records the workspace, `pnpm lint`, `pnpm test` and
  `pnpm build` as the web workflow does. Those steps need Node.js and pnpm: without pnpm, `verify`
  stops before them and says how to install it, and no option skips them.
  ```

<!--#if (ci)-->
- in `docs/guides/build-and-ci.md`, replace the line:

  ```markdown
  | `.github/workflows/codeql.yml` | push and pull request to `main` or `develop`, and weekly | CodeQL code scanning of the C# sources, read without a build; alerts land in the repository's Security tab |
  ```

  with:

  ```markdown
  | `.github/workflows/codeql.yml` | push and pull request to `main` or `develop`, and weekly | CodeQL code scanning of the C# sources, read without a build, and of the TypeScript in `src/Web`; alerts land in the repository's Security tab |
  ```

<!--#endif-->
<!--#if (ci)-->
- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  | `.github/workflows/codeql.yml` | push and pull request to `main` or `develop`, and weekly | CodeQL code scanning of the C# sources, read without a build, and of the TypeScript in `src/Web`; alerts land in the repository's Security tab |
  ```

  add:

  ```markdown
  | `.github/workflows/web.yml` | push to `main`, pull request to `main` or `develop`, and a manual run | in `src/Web`: frozen install, lint, test, build, and a failure when the API client differs from the API's document; one image per app |
  ```

<!--#endif-->
<!--#if (ci)-->
- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  than `main` and `develop` cancels the older one.
  ```

  add:

  ```markdown
  A newer run of `web.yml` cancels the older one on every branch but `main`.
  ```

<!--#endif-->
- in `docs/guides/removing-features.md`, after the line:

  ```markdown
  [below](#migrations) says, and rewrites the OpenAPI reference and what is written from it.
  ```

  add:

  ```markdown
  With a web workspace, that rewrite includes `pnpm api <reference>` in `src/Web`, which needs Node.js and pnpm.
  ```

- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  | Container images and actions | their digest or commit SHA, beside the tag |
  ```

  add:

  ```markdown
  | Node, pnpm and the web packages | `src/Web/.node-version`, `src/Web/package.json`, `src/Web/pnpm-lock.yaml` |
  ```

- in `docs/guides/build-and-ci.md`, after the line:

  ```markdown
  zero whatever it finds, so `verify` and CI read what it prints.
  ```

  add:

  ```markdown

  `pnpm audit` holds the web packages to the same rule: `verify` and the web workflow run it, and any
  advisory fails, whatever its severity. The answer is the same too: raise the dependency, or pin a
  patched release under `overrides` in `src/Web/pnpm-workspace.yaml` with a comment saying when it can go;
  a package that cannot take the pinned release as it is gets a pnpm patch in `src/Web/patches/`.
  ```

- in `docs/guides/deployment.md`, after the lines:

  ```markdown
  needs its `COPY` line there too; the build passes locally without it, and only the image fails.

  ```

  add:

  ```markdown
  Each web app has its own image, built from `src/Web`: nginx serves a single-page app, and a
  server-rendered app runs on its own Node server, as one instance; [the web front end](../features/web-frontend.md#images-and-headers)
  describes them.

  ```

- in `docs/guides/testing.md`, after the line:

  ```markdown
  | Load smoke | nothing comes apart under concurrency | a gate; it never fails a build | `tests/Load/smoke.js`, a k6 script against a running stack |
  ```

  add:

  ```markdown
  | Browser smoke | what a browser blocks or breaks in each served web app: a Content-Security-Policy violation, a console error, an uncaught exception, a relay call that fails | what a lower type can prove; a flow or a screen's content | `src/Web/packages/browser-smoke`, Playwright in Chromium against the Compose stack |
  ```

- in `docs/guides/testing.md`, after the line:

  ```markdown
  apart from both.
  ```

  add:

  ```markdown
  The browser smoke needs the stack and Chromium, and runs with the end-to-end suite.
  ```

- in `docs/guides/testing.md`, after the line:

  ```markdown
  variable set before the command moves the stack and the suite together.
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

- in `docs/guides/testing.md`, after the line:

  ```markdown
  | The stack as shipped | end-to-end, one test per flow | `tests/EndToEnd/AT.EndToEnd.Tests` |
  ```

  add:

  ```markdown
  | What the browser blocks or breaks in a web app | browser smoke, one test per app | `src/Web/packages/browser-smoke` |
  ```

- in `docs/guides/testing.md`, before the line:

  ```markdown
  ## Coverage
  ```

  add:

  ```markdown
  ## The browser smoke

  After the end-to-end suite, `end-to-end` drives every web app in Chromium, through
  `pnpm --filter "*browser-smoke" smoke` in `src/Web`. The package reads the apps `cleanarch.json` records
  and each one's port from `WEB_<APP>_HOST_PORT`, in the environment and then in `.env.example`, so an app
  added later is driven without an edit. For each app, one test:

  - loads `/`, which answers with a success status and renders text;
  - fails on any `securitypolicyviolation` the page raises, any console error and any uncaught exception;
  <!--#if (accounts)-->
  - calls `/api/v1/relay-check` from the page and expects the API's problem answer, `route.notFound` or
    `auth.required`, so the relay and the page's `connect-src` are both held.
  <!--#endif-->
  <!--#if (!accounts)-->
  - calls `/api/v1/relay-check` from the page and expects the API's `route.notFound` problem answer, so
    the relay and the page's `connect-src` are both held.
  <!--#endif-->

  <!--#if (externalAuth)-->
  When `EXTERNAL_AUTH_AUTHORITY` names a host under `.invalid`, which never resolves, as `end-to-end`
  and CI give the stack, the smoke stands in for that provider: it answers its discovery document with
  a minimal one and refuses every other request to it, and expects a WebAssembly app, which reads that
  document as it starts, to have asked for it at that authority.

  <!--#endif-->
  A page rendered on the server is driven the same way, at the port of its Node server. The smoke
  needs the stack, so `pnpm test` leaves it out; `pnpm test` runs the unit tests of how the apps and
  their ports are found.

  Chromium is not installed by `pnpm install`. Install it once per machine with
  `pnpm --filter "*browser-smoke" exec playwright install --only-shell chromium` in `src/Web`, adding
  `--with-deps` on a Linux that lacks the system libraries it loads.

  `end-to-end` fails with that command in its message when the browser is missing, and has no option to
  skip the smoke.
  <!--#if (ci)-->
  CI installs Chromium in the `end-to-end` job and caches it by the Playwright release the lockfile
  resolves.
  <!--#endif-->

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
  tests/                                  one test project per source project, mirroring its folders,
  ```

  add:

  ```text
  src/Web/                                the pnpm workspace: apps/ and packages/
  ```
