---
type: decision
title: 0005. An MCP server acts for the caller its credential names
description: Why the MCP server runs the use cases in process for the caller its credential names, over streamable HTTP.
status: Accepted
---

# 0005. An MCP server acts for the caller its credential names

Status: Accepted

## Context

An agent's client, an assistant or an editor, calls tools over the Model Context Protocol. Here each
tool is a use case, and every owner-scoped use case filters by its caller
([ADR 0004](0004-ownership-is-in-the-query.md)): a server that acts for one fixed account shows that
account's records to everyone, and one that acts for no one can call none of them. The protocol has
two transports. With stdio, the client starts the server as a child process on the user's machine.
With Streamable HTTP, the server runs as a service, and the protocol's authorization makes it an
OAuth 2.1 protected resource: it publishes the authorization servers it trusts (RFC 9728) and
accepts only tokens issued for itself (RFC 8707). A client holds the credential it was given for
as long as the user works, and refreshes nothing it was not told how to.

## Decision

- **Streamable HTTP, on the server, in process.** `<Project>.Mcp` is an ASP.NET Core host that
  composes the use cases in its own process
  ([composition](../architecture/layers-and-projects.md#composition)),
  stateless, so any instance answers any call and the server sends the client no request of its own.
<!--#if (localAuth)-->
- **The caller is the access key's owner.** A client sends, as a bearer credential, an access key its
  user created through the API; the server resolves it to its owner and scopes on every request, and
  a tool its key does not reach is neither listed nor called. The API's access token is not accepted
  here: it lasts fifteen minutes, and the client would not refresh it.
<!--#endif-->
<!--#if (externalAuth)-->
- **The caller is the token's subject.** The provider the API trusts issues the token, for the
  server's own audience, `Mcp:Resource`; a token issued for the API is refused, and the server passes
  no token on. The subject maps to the owner id as every host maps it
  ([an external identity provider](../features/authentication.md#an-external-identity-provider)).
<!--#endif-->
- **Anonymous is refused**, but for the health endpoints, as on the API.
- **A tool is one use case, declared by hand**, under a name and a description written for the
  agent; nothing is exposed because it exists, no tool deletes, and another caller's record answers
  exactly as an absent one.

[MCP server](../features/presentation.md#mcp-server) describes the tools, how a client calls them
and their tests.
<!--#if (localAuth)-->
[Access keys](../features/authentication.md#access-keys) describes the credential.
<!--#endif-->

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
- **Longer-lived access tokens.** A JWT cannot be revoked before it expires, and lengthening every
  token to serve a few clients weakens all of them.
- **Refreshing in the client.** The clients in question take a static header; those that could
  refresh would each carry rotation and replay handling this project cannot test.
- **This project as an authorization server, with the device flow or the protocol's own flow.** The
  standard answer, and the right one once third parties act for users, but a whole OAuth server of
  its own, client registration, consent and token endpoints, all to be secured and kept, for what is
  still the owner's own credential.
- **Personal access tokens accepted by the API as well.** Every endpoint would gain a second
  credential that skips the security stamp and the sessions, and a key leaked from a tool would
  reach the account routes.
- **Access keys in every project with local accounts.** Routes and a table that nothing accepts
  until a host does; they come with the MCP server instead.
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
- An owner gives a tool a credential and takes it back without touching the password; a password
  change leaves keys alive, and revoking them is the owner's act.
<!--#endif-->
- A use case reached by a tool is composed by a second host: a setting it reads is set for this host
  too.
- Without server-to-client requests, a tool cannot ask the user to confirm anything mid-call.
- `HostCompositionTests` and `McpAuthorizationTests` hold the composition, the catalogue and the
  refusals.

## Revisit when

A tool needs to ask its user something mid-call (elicitation), which needs sessions; an agent must
work offline on the user's machine; or a client must connect that cannot send a credential it was
given.
<!--#if (localAuth)-->
Third parties act on an owner's behalf and need consent and delegation, a key must carry finer
permissions than `read` and `write`, or a second host must accept keys.
<!--#endif-->
