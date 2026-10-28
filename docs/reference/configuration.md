# Configuration

Every setting the API reads, one table per section; a section a team adds gets its own, which a
test compares with the options classes. Every section is validated at start-up: a missing or
out-of-range value stops the host and names the key. "Required" means start-up fails without it;
a default applies when the key is absent. `Src/Presentation/AppTemplate.Api/appsettings.json`
holds no secret, and a key `A:B` is the environment variable `A__B`. Loading and secrets are in
[Deployment](../guides/deployment.md).

The Default column is what a deployment gets. `appsettings.Development.json` on the host, and
`.env.example`, from which Compose's `.env` starts, carry development values over it.
<!--#if (localAuth)-->
`.env.example`, for one, locks an account out after 10 failures for 1 minute, where the default is
5 failures for 15 minutes.
<!--#endif-->

## Database

### `ConnectionStrings`

| Key | Type | Default | Notes |
|---|---|---|---|
<!--#if (postgreSql)-->
| `Default` | string | — | **Required.** The one connection string, Npgsql format (`Host=…;Database=…;Username=…;Password=…`), used by every context. A blank value fails the DI registration. |
<!--#endif-->
<!--#if (sqlServer)-->
| `Default` | string | — | **Required.** The one connection string, SqlClient format (`Server=…;Database=…;User Id=…;Password=…`), used by every context. A blank value fails the DI registration. |
<!--#endif-->
<!--#if (sqlite)-->
| `Default` | string | — | **Required.** The one connection string, SQLite format (`Data Source=<file>`, relative to the directory the API starts in), used by every context. A blank value fails the DI registration. |
<!--#endif-->

### `Database`

| Key | Type | Default | Notes |
|---|---|---|---|
<!--#if (postgreSql)-->
| `MaxPoolSize` | int | `20` | 1–500. Npgsql's `Maximum Pool Size`, per instance. The sum over every instance, plus PostgreSQL's reserved connections, must stay under `max_connections`. |
<!--#endif-->
<!--#if (sqlServer)-->
| `MaxPoolSize` | int | `20` | 1–500. SqlClient's `Max Pool Size`, per instance. The sum over every instance must stay under the server's connection limit. |
<!--#endif-->
<!--#if (sqlite)-->
| `MaxPoolSize` | int | `20` | 1–500. Has no effect on SQLite, which has no server-side connection limit. |
<!--#endif-->
| `CommandTimeoutSeconds` | int | `30` | 1–300. Per command. `RequestTimeouts:Default` must stay above this times the retries. |

<!--#if (localAuth)-->
## Identity and tokens

### `Jwt`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Key` | string | `""` | **Required, secret.** HS256 key, 32 bytes at least. Outside Development, a key starting with `dev-only` is refused. |
| `Issuer` | string | `""` | **Required.** |
| `Audience` | string | `""` | **Required.** |
| `RequireHttpsMetadata` | bool | `true` | `false` for a local host only. |
| `AccessTokenLifetimeInMinutes` | int | `15` | 1–1440. An access token cannot be revoked before it expires. |

### `RefreshToken`

| Key | Type | Default | Notes |
|---|---|---|---|
| `LifetimeInDays` | int | `7` | 1–90. |
| `RetentionInDays` | int | `7` | 1–90. How long an expired grant's row is kept, so a late replay is still recognised, before the maintenance job purges it. |

### `Identity`

| Key | Type | Default | Notes |
|---|---|---|---|
| `PasswordRequiredLength` | int | `12` | 8–256; 8 is a floor configuration cannot lower. The only password policy. |
| `PasswordRequiredUniqueChars` | int | `4` | At least 1. |
| `PasswordRequireDigit` | bool | `true` | |
| `PasswordRequireLowercase` | bool | `true` | |
| `PasswordRequireUppercase` | bool | `true` | |
| `PasswordRequireNonAlphanumeric` | bool | `true` | |
| `LockoutEnabled` | bool | `true` | What bounds password guessing. |
| `LockoutMaxFailedAccessAttempts` | int | `5` | 1–20. |
| `LockoutDurationInMinutes` | int | `15` | At least 1. |
| `RequireConfirmedEmail` | bool | `true` | `false` is refused. |
| `RequireUniqueEmail` | bool | `true` | `false` is refused: the address is the identifier. |

### `TwoFactor`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ChallengeLifetime` | timespan | `00:05:00` | 1–30 minutes. |
| `RecoveryCodeCount` | int | `10` | 1–20. |
| `MaxChallengeAttempts` | int | `5` | 1–20. Wrong codes one challenge tolerates: the only bound on guessing a code. |
| `Issuer` | string | `AppTemplate` | Not blank. The label an authenticator app shows. |

