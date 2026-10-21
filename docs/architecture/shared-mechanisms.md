# Shared mechanisms

A feature composes these mechanisms without writing them: a cache, an outbound HTTP budget, a clock,
and the answer to who is calling. Each is installed once, by the host or a module, and none can be
switched off.

## Cache

`ICacheStore` caches a value a use case asks for, under a key the caller chooses and for a lifetime
the caller states:

```csharp
var summary = await cache.GetOrCreateAsync(
    $"widget-summary:{ownerId}",
    token => queries.SummariseForOwnerAsync(ownerId, token),
    TimeSpan.FromMinutes(1),
    cancellationToken);
```

`AddCacheStore()` backs it with `HybridCache`, in process. A deployment that wants a shared second
level registers an `IDistributedCache` beside that call, and no caller changes. There is no
configuration section: a lifetime is a decision about one answer, not about a deployment.

Only an answer whose staleness is harmless is cached. Nothing that decides an authorisation, enforces
a bound, becomes an `ETag` or tells something else what to delete goes through the cache: a cached
bound is multiplied by the number of processes holding a copy. An entry may vanish before its
lifetime ends, so a caller is correct when it does; the port promises to be faster, never to
remember. A key names the owner of what it holds.

<!--#if (tagging)-->
The tags an owner has already used are cached this way, under the key and for the one-minute
lifetime `UsedTagsCache` decides: a suggestion a minute out of date costs a tag typed in full, and
whatever changes an owner's tags drops the entry.
<!--#endif-->

Responses are not cached. Every read is a caller's own behind default-deny authorisation, so a cache
keyed on the request could serve one caller's data to another; the HTTP layer offers revalidation
instead ([caching headers](http-api.md#caching-headers)).

## Outbound HTTP

`AddOutboundHttp()` installs one resilience policy on `IHttpClientFactory`'s defaults, so every client
any module registers inherits it in every host:

| | Value |
|---|---|
| Timeout per attempt | 10 s |
| Timeout for the whole call, retries included | 30 s |
| Retries | up to 3, exponential with jitter, on `GET`, `HEAD`, `OPTIONS` and `TRACE` only |
| Circuit breaker, concurrency limit | the standard resilience handler's defaults |

The values are not configuration. The 30 s total is bound to the five minutes of
`RequestTimeouts:Default`: an outbound call runs inside an inbound request, so the enclosing budget
must be the longer one. If either number moves, re-read both.

Retries are an allow-list of the safe verbs. `PUT` and `DELETE` are idempotent by specification, but
that promise belongs to the server at the other end, and a default applies to servers nobody here
controls. Relax it for one client whose server you know; never widen the default.

Two rules close the escapes: `NoType_ConstructsItsOwnHttpClient`, since a client built with `new`
never meets the factory, and `EveryHost_InstallsTheOutboundPolicy`. The Postmark transport of the
email module is a client of this budget.
<!--#if (localAuth)-->
So is the directory that fetches an external provider's signing keys.
<!--#endif-->
<!--#if (externalAuth)-->
So is the backchannel that reads the identity provider's metadata and keys.
<!--#endif-->
<!--#if (files)-->
The object store's SDK and the malware scanner's socket are out of the factory's reach and state
their own budgets.
<!--#endif-->

## The clock

`IDateTimeProvider.UtcNow` is the one clock. It is registered once, reads UTC, and every stored
instant is UTC: a column mixing local and UTC values makes every comparison silently wrong. A use
case reads the clock and passes `now` to the aggregate, which never reads time itself.

In tests, the in-memory module replaces it with a fixed clock that moves only when told to, and
refuses to move backwards.

## Current user and audit actor

Two ports answer two different questions, and the host answers both.

| Port | Question | In a request | In a background job |
|---|---|---|---|
| `ICurrentUser` | who is calling? | the authenticated principal, or no one | `NoCallerCurrentUser`, which throws when asked |
| `IAuditActor` | whom do the audit columns name? | the caller | nobody: the stamps are null |

A use case that needs an owner calls `RequireUserId()`, which answers `auth.required` when the request
is anonymous. A background pass has no request at all, so pretending its caller were merely
anonymous would let a use case that needs an owner proceed as though one existed; the background
`ICurrentUser` throws instead. The audit actor can answer, nobody, so a write from a job is stamped
with no author rather than failing.

A job opens each pass with `CreateBackgroundScope()`, which marks the scope so both ports answer the
background way. `EveryBackgroundService_OpensItsScopeAsABackgroundScope` holds it. A process with no
requests at all composes `AddNoCallerIdentity()` from `AppTemplate.Presentation.Core`.

`ICurrentAccessToken` tells a use case when the presented access token was issued, for an operation
that demands a recent sign-in.

## Decisions

- [0001. Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): foundations shipped before use, and `HybridCache` in process.
- [0011. Jobs run in the API under a lease](../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
