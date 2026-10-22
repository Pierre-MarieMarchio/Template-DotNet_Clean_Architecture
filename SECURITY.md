# Security

What this project protects, what a deployment must still do, and the gaps it knows of. A control
listed under [Before you deploy](#before-you-deploy) is not a suggestion: it is a hole until you close
it.

## Reporting a vulnerability

Replace this section before the first deployment. State the address you monitor for reports, how
soon you acknowledge one, what a reporter may expect after that, and whether you offer a safe harbour.
Until it is replaced, a reporter has no private channel to use.
<!--#if (templateMaintenance)-->

For the template itself, open a GitHub issue: the template ships no running service and no binary, so a
public report is the fastest and the most useful.
<!--#endif-->

## What this project protects

Each row links to the page that describes it.

| Protection | Where it is described |
|---|---|
<!--#if (accounts)-->
| Default-deny authorisation: an endpoint is protected unless it opts out, and an unknown route answers `401` to an anonymous caller | [Request pipeline › Authorisation](docs/architecture/request-pipeline.md#authorisation) |
| Another caller's resource answers `404`, never `403`: the owner is in the query | [ADR 0009](docs/decisions/0009-ownership-is-in-the-query.md) |
<!--#endif-->
<!--#if (publicApi)-->
| Public access is a policy written in the composition, and no caller is ever identified | [Authentication › A public API](docs/features/authentication.md#a-public-api) |
<!--#endif-->
<!--#if (localAuth)-->
| Opaque refresh tokens that rotate on every use; a replay ends every session of the account | [Authentication › Access and refresh tokens](docs/features/authentication.md#access-and-refresh-tokens), [ADR 0013](docs/decisions/0013-a-session-is-a-refresh-token-family.md) |
| Sessions an owner can list and end; a credential change ends the others | [Authentication › Sessions](docs/features/authentication.md#sessions) |
| Two-factor sign-in and passkeys | [Authentication › Two-factor](docs/features/authentication.md#two-factor), [ADR 0015](docs/decisions/0015-passkeys-stand-on-their-own.md) |
| A browser's refresh token in an `HttpOnly`, `SameSite=Strict` cookie, read only beside its header | [ADR 0016](docs/decisions/0016-browser-refresh-token-in-a-cookie.md) |
| Registration and recovery reveal nothing about which addresses exist | [ADR 0014](docs/decisions/0014-anonymous-routes-reveal-nothing.md), [Accounts](docs/features/accounts.md#registration-and-email-confirmation) |
| Password policy and lockout | [Authentication › Configure](docs/features/authentication.md#configure) |
| A security log of sign-ins and credential changes, naming no address | [Accounts › The security log](docs/features/accounts.md#the-security-log) |
<!--#endif-->
<!--#if (externalAuth)-->
| Bearer tokens validated against the provider's keys; each (issuer, subject) mapped to an internal owner | [Authentication › An external identity provider](docs/features/authentication.md#an-external-identity-provider), [ADR 0017](docs/decisions/0017-external-subjects-map-to-internal-owners.md) |
<!--#endif-->
| Rate limiting per client address, answering `429` with `Retry-After` | [Request pipeline › Rate limiting](docs/architecture/request-pipeline.md#rate-limiting) |
| An oversized body refused before it is buffered, and logged; a timeout per request | [Request pipeline › Body size and timeouts](docs/architecture/request-pipeline.md#body-size-and-timeouts) |
| Security headers on every response, a default-deny CSP, CORS that allows nothing until told | [Request pipeline › CORS and security headers](docs/architecture/request-pipeline.md#cors-and-security-headers) |
| No exception text reaches a client; every refusal is a problem document with a `code` and a `traceId` | [Request pipeline › Problem details](docs/architecture/request-pipeline.md#problem-details) |
| Request logs carry a fixed field list: never the `Authorization` header, cookies, the query or the body | [Request pipeline › Request logging](docs/architecture/request-pipeline.md#request-logging) |
| Lost updates refused: the row version exposed as `ETag`, checked from `If-Match` | [ADR 0008](docs/decisions/0008-optimistic-concurrency-end-to-end.md) |
<!--#if (files)-->
| Stored files keyed by a random key, bound to their digest, inspected from their bytes, served through narrow signed grants | [Files › Inspection and scanning](docs/features/files.md#inspection-and-scanning), [ADR 0018](docs/decisions/0018-stored-files-are-keyed-granted-and-refused.md) |
<!--#endif-->
| A setting the host cannot use stops it at start, naming the key | [Deployment › Configuration and secrets](docs/guides/deployment.md#configuration-and-secrets) |
| Every dependency pinned and audited; any known advisory fails the build | [Build and CI › Supply chain](docs/guides/build-and-ci.md#supply-chain) |
| An unprivileged image exposing 8080 only | [Deployment › The image](docs/guides/deployment.md#the-image) |

## Before you deploy

Required:

1. **Terminate TLS upstream, enforce HTTPS and send HSTS there.** The API speaks plain HTTP and sends
   no HSTS by design: only the component that terminates TLS knows the domain's commitments.
2. **Configure `ReverseProxy`** when anything sits in front of the API. Until then every caller shares
   the proxy's rate-limit window. Never enable it with empty trust lists, which ASP.NET Core reads as
   trusting everyone; the API refuses to start that way.
<!--#if (localAuth)-->
3. **Supply `Jwt:Key`, `ConnectionStrings:Default` and the mail credentials from a secret manager**,
   the key with real entropy, rotated, given to the API alone.
<!--#endif-->
<!--#if (externalAuth)-->
3. **Supply `ConnectionStrings:Default` and the mail credentials from a secret manager**, and
   `Authentication:External:Authority` and `Audience` for your provider.
<!--#endif-->
<!--#if (publicApi)-->
3. **Supply `ConnectionStrings:Default` and the mail credentials from a secret manager.**
<!--#endif-->
4. **Set `Cors:AllowedOrigins`** to your origins and **narrow `AllowedHosts`** from `*`.
<!--#if (serverDatabase)-->
5. **Apply migrations as their own step**, with a principal that holds DDL rights, and give the API's
   runtime user none: [Deployment › Migrations at release](docs/guides/deployment.md#migrations-at-release).
6. **Grant the runtime user least privilege**: DML on its own tables, no DDL, no superuser.
<!--#endif-->
<!--#if (sqlite)-->
5. **Move to a server database first.** This project runs on SQLite, a development database, and the API
   refuses to start outside Development: [Deployment](docs/guides/deployment.md).
<!--#endif-->

Recommended:

<!--#if (localAuth)-->
- Export the telemetry and alert on it: the `429` and `401` rates show abuse, and the
  `jobs.*.iterations` counters going flat on the instance that runs the jobs show the background work
  has stopped.
<!--#endif-->
<!--#if (!localAuth)-->
- Export the telemetry and alert on it: the `429` rate shows abuse, and the `jobs.*.iterations`
  counters going flat on the instance that runs the jobs show the background work has stopped.
<!--#endif-->
- Set a log retention, and check redaction again after adding logging of your own.
- Back up the database and rehearse the restore.
- Put `/health/ready` behind a network policy if the reachability of your database is not public.
- Set `Concurrency:IfMatch` to `Required` once your clients send `If-Match`.
<!--#if (localAuth)-->
- Set `Passkey:ServerDomain` before the first passkey is registered: a passkey belongs to the domain
  it was created under.
<!--#endif-->

## Known gaps

<!--#if (localAuth)-->
- **Mail sent inside the request leaks, by timing, which addresses exist** on forgot-password and
  resend-confirmation: only the branch that owes a mail waits for the relay. Closing it means a
  durable queue.
- **Nothing bounds distributed credential stuffing.** Lockout is per account and the rate limit per
  address; one password against many accounts from many addresses trips neither.
- **Lockout is itself a denial of service**: with the defaults, five failures lock an account for
  fifteen minutes, so about twenty requests an hour keep a chosen account shut.
- **One signing key, no rotation, no `kid`**: replacing it ends every token at once, and no single
  access token can be revoked.
- **A rotation whose response is lost ends every session**, since its retry reads as a replay.
- **On `localhost`, web apps share one refresh-token cookie**, because cookies are not scoped by port.
- **The security log is not tamper-evident**: it lives beside the business data, writable by the same
  user.
<!--#endif-->
- **Domain events are delivered at most once, with no outbox.** A consumer whose effect cannot be
  re-derived later (mail, money, a third party) needs an outbox first:
  [ADR 0006](docs/decisions/0006-events-after-commit-without-an-outbox.md).
- **Nothing alerts when the background jobs stop.** `/health/ready` answers for HTTP only; alert on
  the job heartbeats instead.
- **Idempotency keys expire only where the jobs run.** With the jobs off everywhere, a completed key
  stays replayable and the table grows.
- **`If-Match` is optional by default**: a write without it overwrites whatever is stored.
- **Draining on `SIGTERM` is not proved by a test**; only the readiness flip is.
<!--#if (accounts && anyFeature)-->
- **A feature added without a deletion policy orphans its rows** when an account goes; the generator
  makes the policy a required choice: [ADR 0012](docs/decisions/0012-account-deletion-each-feature-decides.md).
<!--#endif-->
<!--#if (accounts && !anyFeature)-->
- **A feature added without a deletion policy orphans its rows** when an account goes; the generator
  makes the policy a required choice, and the first owner feature brings the deletion sweep with it.
<!--#endif-->
