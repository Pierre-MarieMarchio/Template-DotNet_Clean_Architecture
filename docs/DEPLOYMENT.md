# Deployment

This is the narrative behind [`deploy/kubernetes/`](../deploy/kubernetes/): what each manifest
assumes, and the reasoning three of its numbers share. It complements
[CONFIGURATION.md](CONFIGURATION.md), which documents every configuration key on its own terms —
this file is about how those keys interact with an orchestrator, not a second list of them.

Raw manifests, not a Helm chart. A template is meant to be read and changed, not parameterised
through a values file for values nobody has picked yet.

One limit on everything below, stated once rather than repeated. The manifests are checked
against the code they configure — every environment variable against the options class that
binds it, every port and probe path against `Program.cs` and the Dockerfile, every `secretKeyRef`
against `secret.example.yaml` — but they are **not** checked against a running cluster, because
this repository has none and applies nothing. Anything that depends on how a *controller*
behaves, rather than on what a manifest says, is reasoned from that controller's documentation
and is yours to confirm on first deploy. The HSTS section below is the one place where that
distinction currently changes the answer.

## The shutdown chain: three numbers that have to agree

`Src/Presentation/AppTemplate.Api/Common/Hosting/ShutdownHealthCheck.cs` turns
`/health/ready` unhealthy the instant graceful shutdown begins (`ApplicationStopping`), while
`/health` — liveness — stays healthy for as long as the process is exiting cleanly. Read that
file's own comment; it is the half of this mechanism that lives in code. The other half lives in
the orchestrator, and it is three separate values that only work as a set:

1. **`Shutdown:Timeout`** (`ShutdownOptions.cs`, default 30 s) — how long
   `AppTemplate.Api` waits for in-flight requests to finish once it starts stopping. Its own
   comment already ties it to Kubernetes: 30 s is chosen to match
   `terminationGracePeriodSeconds`'s own Kubernetes default, so the host is not still draining
   when the orchestrator stops waiting for it.
2. **`terminationGracePeriodSeconds`** (`api-deployment.yaml`) — how long the kubelet waits,
   in total, from the moment it starts terminating the pod to the moment it sends `SIGKILL`.
3. **`preStop`'s sleep** (`api-deployment.yaml`, `lifecycle.preStop`) — a delay inserted **before**
   `SIGTERM` is sent to the container at all.

**Which constrains which:** `preStop` exists to buy time for something this application cannot
see or control — how long it takes the Service's `Endpoints`/`EndpointSlices` update to
propagate through `kube-proxy` and any ingress controller in front of this Service, once
Kubernetes marks the pod `Terminating`. That propagation time is a fact about your cluster's
networking, not a number this template can choose for you; `preStop`'s sleep is set here to 5
seconds as a starting point that has to be measured against your actual ingress controller, the
same way `IdempotencyPurge:BatchSize` is a starting point against your actual ingestion rate
(CONFIGURATION.md). `Shutdown:Timeout` is kept at its own deliberately-tuned default (30 s) —
shrinking it to make room would undercut the very thing it exists to protect: a legitimately
slow request finishing instead of being cut off mid-flight. That leaves
`terminationGracePeriodSeconds` as the dependent value: it has to cover `preStop`'s sleep **plus**
`Shutdown:Timeout`, with a small margin for `SIGTERM` delivery and process exit —
`5 + 30 + 5 = 40`, which is what `api-deployment.yaml` sets. Raise `preStop`'s sleep because your
ingress controller is slower to converge, or raise `Shutdown:Timeout` because a legitimately
long request needs more room, and `terminationGracePeriodSeconds` has to move with whichever one
moved — it is never the number that moves first.

Get the order wrong — say, `preStop` and `Shutdown:Timeout` summing to more than
`terminationGracePeriodSeconds` — and the kubelet's `SIGKILL` lands while the application still
believes it has draining time left, truncating an in-flight request exactly the way an unplanned
crash would, on every rolling deploy rather than only during an incident.

**What this buys, precisely.** Kubernetes removes a `Terminating` pod from a Service's
`Endpoints` as soon as the pod is marked for deletion — that removal does not wait for this
application's readiness probe to fail, because a Terminating pod is pulled from load-balancing
regardless of what it currently reports. What it *does* wait for is every component that reads
`Endpoints` (`kube-proxy`'s rules, an ingress controller's own upstream list) to notice the
change and reprogram itself, and that takes real, non-zero time. `SIGTERM` — sent right after
`preStop` returns — is what makes Kestrel stop accepting new connections, almost immediately.
`preStop`'s sleep is the only thing standing between those two clocks: without it, `SIGTERM` can
arrive before every data-plane component has stopped sending this pod new work, and a caller
sees a connection refused instead of drained — a self-inflicted burst of failed requests on every
deploy, not just an unlucky one. The readiness flip is not what closes this particular race; it
is a second, faster signal for anything that actively polls `/health/ready` instead of only
watching `Endpoints` (a service-mesh sidecar, an ingress controller with its own upstream health
checks) — genuinely useful, but not a substitute for `preStop`.

