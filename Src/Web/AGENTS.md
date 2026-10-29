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

`pnpm api` builds the API, so it needs the .NET SDK as well as Node.

## Files not edited by hand

- `Src/Web/packages/api-client/src/generated/`: `pnpm api` writes it from the API's OpenAPI document.
  Change the API, run `pnpm api`, commit the result.
- The workspace lockfile: pnpm writes it; commit it with the change that moved it, since CI and every
  image install exactly what it records.

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
- An app calls the API through `@app-template/api-client`, typed from the API's document, and never
  imports another app.
<!--#if (localAuth)-->
- In a single-page app the access token lives in a variable, never in storage, and the refresh token
  stays in the API's `HttpOnly` cookie ([ADR 0016](../../docs/decisions/0016-browser-refresh-token-in-a-cookie.md));
  a server-rendered app keeps both on its server, and its pages send `X-CSRF: 1` through
  `serverSessionMiddleware` ([ADR 0024](../../docs/decisions/0024-a-server-rendered-web-app-runs-on-its-own-server.md)).
<!--#endif-->
- A new app or package comes from `dotnet new cleanarch-webapp` or `dotnet new cleanarch-webpackage`
  at the project root; [more apps and packages](../../docs/features/web-frontend.md#more-apps-and-packages)
  gives the lines to add after it.

## Done means

`pnpm lint`, `pnpm test` and `pnpm build` pass here, and the root gate passes too.
<!--#if (ci)-->
`.github/workflows/web.yml` runs the same three and fails when the committed client differs from what
`pnpm api` writes.
<!--#endif-->
