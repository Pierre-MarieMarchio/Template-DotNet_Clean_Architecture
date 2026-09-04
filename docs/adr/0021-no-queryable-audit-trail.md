# 0021 — No queryable audit trail in the application database

Status: Accepted

## Context

`RequestLoggingMiddleware`
(`Src/Presentation/AppTemplate.Api/Common/Observability/RequestLoggingMiddleware.cs`)
already logs one structured entry per request carrying both `TraceIdentifier` — the value
quoted in every ProblemDetails `traceId` — and the W3C `TraceId`, so a caller's complaint
joins directly to a trace. Security-relevant events — logins, lockouts, refresh-token
family revocations, password changes — are emitted the same way, as structured log
entries correlated to that trace, not written to a table. `SECURITY.md`'s "Known gaps"
section already names this: "No audit log of security-relevant events. Logins, lockouts,
token-family revocations and password changes are traced but not recorded in a queryable,
tamper-evident store." This record is the decision behind that gap staying open.

## Decision

**No audit table is added to the application's own database.** The structured log
pipeline already in place is the audit trail this template ships, and it is deliberately
not a database table this API can read from or write to.

## Consequences

- The value an audit trail exists to provide is that it is outside the reach of the thing
  being audited. A table reachable through the same connection string and the same
  runtime credentials as `TodoList` rows is not that: any code path with `UPDATE`/`DELETE`
  rights on the database — which SECURITY.md already requires the runtime user to have,
  for its own tables — could also rewrite or erase audit rows, whether by bug or by an
  attacker who got that far. An operator who trusted such a table as tamper-evident would
  be worse off than one who knew plainly there wasn't one.
- Closing this properly needs storage the application's own credentials cannot mutate:
  append-only storage, or a separate write-only sink with its own retention policy and its
  own access control, decided by whoever deploys this — not a schema choice a template
  can make once for every deployment.
- The structured log pipeline (`RequestLoggingMiddleware`, and the same `ILogger` calls
  around Identity's login, lockout and token-revocation paths) is the seam that plugs into
  whatever external store a deployment adds — the events are already emitted; only their
  destination changes.
- `SECURITY.md`'s known-gaps entry stands, deliberately, until a deployment closes it.

## Alternatives rejected

- **An audit table in the same database.** Same credentials, same reach, as above —
  provides the appearance of tamper-evidence without the property.
- **An EF Core `SaveChangesInterceptor` writing audit rows in the same transaction as the
  business change.** Same credentials, same reach, plus a second cost: it ties
  auditability to whichever operations happen to go through EF Core's change tracker,
  when several of the events that matter most here — a failed login, a lockout — are not
  `SaveChanges` calls on an aggregate at all.
- **A hash-chained audit table**, each row's hash covering the previous row, to detect
  tampering after the fact. Detects a rewrite only if whatever verifies the chain runs
  outside the system that wrote the chain — a verifier living in the same database, run by
  the same credentials, can be defeated by recomputing the chain along with the rewrite.
  This does not remove the need for a store outside the application's own reach; it only
  adds a check that is only as trustworthy as where it runs.

## Revisit when

A compliance requirement names a specific retention period and a specific access model for
security events. That is the point to pick the append-only store or external sink the
requirement actually demands, rather than build one speculatively now.