`AppTemplate.Worker` has none of this. It has no Service and no ingress routing traffic to drain,
so there is nothing for `preStop` to buy time against — see the comment in
`worker-deployment.yaml` for what its own `terminationGracePeriodSeconds` is sized against
instead (the .NET Generic Host's own default shutdown timeout, which this host does not
override).

## Probes

| Probe | Path | Backed by |
|---|---|---|
| Liveness | `/health` | Zero checks (`Predicate = _ => false` in `Program.cs`) — answers "is the process up", nothing else |
| Readiness | `/health/ready` | The `database` and `shutdown` checks, both tagged `ready` |

Liveness never carries the shutdown check, on purpose: failing liveness during a clean shutdown
would ask the orchestrator to `SIGKILL` a process that is exiting correctly on its own, which is
strictly worse than letting it finish. Only readiness flips.

**Both probes are exempt from the rate limiter** (`DisableRateLimiting()` in `Program.cs`), and
that exemption is load-bearing, not a convenience. Behind a service mesh sidecar or a
`hostNetwork` ingress, the probe and real inbound traffic can share one source address and
therefore one rate-limit partition; a genuine traffic spike then answers the probe `429` too,
which the kubelet reads as failure on `/health` and the orchestrator reads as "kill it" — right as
the instance is already under load, cascading onto whatever replicas survive the resulting
restart. `deploy/kubernetes/api-deployment.yaml`'s probe `failureThreshold`s are chosen with the
ordinary case in mind (a database blip, not a shutdown), not with closing the shutdown race —
see the previous section for that.

`AppTemplate.Worker` has neither probe: see `worker-deployment.yaml` for why a liveness probe
needs an HTTP endpoint this host does not have, and a readiness probe needs traffic to gate that
does not exist here either.

## Port, user, and the absence of HTTPS redirection

The container listens on plain HTTP, port 8080 (`Src/Presentation/AppTemplate.Api/Dockerfile`),
and runs as the non-root `$APP_UID` the base image defines. `UseHttpsRedirection` is deliberately
absent from `Program.cs`: TLS terminates upstream, at the Ingress
(`deploy/kubernetes/ingress.yaml`), and a redirect installed in the application would answer the
orchestrator's own plain-HTTP probe with a `307` instead of a health status.

## HSTS is the Ingress's responsibility

The application deliberately sends no HSTS header: `max-age`, `includeSubDomains` and
`preload` are commitments about a whole domain that a process serving one path prefix cannot
make on its own, and the container does not even see the TLS connection the header is about.
`ingress.yaml` states the recommended starting point — a short `max-age`, no subdomains, no
preload — and it is the intent to carry forward: raise it only after confirming every host under
the domain actually serves TLS.

**But read that file as a statement of intent, not as a working configuration.** The four
`hsts`, `hsts-max-age`, `hsts-include-subdomains` and `hsts-preload` keys it carries under the
`nginx.ingress.kubernetes.io/` prefix are **ingress-nginx ConfigMap keys, not per-Ingress
annotations**. The controller has no annotation of those names, so it does not read them off an
`Ingress` object; it does not reject them either, because an unrecognised annotation is simply
ignored. The net effect is that the manifest's four lines do nothing at all and the controller's
own defaults apply instead — HSTS on, `max-age` of one year, `includeSubDomains` **enabled** —
which is the exact opposite of the 300 s the comment above them argues for, and it is a
commitment about your domain that cannot be withdrawn for a year once a browser has seen it.

Where the value actually lives: the ingress-nginx controller's own ConfigMap (the one named by
the controller Deployment's `--configmap` flag, typically `ingress-nginx-controller` in the
`ingress-nginx` namespace), whose `hsts-max-age`, `hsts-include-subdomains` and `hsts-preload`
keys apply to **every** Ingress that controller serves — which is why this template cannot ship
it: the file is cluster-wide and not ours to own. If you need HSTS per host rather than per
cluster, the per-Ingress route is a `configuration-snippet` (or, on a controller where snippets
are disabled, `more_set_headers` through a controller-level snippet), not these keys. Either way,
set it deliberately before the first browser sees the domain.

