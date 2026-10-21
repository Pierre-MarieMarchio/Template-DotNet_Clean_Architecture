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

`dotnet run Tools/Tasks.cs web-scaffold` at the project root creates each react or angular app that
still holds its `scaffold.json`, with its framework's official CLI, then installs and runs `pnpm api`;
`bootstrap` runs the same step.

## Adding an app or a package

At the project root, `@app-template` being this workspace's scope:

```bash
dotnet new cleanarch-webapp -n BackOffice --framework react --scope app-template --audience admin
dotnet new cleanarch-webpackage -n tokens --scope app-template
```

Pass the project's `--auth` to `cleanarch-webapp` when it is not the default.
The app's `README.md` gives the two lines that join it to the stack.

## The first app of a project generated without one

A project generated with `--web none` has no workspace. Its first app comes with the workspace:

```bash
dotnet new cleanarch-webapp -n PlaceholderSite --framework <react|angular|placeholder> --scope app-template --workspace
```

Pass the project's `--auth` and `--ci` too when they are not the defaults.

`--workspace` writes this folder, the shared packages, the API client and, unless `--ci false`,
`.github/workflows/web.yml`. The API side is in every project already. What lives in files the item
template cannot edit is yours to add, exactly as `--web` would have written it for this app:

- in `docker-compose.yml`, above `services:`:

  ```yaml
  # One line per web app: its compose.yml declares the service, built from Src/Web.
  include:
    - path: Src/Web/apps/PlaceholderSite/compose.yml
  ```

- in `.env.example`, below `API_HOST_PORT`, the port the app's `compose.yml` reads:

  ```bash
  # Web apps, loopback only: one port per app, named after its folder.
  WEB_PLACEHOLDER_SITE_HOST_PORT=8081
  ```

- at the end of `.dockerignore`, so the API's image leaves the workspace out of its build context:

  ```text
  # ---------------------------------------------------------------------------
  # The web workspace: each web app builds from Src/Web with its own image.
  # ---------------------------------------------------------------------------
  Src/Web/
  ```

- in `.github/dependabot.yml`, the apps' images under the `docker` entry's `directories`, and the
  workspace's packages as an entry of their own:

  ```yaml
        # Every web app's nginx image, by directory glob.
        - /Src/Web/apps/*
  ```

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

Another app's lines are in its own `README.md`. Then run `dotnet run Tools/Tasks.cs web-scaffold` for
a react or angular app, or `pnpm install` here for a placeholder, and commit the workspace with its
`pnpm-lock.yaml`.
