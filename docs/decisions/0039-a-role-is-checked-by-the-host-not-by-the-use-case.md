---
type: decision
title: 0039. A role is checked by the host, not by the use case
description: Why a role is a policy each host checks, and a use case never reads the caller's roles.
status: Accepted
---

# 0039. A role is checked by the host, not by the use case

Status: Accepted

## Context

Some operations are for administrators only: locking an account, purging expired rows, changing
another caller's roles. The check can live in the host that exposes the operation, as an ASP.NET
Core authorization policy on the route or the page, or in the use case, which would ask the caller's
roles before it runs. Several hosts can compose the same use cases: the API, a portal, an MCP server,
the worker. A check in the use case holds in every host at once; a check in the host holds where the
host puts it.

## Decision

- **A role is a policy of the host.** `AuthorizationPolicies.Administrator`, in
  `AppTemplate.Presentation.Core`, names the one policy; each host that authenticates callers
  registers it where it composes its authentication, requiring the role the deployment configures as
  the administrator's, and puts it on what it exposes: `[Authorize(Policy = ...)]` on each controller
  or page of administration and maintenance.
- **A use case knows the caller, not the caller's roles.** `ICurrentUser` names the caller and
  carries no role, so a handler can scope its query to the owner
  ([ADR 0009](0009-ownership-is-in-the-query.md)) but cannot ask whether the caller is an
  administrator. `RoleCheckTests` holds both faces: `ICurrentUser` exposes no role or claim, and no
  type of the application layer reads a principal, calls `IsInRole` or names an authorisation
  policy.
- **Default deny comes first.** Every route requires an authenticated caller unless it opts out, and
  the policy narrows a route to administrators on top of it.
<!--#if (localAuth && api)-->
- `AdministratorPolicyTests` holds that an ordinary caller gets `403` with a code and an anonymous
  one `401` on the administration routes, and `DefaultDenyAuthorizationTests` the default deny.
<!--#endif-->
<!--#if (externalAuth && api)-->
- `ExternalAuthenticationHostTests` holds that the provider's administrator role passes the policy
  and another role is refused, and `DefaultDenyAuthorizationTests` the default deny.
<!--#endif-->

## Alternatives considered

- **The role checked in the use case**, through a role on `ICurrentUser` or a pipeline behaviour
  reading an attribute on the message. One check for every host, but the application layer learns
  the deployment's role names and the claims they come from, a background job, which has no caller,
  must be let through by a special case, and an administration use case could no longer be reused by
  a host whose own rule differs.
- **Permissions instead of roles**, one per operation, checked in either place. Finer, and a
  permission model, its storage and its administration screens for a project that has one kind of
  privileged caller.

## Consequences

- The use cases stay free of who may call them; a host decides what it exposes and to whom.
- A second host that composes an administration use case must put the policy on it again: nothing
  in the use case refuses an ordinary caller. A review of a new host's routes, pages or tools reads
  each one that reaches such a use case.
- A new policy is a constant beside `Administrator` and an `AddPolicy` call in each host that
  composes authentication.

## When this decision does not apply

- **Ownership** is not a role: whose record a caller may read or change is decided in the query, in
  the use case's port, and answers `404` for another caller's record.

## Revisit when

The project needs permissions finer than one administrator role, or a use case must refuse a caller
by role whatever host sends it.
