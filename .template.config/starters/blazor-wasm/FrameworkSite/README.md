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
