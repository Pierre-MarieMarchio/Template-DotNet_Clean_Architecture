# 0024. A server-rendered web app runs on its own server

Status: Accepted

## Context

A web app of this project is either a single-page app, a static site that nginx serves, or a
server-rendered app, which its framework's server writes for each request. A server-rendered page
changes the forces nginx answers for the others:

- each response is written anew, and the framework adds inline scripts while it renders, such as
  Angular's event replay; a hash computed when the image is built cannot know them;
- after the first page the browser still calls the API, from the page's origin, and the API keeps
  CORS closed;
- the server needs the page's data before any script runs, and a single-page app keeps a caller's
  access token in a browser variable, with the refresh token in a cookie scoped to `/api/v1/auth`
  ([ADR 0016](0016-browser-refresh-token-in-a-cookie.md)), which a page request never carries.

## Decision

- **The app's own Node server serves it, in place of nginx.** It runs the framework's server build,
  serves the browser files with the cache rules nginx gives a static app, sends the same page
  headers, and relays `/api/` and `/health` to `API_UPSTREAM` through `relayToApi` of the
  `api-client` package, forwarding the browser's address, so the browser keeps one origin and the
  API keeps CORS closed.
- **The server calls the API directly**, at `API_UPSTREAM`, through `createApiClient` with that base
  address ([ADR 0019](0019-one-generated-api-client.md)), never through its own relay.
- **Every page carries a fresh nonce.** Each response draws 128 random bits, sends them in
  `script-src 'self' 'nonce-<value>'`, and hands them to the framework, which writes them on each
  script it emits: Angular takes them as `CSP_NONCE` from the request context. A script the
  framework's build writes into the page template, Angular's event-dispatch contract, carries no
  nonce and runs by the SHA-256 the server computes from that template when it starts. Every route
  renders per request, since a page prerendered at build cannot carry a response's nonce. Scripts
  never run by `'unsafe-inline'`, and a rendered page is `Cache-Control: no-store`.
<!--#if (localAuth)-->
- **The tokens stay on the server.** The browser holds one cookie, `__Host-session`: an opaque
  random id, `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, expiring with the refresh token. The
  server keeps the access and refresh tokens that id names, in its own memory, through
  `serverSessions` of the `api-client` package. Its relay drops the `Authorization`, `Cookie`,
  `X-Refresh-Token-Mode` and `X-CSRF` headers the browser sends and adds the session's
  `Authorization: Bearer`; a page rendered for the browser calls the API with the same token.
- **Signing in goes through the relay.** On the routes that issue a grant, `login`,
  `login/two-factor`, `login/external` and `passkeys/sign-in`, the relay forwards the request in body
  mode, keeps the tokens the API answers, opens the session, and answers without the access and
  refresh tokens; a sign-in replaces the session the browser had. On `logout` it revokes the
  session's refresh token and ends the session. The browser never calls `refresh`;
  `createBrowserSession` serves the single-page apps alone.
- **A session refreshes once at a time.** The server refreshes an access token 30 seconds before it
  expires, and the requests of that session wait for that one refresh, since the API ends every
  session of an account when one refresh token is presented twice
  ([ADR 0013](0013-a-session-is-a-refresh-token-family.md)). The refresh carries the browser's
  `User-Agent` and address, so the API's session list names the browser. A refused refresh ends the
  session and clears the cookie.
- **A request that changes state carries a header.** The relay answers `403`
  `session.csrfHeaderRequired` to a request other than `GET` or `HEAD` without `X-CSRF: 1`, before
  the API sees it, and `serverSessionMiddleware` sends that header. A page of another origin cannot
  send it without a preflight the server never allows, which is the defence ADR 0016 relies on, here
  for a cookie the browser attaches to every request.
<!--#endif-->
<!--#if (externalAuth)-->
- **A server-rendered app keeps no session for an external provider yet.** The browser alone holds
  the provider's access token, so a server would render every page signed out;
  `cleanarch-webapp --rendering ssr` refuses a project that signs in at an external provider.
<!--#endif-->
<!--#if (publicApi)-->
- **There is no session.** This project authenticates nobody, so the server renders the same pages
  for everyone and its relay forwards what the browser sends.
<!--#endif-->
- The server tests of the `api-client` package hold the relay and the session, and `WebAppTests`
  holds, for every web app in the stack, that an inline script runs only by its hash or by its
  response's nonce, and that no two responses share a nonce.
<!--#if (localAuth)-->
  It also holds that a server-rendered app refuses a call that changes state without `X-CSRF`.
<!--#endif-->

## Alternatives considered

- **nginx in front of the Node server.** It keeps the static app's configuration, but the nonce is
  written by the Node server anyway, and each app becomes two services.
- **Hashes, as the static apps have.** A hash is computed from the page the image builds; the
  scripts a server adds while rendering are in no such page.
- **Prerendering at build, served by nginx.** It renders only the pages known at build, with no
  request's data, and a prerendered page carries no response's nonce.
- **The server calling the API through its own relay.** A hop through its own process, for a call
  that never needs the browser's origin.
<!--#if (localAuth)-->
- **The browser keeps the session, as in ADR 0016, and the server renders for an anonymous caller.**
  Nothing new on the server and no change to the browser's contract, but every signed-in page
  renders signed out and fills in once the script runs, which takes away what server rendering is
  chosen for on those pages.
- **The tokens in an encrypted cookie instead of the server's memory.** No state, and several
  instances at no cost, but two requests of one page each rotate the refresh token, and the API reads
  the second as a replay and ends every session of the account.
- **A shared session store from the start.** It survives a restart and serves several instances, at
  the cost of one more service every project would run before one needs it.
- **`SameSite=Strict` on the session cookie.** A person following a link from another site would see
  the first page rendered signed out; `Lax` sends the cookie on a top-level navigation, and the header
  guards every request that changes state.
<!--#endif-->

## Consequences

- A server-rendered app runs as one instance of a Node process, built from the same workspace and
  lockfile as the others; its image runs as the unprivileged `node` user on port 8080.
<!--#if (localAuth)-->
- Script in the page never holds a token: a flaw in the page can act through the relay while the
  page lives, with the header, but cannot carry the session away. The sessions live as long as the
  server process, so a restart or a deployment signs everyone out.
<!--#endif-->
- Angular's server answers `400` to a host `NG_ALLOWED_HOSTS` does not list, its defence against
  server-side request forgery: a deployment names the host its users reach.
- The API's rate limits see the browser's address once `ReverseProxy` names the web server, as they
  do for nginx ([Behind a proxy](../guides/deployment.md#behind-a-proxy)).
- ADR 0016 still governs the single-page apps; its revisit condition named this backend-for-frontend.
  No row of
  [ADR 0001](0001-adopt-the-generated-baseline.md) changes.

## Revisit when

A server-rendered app must run more than one instance or keep its sessions across a restart, which
needs a shared store, must sign people in at an external provider, or the framework's own server
gains a session or a nonce mechanism that does this work.