Per the limit stated at the top: this is read from ingress-nginx's documented vocabulary, not
observed against a cluster. Check the rendered header with `curl -I` against your own ingress
once, on the day you deploy; it is a one-line check and it is the only thing that settles what
your controller actually sends.

## `ReverseProxy`, or: what an Ingress silently breaks if you forget it

Putting `ingress.yaml` in front of `api-service.yaml` and stopping there does not merely leave a
feature off — it silently defeats the rate limiter. Without `ReverseProxy:Enabled`,
`X-Forwarded-For` is ignored and the limiter partitions on the immediate connection's own
address, which — behind any proxy — is the proxy's address for every caller. SECURITY.md's
phrasing is exact: **every caller in the world then shares one 10-request window**, and the
brute-force protection the limiter exists for does nothing.

`configmap-api.yaml` turns it on and sets `KnownNetworks` to the cluster's Pod CIDR, on the
assumption that the ingress controller's own pods are the one hop between a caller and this
Service. That assumption is the part of this manifest most likely to be wrong for your cluster —
verify the actual topology (is there a CDN or an external load balancer in front of the ingress
controller that also touches `X-Forwarded-For`? does the ingress controller run with
`hostNetwork` instead of a Pod IP?) before trusting the shipped value. Enabling this with an
empty trust list is not a shortcut to "it works" — `ReverseProxyOptions`' own validator refuses to
start in that state, because it is strictly worse than leaving the feature off: it would let any
caller forge its own partition key.

## `Database:MaxPoolSize` times replica count

`CONFIGURATION.md`'s own budget line is the one to internalise before touching replica counts in
either Deployment:

```
sum over every replica of (that replica's Database:MaxPoolSize)
  + PostgreSQL's own reserved connections
  <= max_connections
```

`api-deployment.yaml` ships 2 API replicas; `worker-deployment.yaml` ships 1 worker replica; both
ConfigMaps restate the shipped `Database:MaxPoolSize` of 20. That is `2 × 20 + 1 × 20 = 60`
connections against PostgreSQL's own default `max_connections` of 100 — inside the "4–5 replicas
total" room CONFIGURATION.md describes, with headroom left for a `psql` session or a monitoring
dashboard. **Raising either Deployment's `replicas` multiplies this number**; it is not an
independent scaling knob. The idempotency store's own extra connections (up to three per
in-flight idempotent write, per CONFIGURATION.md) count against this same budget too, not a
separate one — size in, not around.

## Secrets

### Reading them from a secret manager, which the template deliberately does not abstract

Azure Key Vault, AWS Secrets Manager, GCP Secret Manager and HashiCorp Vault all plug in as
**`IConfiguration` providers**, not as ports. That is the whole integration:

```csharp
// Before any module is composed, so that every validated options section sees the values.
builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
```

**There is no `ISecretStore` in this repository and there should not be.** A port here would be an
abstraction layered over one .NET already provides, and it would buy nothing: the configuration
system is already the seam, every section is already bound and validated at start-up, and a value
that arrives from a vault is indistinguishable — by design — from one that arrived from an
environment variable. Writing a port would mean every options class had two ways to be filled.

What the template does enforce is the part that actually matters, and it enforces it already: **no
secret is in tracked configuration**. Every secret-shaped value in every `appsettings.json` is an
empty string, each section is validated with `ValidateOnStart()`, and the process refuses to boot
rather than run half-configured. `deploy/kubernetes/secret.example.yaml` shows the shape without the
values and says, at the top, to generate the object from a real manager rather than write it by
hand. The one asymmetry worth knowing is that `Jwt__Key` reaches the API alone — see `SECURITY.md`.

Every secret-shaped value the template validates at startup — `ConnectionStrings:Default`,
`Jwt:Key`, the SMTP credentials — ships in `appsettings.json` as an empty string and fails
`ValidateOnStart()` if it stays that way, so a pod that is missing one of them does not start,
let alone serve traffic. `deploy/kubernetes/secret.example.yaml` shows the **shape** these
secrets take as Kubernetes objects — placeholder values only, referenced by both Deployments
through individual `secretKeyRef` entries rather than `envFrom`, so a container gets exactly the
keys it composes and nothing else. `AppTemplate.Worker` **does** receive `Email__UserName` /
`Email__Password`: it composes `AppTemplate.Infrastructure.Email` too, for `IReminderNotifier`'s
adapter — see below and `AppTemplate.Worker.csproj`. It receives `Storage__AccessKeyId` /
`Storage__SecretAccessKey` on the same reasoning: it composes the storage module for the file
loop's sweeps. Both may legitimately be empty, since the AWS SDK's own credential chain resolves
an instance role and the validator only refuses exactly one of the pair — see `SECURITY.md`.

