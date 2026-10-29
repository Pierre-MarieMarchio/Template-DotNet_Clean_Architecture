# 0027. An MCP server acts for the caller its credential names

Status: Accepted

## Context

An agent's client, an assistant or an editor, calls tools over the Model Context Protocol. Here each
tool is a use case, and every owner-scoped use case filters by its caller
([ADR 0009](0009-ownership-is-in-the-query.md)): a server that acts for one fixed account shows that
account's records to everyone, and one that acts for no one can call none of them. The protocol has
two transports. With stdio, the client starts the server as a child process on the user's machine.
With Streamable HTTP, the server runs as a service, and the protocol's authorization makes it an
OAuth 2.1 protected resource: it publishes the authorization servers it trusts (RFC 9728) and
accepts only tokens issued for itself (RFC 8707). A client holds the credential it was given for
as long as the user works, and refreshes nothing it was not told how to.

## Decision

- **Streamable HTTP, on the server, in process.** `<Project>.Mcp` is an ASP.NET Core host that
  composes the use cases in its own process
  ([ADR 0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md))
  and maps the endpoint of `ModelContextProtocol.AspNetCore` at `/mcp`. It runs stateless: each
  request is answered in its own scope, with no session between requests, so any instance answers
  any call, and the server sends the client no request of its own.
<!--#if (localAuth)-->
- **The caller is the access key's owner.** A client sends an access key its user created through
  the API, as a bearer credential ([ADR 0026](0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md)).
  The server resolves it through the Auth context's own use case to the owner and the scopes it
  grants, on every request, so a key revoked or expired, or an account locked, stops at the next
  call. A key that grants `read` reaches the tools that read, one that grants `write` the tools that
  create; a tool its key does not reach is neither listed nor called. The API's access token is not
  accepted here: it lasts fifteen minutes, and the client would not refresh it.
<!--#endif-->
<!--#if (externalAuth)-->
- **The caller is the token's subject.** The provider the API trusts issues the token. The server
  validates it as the API does, against `Authentication:External`, but for its own audience,
  `Mcp:Resource`, the address clients connect to: a token issued for the API is refused, and the
  server passes no token on. The issuer and subject map to the owner id as
  [ADR 0017](0017-external-subjects-map-to-internal-owners.md) has it, provisioned on the first
  request, with roles from the same source.
<!--#endif-->
- **Anonymous is refused.** The endpoint requires an authenticated caller.
<!--#if (externalAuth)-->
  A call without a valid token is a `401` whose `WWW-Authenticate` names the protected resource
  metadata, and `/.well-known/oauth-protected-resource/mcp` names the provider.
<!--#endif-->
<!--#if (localAuth)-->
  A call without a valid key is a `401` with a bare `Bearer` challenge: no authorization server
  issues access keys, so the server names none.
<!--#endif-->
  The health endpoints answer anyone, as the API's do.
- **A tool is one use case, declared by hand.** Each tool calls one use case in its request's scope,
  under a name and a description written for the agent, and says whether it only reads; nothing is
  exposed because it exists, and no tool deletes. A failure is a tool result marked as an error,
  carrying the error's `code` and detail, the codes the [API reference](../reference/api.md) lists,
  so another caller's record answers exactly as an absent one.

## Alternatives considered

- **stdio, composing the use cases on the user's machine.** It would carry the database's
  credentials onto every machine that runs the client.
- **stdio, as an operator's tool on the server.** It acts for no one, and every owner-scoped use
  case refuses a scope without a caller.
- **A client of the API over stdio, through the .NET client and the user's token.** A desktop shape
  of its own, each call crossing HTTP twice; nothing here prevents adding it beside this one.
- **A service account the server acts as.** Ownership would hold for the wrong owner: every caller
  would see that account's records.
<!--#if (externalAuth)-->
- **The API's audience.** A token stolen from one host would open the other, and the protocol
  forbids a server to accept a token issued for another resource.
<!--#endif-->
<!--#if (localAuth)-->
- **The API's access token.** It expires within the quarter hour and a client cannot refresh it,
  and a token stolen from one host would open the other.
- **This project as an authorization server.** The protocol's own flow would let a client obtain
  and refresh tokens, at the price of an OAuth server of its own: client registration, consent and
  token endpoints, all to be secured and kept.
<!--#endif-->
- **Sessions bound to an instance.** Every call of a session would have to reach the instance that
  opened it, and the caller of a session's later calls would need a rule of its own.

## Consequences

<!--#if (externalAuth)-->
- The provider registers the server as a resource, with `Mcp:Resource` as its audience, and each MCP
  client as a public client with a loopback redirect; a provider without dynamic client registration
  needs each client registered by hand.
<!--#endif-->
<!--#if (localAuth)-->
- A user creates a key in the API, once signed in, and pastes it into the client's configuration
  as an `Authorization` header; a client that only knows the protocol's OAuth flow cannot connect.
<!--#endif-->
- A use case reached by a tool is composed by a second host: a setting it reads is set for this host
  too.
- Without server-to-client requests, a tool cannot ask the user to confirm anything mid-call.
- `HostCompositionTests` resolves every use case and every tool type the server composes, and holds
  the tools to the catalogue its tests read; `McpAuthorizationTests` holds the anonymous `401`,
<!--#if (externalAuth)-->
  its metadata, the refusal of a token issued for the API,
<!--#endif-->
<!--#if (localAuth)-->
  the refusal of an unknown or revoked key, a read-only key that sees and calls no tool that writes,
<!--#endif-->
  and that another caller's record answers as an absent one.

[Presentation project](../features/presentation.md) describes the tools and their settings.

## Revisit when

A tool needs to ask its user something mid-call (elicitation), which needs sessions; an agent must
work offline on the user's machine; or a client must connect that cannot send a credential it was
given.