### `Passkey`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ServerDomain` | string | the request's host | The WebAuthn relying-party id: a bare domain. Changing it invalidates every passkey. |
| `CeremonyLifetime` | timespan | `00:05:00` | 1–30 minutes. How long an options call's state can be redeemed. |

### `ExternalIdentity`

| Key | Type | Default | Notes |
|---|---|---|---|
| `KeySetLifetime` | timespan | `00:15:00` | 1 minute–24 hours. How long a provider's signing keys are cached. |
| `Providers:<n>:Name` | string | — | The name a client posts as `provider`. No provider listed refuses every external sign-in; no secret belongs here. |
| `Providers:<n>:Issuers:<m>` | string | — | At least one. |
| `Providers:<n>:Audiences:<m>` | string | — | At least one: this project's client id at the provider. |
| `Providers:<n>:JwksUri` | string | — | Absolute `https`. Exactly one of this and `MetadataAddress`. |
| `Providers:<n>:MetadataAddress` | string | — | Absolute `https`. OpenID Connect discovery. |

### `IdentityTokens`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Lifespan` | timespan | `1.00:00:00` | 5 minutes–30 days. The email-confirmation token. |

### `SecurityEventLog`

| Key | Type | Default | Notes |
|---|---|---|---|
| `RetentionInDays` | int | `90` | 1–3650. Then the maintenance job purges the event, the deleted accounts' included. |
| `PurgeBatchSize` | int | `1000` | 1–100000. Rows per delete statement. |

### `IdentitySeed`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Creates an administrator at start-up. Throws outside Development. |
| `AdminEmail` | string | `""` | Required when enabled. |
| `AdminPassword` | string | `""` | **Required when enabled, secret.** No default. |

<!--#endif-->
<!--#if (externalAuth)-->
## External authentication

### `Authentication:External`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Authority` | string | `""` | **Required.** The provider's issuer URL; its discovery document supplies the keys. |
| `Audience` | string | `""` | **Required.** This API's identifier at the provider. |
| `RequireHttpsMetadata` | bool | `true` | `false` for a provider on a developer's machine only. |
| `SubjectClaim` | string | `sub` | Mapped to an internal owner id, never used as one. |
| `EmailClaim` | string | `email` | Kept for notifications, unless `email_verified` is present and not `true`. |
| `NameClaim` | string | `name` | Kept for notifications. |
| `Roles:Source` | `Claim` or `Local` | `Local` | Roles from the token, or from `external_identity.RoleAssignments`. |
| `Roles:ClaimPath` | string | — | **Required when `Source` is `Claim`.** A dot path: `roles`, `realm_access.roles`, or a namespaced claim matched whole. |
| `Roles:AdministratorRole` | string | `Admin` | The role the `Administrator` policy requires. |

<!--#if (presentationBlazorServer)-->
The presentation project reads this section too, as the provider it signs people in with.

### `Authentication:External:Interactive`

The presentation project's client at the provider, for the OpenID Connect code flow; the API never reads it.

| Key | Type | Default | Notes |
|---|---|---|---|
| `ClientId` | string | `""` | **Required.** The client the provider registered for the presentation project. |
| `ClientSecret` | string | — | Its secret, for a confidential client; a public client sends none and relies on PKCE. |

<!--#endif-->
<!--#endif-->
<!--#if (presentationBlazorServer && !localAuth)-->
## Presentation project

### `DataProtection`

Read by the presentation project alone, for the keys that protect its session cookie and its forms.

| Key | Type | Default | Notes |
|---|---|---|---|
| `KeysDirectory` | string | — | **Required outside Development.** An absolute folder every instance shares and that outlives a restart; Compose mounts a volume at `/data-protection-keys`. |

<!--#endif-->
<!--#if (presentationAvalonia)-->
## Presentation project

### `Api`

Read by the desktop app alone, from the `appsettings.json` beside it, its environment variables
(`Api__BaseAddress`) or its command line (`--Api:BaseAddress=<address>`); the API never reads it.

| Key | Type | Default | Notes |
|---|---|---|---|
| `BaseAddress` | absolute URI | `http://localhost:8080/` | **Required.** The API's `http` or `https` address, the API Compose publishes by default; the app refuses to start without it. |

<!--#if (externalAuth)-->
### `SignIn`

