---
type: decision
title: 0037. A web app is deployed apart, behind a relay to the API
description: Why each web app is a deployable of its own that relays its API calls, rather than files the API serves.
status: Accepted
---

# 0037. A web app is deployed apart, behind a relay to the API

Status: Accepted

## Context

A browser app and the API it calls can share an origin or not. The API can serve the app's built
files itself, so both are one deployable on one origin. The app can be served apart, on an origin
of its own, and call the API across origins, which takes CORS opened to that origin, with
credentials when a cookie must travel. Or the app can be served apart and relay the API's paths, so
the browser sees one origin per app while the API stays a service of its own.

The API keeps CORS closed by default. A workspace may hold several apps, each built by its
framework's own toolchain, on Node rather than .NET.
<!--#if (localAuth)-->
The browser's refresh-token cookie is `SameSite=Strict` and read only beside a header a page of
another origin cannot send with credentials
([ADR 0016](0016-browser-refresh-token-in-a-cookie.md)).
<!--#endif-->

## Decision

- **Each web app is a deployable of its own**: one image per app, built from the workspace, served
  by nginx for a static app or by the app's own Node server for a server-rendered one, and one
  Compose service, which its `compose.yml` declares.
- **The app relays the API.** Its development server, its nginx and its Node server forward `/api/`
  and `/health` to `API_UPSTREAM`, forwarding the browser's address, so the browser calls the API on
  the page's own origin.
- **The API never serves an app**, and no app of this workspace needs an entry in
  `Cors:AllowedOrigins`, which allows no origin by default.
- `WebAppTests.AWebApp_ServesItsPageAndRelaysApiToTheApi` holds the relay of every app in the
  stack, and the browser smoke calls the API through it from inside the page; `end-to-end` runs
  both.

## Alternatives considered

- **The API serves the built app.** One deployable, but the API's image carries every app's build,
  a change to a page redeploys the API, the apps' security headers are the API's, and a
  server-rendered app cannot run in it.
- **The app on its own origin, calling the API through CORS.** No relay, but every app's origin is
  listed in the API's settings, each call that sends a header is preflighted, and a cookie crosses
  origins only with CORS allowing credentials, which a cookie-based session must then defend
  against.
- **One gateway in front of every app and the API.** One origin for all, and one more service every
  project runs and configures, whose routes must follow each app added; a deployment can still put
  one in front of the relays.

## Consequences

- The browser sees one origin per app: the generated client calls relative paths, and the cookies
  and the Content-Security-Policy of an app concern its origin alone.
- Each app deploys, scales and rolls back apart from the API; a contract change still needs the app
  and the API released together.
- The API sees the relay's address unless `ReverseProxy` names the web servers, as for any proxy
  ([Behind a proxy](../guides/deployment.md#behind-a-proxy)); its rate limits key on the browser's
  address only then.
- A relay is one more hop per call, and its timeouts and body limits are the app's to keep in step
  with the API's.

## When this decision does not apply

- **A client that is not a web app of this workspace**, a desktop or mobile app, a script, or a site
  another team serves, calls the API on its own origin. A browser app on another origin needs its
  origin in `Cors:AllowedOrigins`, and gets no cookie-based session from the API.

[Web front end](../features/web-frontend.md) describes the relay of each kind of app.

## Revisit when

The apps and the API must be served from one origin by a gateway the deployment provides anyway, or
an app needs to call an API that is not this project's.
