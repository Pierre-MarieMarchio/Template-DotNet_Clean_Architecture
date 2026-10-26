# 0003. Framework apps come from their official CLIs

Status: Superseded by [0008](0008-a-web-app-comes-from-a-starter.md)

## Context

A generated project may start with a React or an Angular app. A framework's starter, its build
settings and its dependency set change with every release; a copy kept in the template would age
between two template releases, and maintaining it would be maintaining a second product.

## Decision

- **The template keeps no React or Angular code.** For each framework it holds only the wiring: a
  `Dockerfile`, an `nginx.conf`, a `compose.yml`, a `README.md`, a `scaffold.json` descriptor and an
  `overlay/` folder.
- **The app is created at bootstrap.** `dotnet run Tools/Tasks.cs web-scaffold`, which `bootstrap`
  runs, runs the CLI named in `scaffold.json` at its exact version through `pnpm dlx`: `create-vite`
  with its `react-ts` template, or the Angular CLI's `ng new`. It writes into a scratch folder inside
  the workspace and moves nothing into place until the starter is complete. The CLI runs from that
  folder with its input closed, so a CLI that asks a question makes `web-scaffold` fail with the app's
  folder unchanged instead of waiting.
- **The overlay is wiring and nothing else**: the dev server's relay to the API, the package name,
  the API client dependency, the scripts the workspace runs, and the compiler settings the client's
  sources need. Settings land as JSON merges into the CLI's own files, and a file is replaced only
  where it cannot be configured.
- **Then the app is the project's.** Every dependency range is pinned to the version installed and
  the lockfile is written; with the descriptor gone, nothing is created again.

## Consequences

- The template follows no framework release. Bumping one is editing the version in its
  `scaffold.json` and running its acceptance leg.
- A project's first app is exactly the CLI's starter, as its authors write it; the screens are the
  project's to write.
- Bootstrap needs the package registry once; an offline run fails, leaves the folder as it was, and
  says so.
- The placeholder app has no CLI: it is the template's own static page, kept in the repository.

## Revisit when

A CLI's output can no longer be wired by merges into its own files, or projects must be generated
without network access.
