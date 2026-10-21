# 0019. One generated API client

Status: Accepted

## Context

Web apps call the API. Types written by hand drift from what the API returns, and each framework
has its own idiom for HTTP, errors and credentials, so every app would solve the same problems its
own way. Shared code inside a workspace also has to be built before an app can use it, which brings
a build order and stale output.

## Decision

- **The schema is generated from the API's own document.** `pnpm api` builds the API with document
  generation on and turns the OpenAPI document into the TypeScript schema of the `api-client`
  package with `openapi-typescript`. The schema is committed, so an app type-checks without the .NET
  SDK.
<!--#if (ci)-->
  CI regenerates it and fails on any difference.
<!--#endif-->
- **One wrapper, the same for every framework.** `createApiClient` is `openapi-fetch` over the
  generated paths, relative to the page's origin, which each app's relay makes the API's. `unwrap`
  returns the body or throws an `ApiError` carrying the problem's `code` and field errors; an answer
  that is no problem document becomes `response.unreadable`, so a caller branches on codes alone.
  Credentials enter through an `accessToken` provider and the `middleware` list.
- **Internal packages export their sources.** A package's `exports` names its `src/index.ts`, and
  each app's bundler compiles it. A package's `build` is a type check.

## Consequences

- A change to the API's contract is a type error in every app that relied on the old one, found at
  build time.
- There is no build order between packages and no stale output; a change shows in every app's dev
  server at once.
- The shared compiler settings turn on what every framework's CLI turns on, so a package that passes
  its own build passes inside any app.
- A package cannot leave the workspace as it is; one that has to gets its own build step and an ADR.

[Calling the API](../features/web-frontend.md#calling-the-api) shows the client in use.

## Revisit when

A package must be published outside the workspace, or an app needs its framework's own HTTP client
for something the wrapper cannot carry.
