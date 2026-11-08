# Agents: the web workspace

What a change under `src/Web` needs beyond [the root AGENTS.md](../../AGENTS.md), which still applies.
[The web front end](../../docs/features/web-frontend.md) says how the apps run and call the API.

## Commands

From `src/Web`, after `corepack enable` once per machine:

| When | Command |
|---|---|
| After a clone or a pull | `pnpm install --frozen-lockfile` |
| <!--#if (webNodeFramework)--> |
| While you work on one app | `pnpm --filter <app> dev`, `<app>` being the folder's kebab name |
| <!--#endif--> |
| <!--#if (webBlazorWasm)--> |
| While you work on the WebAssembly app | `dotnet watch --project apps/<Name>`, its pages without the `/api` relay |
| <!--#endif--> |
| After a change to the API's routes or bodies | `pnpm api` |
| Before calling a change done | `pnpm lint`, `pnpm test`, `pnpm build` |
| After a change to an app's page, headers or relay | `dotnet cleanarch end-to-end`, which ends with `pnpm --filter "*browser-smoke" smoke` |

`pnpm api` runs `dotnet cleanarch openapi`, which builds the API and writes the OpenAPI reference,
then writes the schema from it, so it needs the .NET SDK as well as Node; `pnpm api --no-build` reads
the API's last build instead.

## Files not edited by hand

- `src/Web/packages/api-client/src/generated/`: `pnpm api` writes it from the API's OpenAPI document.
  Change the API, run `pnpm api`, commit the result.
- The workspace lockfile: pnpm writes it; commit it with the change that moved it, since CI and every
  image install exactly what it records.
- `src/Web/patches/`: `pnpm patch` and `pnpm patch-commit` write it; when a new release refuses a
  patch, make the patch again from that release.

## Rules

<!--#if (webNodeFramework)-->
- A framework app starts from its framework's official CLI, which `bootstrap` and `dotnet cleanarch add web-app`
  run once; from then on the app is this project's own, and they leave it alone.
<!--#endif-->
<!--#if (webBlazorWasm)-->
- The WebAssembly app starts from `dotnet new blazorwasm`, which `bootstrap` and `dotnet cleanarch add web-app`
  run once; it is a .NET project of the solution, built and formatted with it, and calls the API
  through the .NET client of `src/Presentation/AT.Presentation.Client`, never through `@app-template/api-client`.
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
- A new app or package comes from `dotnet cleanarch add web-app` or `dotnet cleanarch add web-package`,
  which make every edit it needs outside its folder, as [more apps and packages](../../docs/features/web-frontend.md#more-apps-and-packages) says.

## Done means

`pnpm lint`, `pnpm test` and `pnpm build` pass here, and the root gate too, which runs them and
needs Node.js and pnpm. A change to what an app serves or relays is done once `end-to-end` passes:
only the browser smoke runs the page.
<!--#if (ci)-->
`.github/workflows/web.yml` runs the same three and fails when the committed client differs from what
`pnpm api` writes.
<!--#endif-->
