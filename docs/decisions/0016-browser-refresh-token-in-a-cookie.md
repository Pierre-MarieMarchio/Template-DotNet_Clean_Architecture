# 0016. A browser's refresh token travels in a cookie

Status: Accepted

## Context

A browser app cannot keep a refresh token safely: whatever script can read, `localStorage`
included, a cross-site scripting flaw reads too, and a refresh token outlives the page by days. A
cookie script cannot read solves that, and brings its own risk: the browser attaches it to requests
another page provokes.

## Decision

- **Opt-in per request.** A request sends `X-Refresh-Token-Mode: cookie` on each endpoint that
  issues, rotates or revokes a grant: the sign-in completions, `refresh` and `logout`. The API then
  sets the refresh token as a cookie and leaves it out of the body. Without the header, every
  endpoint answers in body mode. Any value other than `cookie` is a `400`.
- **The cookie** is `refresh_token`: `HttpOnly`, `Secure`, `SameSite=Strict`, host-only, on
  `Path=/api/v1/auth` under the request's path base, expiring with the grant. It carries no
  `__Host-` prefix, which would force `Path=/` and send it with every request.
- **The header is the CSRF defence.** The cookie is read only beside the header. `SameSite=Strict`
  stops other sites, but another port of the same host is the same site; what such a page cannot do
  is send a custom header with credentials, because the request is preflighted and the CORS policy
  never allows credentials.
- **Only the API layer knows it.** The use cases see a string; `BrowserSession` alone knows the
  cookie.

<!--#if (anyWeb)-->
In this project's single-page web apps, the client package's `createBrowserSession` keeps the
access token in a variable only, never in storage, and calls `refresh()` once at start so a reload
costs one request. Concurrent refreshes share one request, since two would present one token twice
and end the session, and only a refused refresh signs the page out. A server-rendered app keeps both
tokens on its server instead, behind a session cookie of its own
([ADR 0024](0024-a-server-rendered-web-app-runs-on-its-own-server.md)).

<!--#endif-->
## Consequences

- Script never sees the refresh token; a flaw in the page can use the access token while the page
  lives, not keep the session.
- The deployment's ingress sends HSTS, which is what keeps a plain-HTTP page from planting a cookie
  of that name.
- `BrowserSessionTests` and `RefreshTokenCookieTests` hold the cookie, the header and the refusals.
<!--#if (anyWeb)-->
  `Src/Web/packages/api-client/test/session.test.ts` holds the browser side.
<!--#endif-->

[Browsers: the refresh-token cookie](../features/authentication.md#browsers-the-refresh-token-cookie)
is the client's contract.

## Revisit when

A browser app is served from a different site than the API, which `SameSite=Strict` refuses, or the
project moves to a backend-for-frontend that keeps every token server-side.
