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
- **A server-rendered app comes with a project that authenticates nobody.** The browser alone holds
  a caller's access token, so a server would render every page signed out;
  `cleanarch-webapp --rendering ssr` refuses a project with accounts.
- The server tests of the `api-client` package hold the relay, and `WebAppTests` holds, for every web
  app in the stack, that an inline script runs only by its hash or by its response's nonce, and that
  no two responses share a nonce.

## Alternatives considered

- **nginx in front of the Node server.** It keeps the static app's configuration, but the nonce is
  written by the Node server anyway, and each app becomes two services.
- **Hashes, as the static apps have.** A hash is computed from the page the image builds; the
  scripts a server adds while rendering are in no such page.
- **Prerendering at build, served by nginx.** It renders only the pages known at build, with no
  request's data, and a prerendered page carries no response's nonce.
- **The server calling the API through its own relay.** A hop through its own process, for a call
  that never needs the browser's origin.

## Consequences

- A server-rendered app runs as one instance of a Node process, built from the same workspace and
  lockfile as the others; its image runs as the unprivileged `node` user on port 8080.
- Angular's server answers `400` to a host `NG_ALLOWED_HOSTS` does not list, its defence against
  server-side request forgery: a deployment names the host its users reach.
- The API's rate limits see the browser's address once `ReverseProxy` names the web server, as they
  do for nginx ([Behind a proxy](../guides/deployment.md#behind-a-proxy)).
- ADR 0016 still governs the single-page apps. No row of
  [ADR 0001](0001-adopt-the-generated-baseline.md) changes.

## Revisit when

A server-rendered app must sign people in, which needs a session its server keeps, or must run more
than one instance, or the framework's own server gains a nonce mechanism that does this work.