It **does not** receive `Jwt__Key`, and that is the one asymmetry between the two Deployments.
The worker needs the `Jwt` *section* to exist, because it composes the identity module and
`JwtOptionsValidator` runs at startup; it never uses the *key*, because nothing in it signs or
verifies a token. `configmap-worker.yaml` supplies a self-describing placeholder instead, so a
compromise of the host that sends reminder mail does not hand over the ability to mint an access
token for any user. `Jwt__Issuer` and `Jwt__Audience` stay identical across both hosts — those two
are what the hosts genuinely have to agree about.

**`PasswordReset:ResetPasswordUrl`, `EmailChange:ConfirmEmailChangeUrl` and `Email:Host` are all
required by both hosts, not just the API.** `AppTemplate.Worker` composes
`AppTemplate.Infrastructure.Identity` for its reminder loop's `IUserProfilesService` as much as for
`IRefreshTokenMaintenanceService`'s adapter — see `docs/CONFIGURATION.md` for why moving the latter
would free this host of nothing — and that
module validates `PasswordReset`, `EmailConfirmation` and `EmailChange` at startup regardless of
which host loaded it — leave any of those three URLs unset on the worker and it refuses to start,
the same as the API would. It separately composes `AppTemplate.Infrastructure.Email` for
`IReminderNotifier`'s adapter (a due reminder is rung by mail), which validates `Email` (SMTP) the
same way — a deployment that never uses reminders still has to point this host at a working relay
for it to boot. `configmap-worker.yaml` carries all four sections for exactly this reason; see
docs/CONFIGURATION.md for the full accounting of what each host validates and why.

**A separate secret, and a separate database principal, for the migration Job.** SECURITY.md is
explicit that the application's runtime credentials should hold DML rights only, and that DDL
belongs to a migration-time principal that exists for the duration of the deployment.
`secret.example.yaml` therefore ships two `Secret` objects, not one:
`app-template-secrets` (DML, read by both Deployments) and
`app-template-migration-db-credentials` (DDL, read only by `migration-job.yaml`). Folding them
into one object would put every pod one credential away from being able to alter its own schema.

## Applying migrations

Migrations run as an explicit step, never inside the serving process, and a Kubernetes `Job` is
exactly the right shape for that step: it runs once, to completion, with its own credentials, and
its success or failure is a thing the cluster records rather than a line in a pod's log.
`migration-job.yaml` is that Job — it runs the self-contained `efbundle` executable
`.github/workflows/release.yml`'s `migration-bundle` job already produces, against the
DDL-capable principal above. Two things this
manifest assumes but does not itself provide:

- **An image to run it in.** The release workflow uploads `efbundle` as a plain workflow
  artifact today, not a container image — packaging it (a base image, a non-root user, an
  `ENTRYPOINT` pointing at the bundle) is a pipeline change this manifest names but does not
  make.
- **Ordering.** `kubectl apply -f deploy/kubernetes/` does not wait for a `Job` to reach
  `condition: complete` before touching a `Deployment`. Run the migration, wait for it, then roll
  the Deployments — see [`deploy/kubernetes/README.md`](../deploy/kubernetes/README.md) for the
  exact sequence. Skipping that ordering is silent until the day a pod starts against a schema
  its code does not expect; wiring `/health/ready`'s database check into an actual alert is the
  cheapest place to catch it after the fact.

## The worker is its own Deployment, on purpose

`AppTemplate.Worker` answers no HTTP request. Three `BackgroundService`s run on their own
timers, and nothing else: `MaintenanceBackgroundService`, `ReminderBackgroundService`, and
`FileBackgroundService` — which alone carries three timers, because the file feature's two
sweeps and its deposit inspection have costs orders of magnitude apart (`configmap-worker.yaml`
sets all three intervals under `FileWorker__`). `worker-deployment.yaml`
therefore has no matching `Service` and no `Ingress` (there is no traffic to route), and no
readiness probe (readiness answers "safe to route traffic to", which does not apply to a process
nothing calls). It is still a replica against the same `Database:MaxPoolSize` budget as the API —
see above.

It shares `app-template-secrets` with the API, but not the whole of it: five `secretKeyRef`
entries — `ConnectionStrings__Default`, `Email__UserName`, `Email__Password`,
`Storage__AccessKeyId`, `Storage__SecretAccessKey` — against the API's six. `Jwt__Key` is the
sixth, and it is the one this pod does not mount; `worker-deployment.yaml` says so in a comment
at the point where the entry would otherwise sit. Copy that omission into any manifest you write
from this one. The section above explains why, and `SECURITY.md` explains what mounting it here
would cost.
