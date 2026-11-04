# Agents: the web workspace

What a change under `Src/Web` needs beyond [the root AGENTS.md](../../AGENTS.md), which still applies.
[The web front end](../../docs/features/web-frontend.md) says how the apps run and call the API.

## Commands

From `Src/Web`, after `corepack enable` once per machine:

| When | Command |
|---|---|
| After a clone or a pull | `pnpm install --frozen-lockfile` |
<!--#if (webNodeFramework)-->
| While you work on one app | `pnpm --filter <app> dev`, `<app>` being the folder's kebab name |
<!--#endif-->
<!--#if (webBlazorWasm)-->
| While you work on the WebAssembly app | `dotnet watch --project apps/<Name>`, its pages without the `/api` relay |
<!--#endif-->
| After a change to the API's routes or bodies | `pnpm api` |
| Before calling a change done | `pnpm lint`, `pnpm test`, `pnpm build` |
| After a change to an app's page, headers or relay | `dotnet run Tools/Tasks.cs end-to-end`, which ends with `pnpm --filter "*browser-smoke" smoke` |

`pnpm api` builds the API, so it needs the .NET SDK as well as Node; `pnpm api --no-build` reads the
API's last build instead.

## Files not edited by hand

- `Src/Web/packages/api-client/src/generated/`: `pnpm api` writes it from the API's OpenAPI document.
  Change the API, run `pnpm api`, commit the result.
- The workspace lockfile: pnpm writes it; commit it with the change that moved it, since CI and every
  image install exactly what it records.
- `Src/Web/patches/`: `pnpm patch` and `pnpm patch-commit` write it, and `patchedDependencies` in
  `Src/Web/pnpm-workspace.yaml` names it, by a version range where the first install resolves the
  newest release, as for openapi-typescript's `@redocly/openapi-core`; an install that fails because
  a new release refuses the patch needs the patch made again from that release.

## Rules

<!--#if (webNodeFramework)-->
- A framework app starts from its framework's official CLI, which `dotnet run Tools/Tasks.cs web-scaffold`
  runs once; from then on the app is this project's own, and `web-scaffold` leaves it alone.
<!--#endif-->
<!--#if (webBlazorWasm)-->
- The WebAssembly app starts from `dotnet new blazorwasm`, which `dotnet run Tools/Tasks.cs web-scaffold`
  runs once; it is a .NET project of the solution, built and formatted with it, and calls the API
  through the .NET client of `Src/Clients`, never through `@app-template/api-client`.
<!--#endif-->
<!--#if (webPlaceholder)-->
- The placeholder app is a static page under `public/`, served by nginx; replace those files with the
  build output of the front end this project grows into.
<!--#endif-->
- `packages/browser-smoke` finds the apps in `cleanarch.json` and their ports in the stack's environment:
  an app is never listed in it by hand, and `smoke` stays apart from `pnpm test`, which needs no stack.
- An app calls the API through `@app-template/api-client`, typed from the API's document, and never
  imports another app.
<!--#if (localAuth)-->
- In a single-page app the access token lives in a variable, never in storage, and the refresh token
  stays in the API's `HttpOnly` cookie ([ADR 0016](../../docs/decisions/0016-browser-refresh-token-in-a-cookie.md));
  a server-rendered app keeps both on its server, and its pages send `X-CSRF: 1` through
  `serverSessionMiddleware`.
<!--#endif-->
<!--#if (externalAuth)-->
- A server-rendered app keeps the provider's tokens on its server, which signs people in at
  `/session/sign-in`, and its pages send `X-CSRF: 1` through `serverSessionMiddleware`; no token
  reaches the page.
<!--#endif-->
<!--#if (webServerRendering)-->
- [ADR 0024](../../docs/decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md) says why a
  server-rendered app runs on its own server and keeps the session there.
<!--#endif-->
- A new app or package comes from `dotnet new cleanarch-webapp` or `dotnet new cleanarch-webpackage`
  at the project root; [more apps and packages](../../docs/features/web-frontend.md#more-apps-and-packages)
  gives the lines to add after it.

## Done means

`pnpm lint`, `pnpm test` and `pnpm build` pass here, and the root gate passes too: `verify` runs the
three after a frozen install, and stops, saying how to install pnpm, on a machine without it. A change
to what an app serves or relays is done once `end-to-end` passes, since only the browser smoke runs the
page.
<!--#if (ci)-->
`.github/workflows/web.yml` runs the same three and fails when the committed client differs from what
`pnpm api` writes.
<!--#endif-->