Read by the desktop app alone, as the public client it signs its user in with at the provider, from
the same places as `Api` (`SignIn__ClientId`, `--SignIn:ClientId=<id>`); the API never reads it.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Authority` | absolute URI | `""` | **Required.** The provider's issuer URL, the one `Authentication:External:Authority` names; `https` unless `RequireHttpsMetadata` is `false`. The app refuses to start without it. |
| `ClientId` | string | `""` | **Required.** The public client the provider registered for the app: no secret, the redirect URI `http://127.0.0.1/` on any port. |
| `Scope` | string | `openid profile email offline_access` | Must include `openid`. `offline_access` asks for the refresh token that keeps the session past the first access token; the API's own scope goes here when the provider issues its audience that way. |
| `RequireHttpsMetadata` | bool | `true` | `false` for a provider on a developer's machine only. |

<!--#endif-->
<!--#endif-->
<!--#if (files)-->
## Files

### `Files`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ApiUploadMaxBytes` | long | `10485760` | 1 KiB–30 MiB. Largest file uploaded through `POST /api/v1/files/uploads`. |
| `Categories:<name>:AllowedMediaTypes` | string[] | — | At least one exact `type/subtype`. At least one category; `<name>` is a lower-case slug of 1–64 characters. |
| `Categories:<name>:MaxSizeInBytes` | long | — | **Required.** 1 byte–5 GiB. |
| `Categories:<name>:Scan` | bool | `false` | Refused while `Files:Scanning:Enabled` is false. |
| `Categories:<name>:VerifySignature` | bool | `true` | Leading bytes must match the declared type. Markup is refused either way. |
| `Categories:<name>:Visibility` | string | `Private` | `Private` or `Authenticated` (any signed-in caller reads an available file's content). |
| `Categories:<name>:UploadModes` | string[] | — | At least one of `Direct` and `Api`. `Direct` alone is refused with the `FileSystem` provider. |
| `Categories:<name>:Quota:Mode` | string | `Strict` | `Strict` (a lock on the owner and category) or `Indicative`. |
| `Categories:<name>:Quota:MaxFiles` | int | `1000` | Per owner. |
| `Categories:<name>:Quota:MaxBytes` | long | `10737418240` | Per owner, pending files at their declared size. |
| `Categories:<name>:Quota:MaxPendingUploads` | int | `20` | Per owner. |

### `Files:Storage`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Provider` | string | `S3` | `S3` or `FileSystem`. Only the chosen provider's section is bound. |

### `Files:Storage:S3`

| Key | Type | Default | Notes |
|---|---|---|---|
| `BucketName` | string | `""` | **Required.** Exclusive to this feature: the orphan sweep deletes what no row names. |
| `Region` | string | `us-east-1` | |
| `Endpoint` | string | `""` | Empty for AWS S3; set for SeaweedFS, MinIO, R2. |
| `PublicEndpoint` | string | `Endpoint` | The name a client resolves, signed into every URL. |
| `ForcePathStyle` | bool | `false` | `true` for a store addressed by host and port. |
| `AccessKeyId` | string | `""` | Empty in production: the SDK resolves an instance role. |
| `SecretAccessKey` | string | `""` | Secret. Exactly one of the pair is refused. |
| `AllowInsecureTransport` | bool | `false` | For a plain-HTTP local endpoint only. |
| `MaxGrantLifetime` | timespan | `00:30:00` | How long a signed URL is a bearer right. |

### `Files:Storage:FileSystem`

| Key | Type | Default | Notes |
|---|---|---|---|
| `RootPath` | string | `""` | **Required** for this provider, absolute, exclusive to this feature. |

### `Files:Scanning`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | `false` registers a scanner that finds nothing. |
| `Host` | string | `""` | **Required when enabled.** The `clamd` daemon. |
| `Port` | int | `3310` | |
| `MaxScannableBytes` | long | `26214400` | At most the daemon's `StreamMaxLength`; larger content is refused. |

<!--#endif-->
## Mail

### `Email:Transport`

| Value | Adapter | Reads |
|---|---|---|
| `Smtp` (default) | MailKit over SMTP | the SMTP keys of `Email` |
| `Postmark` | Postmark's HTTP API | `Postmark` |
| any other | none: start-up stops | |

### `Email`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Host` | string | `""` | **Required for `Smtp`.** |
| `Port` | int | `587` | 1–65535. |
| `Security` | enum | `StartTls` | `None`, `Auto`, `SslOnConnect`, `StartTls`, `StartTlsWhenAvailable`. Against a host other than loopback, `None`, `Auto` and `StartTlsWhenAvailable` need `AllowInsecureTransport`. |
| `AllowInsecureTransport` | bool | `false` | Explicit opt-in to an unencrypted relay, such as Mailpit under Compose. |
| `UserName` | string | `null` | |
| `Password` | string | `null` | Secret. |
| `FromAddress` | string | `""` | **Required.** A mailbox address. |
| `FromName` | string | `""` | |

### `Postmark`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ServerToken` | string | `""` | **Secret.** Read when `Email:Transport` is `Postmark`. |
| `ApiBaseUrl` | string | `https://api.postmarkapp.com/` | Absolute; plain `http` only against loopback. |
| `MessageStream` | string | `outbound` | Not blank. |

### `Localization`

| Key | Type | Default | Notes |
|---|---|---|---|
| `DefaultCulture` | string | `en` | A culture tag: the language of a mail when no request names one. |

<!--#if (localAuth)-->
### `EmailConfirmation`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConfirmEmailUrl` | URI | — | **Required.** The confirmation page: absolute, no fragment, reachable from a browser. |

### `PasswordReset`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ResetPasswordUrl` | URI | — | **Required.** The reset page, same rules. |
| `TokenLifespan` | timespan | `01:00:00` | 5 minutes–1 day. |

### `EmailChange`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConfirmEmailChangeUrl` | URI | — | **Required.** The page confirming a new address, same rules. |
| `TokenLifespan` | timespan | `01:00:00` | 5 minutes–1 day. |

The three pages follow the contract in [Accounts](../features/accounts.md#the-pages-the-mailed-links-open).

<!--#endif-->
## The HTTP boundary

### `Cors`

| Key | Type | Default | Notes |
|---|---|---|---|
| `AllowedOrigins` | string[] | `[]` | Exact origins, or `*`. Empty allows no cross-origin call. Credentials are never allowed. |

### `ReverseProxy`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Off trusts no forwarding header. On with both lists empty is refused. |
| `KnownProxies` | string[] | `[]` | Literal addresses of the hops in front. |
| `KnownNetworks` | string[] | `[]` | CIDR blocks, given by their network address. |
| `ForwardLimit` | int | `1` | Exactly the number of proxies in front. |

### `SecurityHeaders`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ContentSecurityPolicy` | string | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | Sent on every API response. |

### `RequestLimits`

| Key | Type | Default | Notes |
|---|---|---|---|
<!--#if (files)-->
| `MaxRequestBodyBytes` | long | `65536` | 1024–31457280. Above it, `413`. `POST /api/v1/files/uploads` takes `Files:ApiUploadMaxBytes` plus 16 KiB instead. |
<!--#endif-->
<!--#if (!files)-->
| `MaxRequestBodyBytes` | long | `65536` | 1024–31457280. Above it, `413`. |
<!--#endif-->

### `RequestTimeouts`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Default` | timespan | `00:05:00` | 1 second–1 hour. Longer than a database call's worst case with its retries. |

### `RateLimiting`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Window` | timespan | `00:01:00` | 1 second–1 day. |
<!--#if (localAuth)-->
| `AuthenticationPermitLimit` | int | `10` | Per window and client address, on the sign-in routes. Per instance. |
<!--#endif-->
| `GlobalPermitLimit` | int | `300` | Per window and client address, on every request. Per instance. |

### `ProblemTypes`

| Key | Type | Default | Notes |
|---|---|---|---|
| `BaseUri` | string | `https://apptemplate.example/problems` | Absolute. Prefixes the `type` of every problem document; set it to a domain you own. |

## Correctness mechanisms

### `Concurrency`

| Key | Type | Default | Notes |
|---|---|---|---|
| `IfMatch` | `Optional` or `Required` | `Optional` | `Required` refuses a write without `If-Match` with `428`. |

### `Idempotency`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `true` | `false` makes `Idempotency-Key` inert. |
| `Retention` | timespan | `1.00:00:00` | Over 0, at most 30 days. How long a completed response is replayable; the purge enforces it. |
| `ClaimLease` | timespan | `00:15:00` | Over 0, at most `Retention`. How long an unfinished claim blocks a retry. Keep it above `RequestTimeouts:Default`. |
| `MaxKeyLength` | int | `128` | 1–512. |
| `MaxStoredResponseBytes` | int | `8192` | At least 1. A larger response is stored without its body and cannot be replayed. |

### `IdempotencyPurge`

| Key | Type | Default | Notes |
|---|---|---|---|
| `BatchSize` | int | `1000` | 1–100000. Rows per round trip of the expired-key purge. |

## Lifecycle and observability

### `Shutdown`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Timeout` | timespan | `00:00:30` | Over 0, at most 10 minutes. How long in-flight requests drain. |

### `OpenTelemetry`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Off registers no instrumentation and no exporter. |
| `OtlpEndpoint` | string | `""` | For example `http://collector:4317`. |
| `OtlpProtocol` | string | `Grpc` | `Grpc` or `HttpProtobuf`. |
| `ServiceName` | string | the host's assembly name | Blank is refused; remove the key instead. |
| `TracesSamplingRatio` | double | `1.0` | Over 0, at most 1. |

## Background jobs

### `Jobs`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `true` | Whether this instance runs jobs. `false` binds no job section. |

### `Jobs:Maintenance`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Interval` | timespan | `01:00:00` | 1 second–1 day. One timer for every maintenance task. |
| `PurgeExpiredIdempotencyKeysEnabled` | bool | `true` | |

<!--#if (localAuth)-->
### `Jobs:Maintenance:PurgeExpiredRefreshTokensEnabled`

| Key | Type | Default | Notes |
|---|---|---|---|
| `PurgeExpiredRefreshTokensEnabled` | bool | `true` | A key of `Jobs:Maintenance`, read by authentication's own options. |

### `Jobs:Maintenance:PurgeExpiredSecurityEventsEnabled`

| Key | Type | Default | Notes |
|---|---|---|---|
| `PurgeExpiredSecurityEventsEnabled` | bool | `true` | A key of `Jobs:Maintenance`, read by authentication's own options. |

<!--#endif-->
<!--#if (anyFeature)-->
### `Jobs:Maintenance:SweepDeletedAccountsDataEnabled`

| Key | Type | Default | Notes |
|---|---|---|---|
| `SweepDeletedAccountsDataEnabled` | bool | `true` | A key of `Jobs:Maintenance`. The guarantee behind account deletion: keep it on. |

<!--#endif-->
<!--#if (reminders)-->
### `Jobs:Reminders`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Interval` | timespan | `00:01:00` | 1 second–1 hour. |
| `Enabled` | bool | `true` | |

<!--#endif-->
<!--#if (files)-->
### `Jobs:Files`

| Key | Type | Default | Notes |
|---|---|---|---|
| `InspectDepositedFilesInterval` | timespan | `00:01:00` | 10 seconds–1 hour. A latency users feel. |
| `InspectDepositedFilesEnabled` | bool | `true` | Off, no upload ever becomes readable. |
| `PurgeAbandonedRegistrationsInterval` | timespan | `01:00:00` | |
| `PurgeAbandonedRegistrationsEnabled` | bool | `true` | |
| `ReclaimOrphanedContentInterval` | timespan | `12:00:00` | |
| `ReclaimOrphanedContentEnabled` | bool | `true` | Keep it on: without it, deleted files' bytes stay. |

<!--#endif-->
## Beyond the sections

### Not configurable

Constants in code; to vary one per environment, promote it to an options class with a validator.

| Behaviour | Value | Where |
|---|---|---|
| Outbound HTTP | 10 s per attempt, 30 s in all, 3 retries of safe verbs | `AddOutboundHttp`, [Shared mechanisms](../architecture/shared-mechanisms.md#outbound-http) |
| `Cache-Control` on reads | `private, no-cache` | `CacheHeaderExtensions` |
| Readiness checks | the database, the shutdown state | `HealthEndpoints` |
| Largest `search` | 100 characters | `SearchTerm.MaxLength` |
| Largest cursor | 512 characters | `Cursor.MaxEncodedLength` |
| Page size, sort fields | per feature | each feature's `ICollectionPolicy` |
<!--#if (localAuth)-->
| Refresh token | 32 random bytes, SHA-256 hash | `SessionTokensService` |
<!--#endif-->
<!--#if (todoLists)-->
| Items per list, tags per item | 500, 20 | `TodoList.MaxItems`, `TodoItem.MaxTags` |
<!--#endif-->
<!--#if (files)-->
| Largest file | 5 GiB | `FileSize.MaxBytes` |
<!--#endif-->
<!--#if (tagging)-->
| Used-tag list cache | 1 minute | `UsedTagsCache.Lifetime` |
<!--#endif-->

### Standard ASP.NET Core keys

| Key | Notes |
|---|---|
| `Logging:LogLevel:*` | Levels per category; the output is JSON on the console. |
| `AllowedHosts` | Host filtering; `*` by default. |
<!--#if (localAuth)-->
| `ASPNETCORE_ENVIRONMENT` | `Development` loads `appsettings.Development.json`, serves OpenAPI and Scalar, migrates at start-up and allows the identity seed. |
<!--#endif-->
<!--#if (!localAuth)-->
| `ASPNETCORE_ENVIRONMENT` | `Development` loads `appsettings.Development.json`, serves OpenAPI and Scalar and migrates at start-up. |
<!--#endif-->
| `ASPNETCORE_HTTP_PORTS` | `8080` in the container. Do not also set `ASPNETCORE_URLS`, which wins. |
