# Deployment

This is the narrative behind [`deploy/kubernetes/`](../deploy/kubernetes/): what each manifest
assumes, and the reasoning three of its numbers share. It complements
[CONFIGURATION.md](CONFIGURATION.md), which documents every configuration key on its own terms —
this file is about how those keys interact with an orchestrator, not a second list of them.

Raw manifests, not a Helm chart. A template is meant to be read and changed, not parameterised
through a values file for values nobody has picked yet.

## The shutdown chain: three numbers that have to agree

`Src/Presentation/AppTemplate.Api/Common/Lifecycle/ShutdownHealthCheck.cs` turns
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

`docs/adr/0012` is the full record; the short version is that `max-age`, `includeSubDomains` and
`preload` are commitments about a whole domain that a process serving one path prefix cannot
make on its own, and the container does not even see the TLS connection the header is about.
`ingress.yaml` carries the header instead, with the ADR's own recommended starting point (a
short `max-age`, no subdomains, no preload) — raise it only after confirming every host under the
domain actually serves TLS.

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

Every secret-shaped value the template validates at startup — `ConnectionStrings:Default`,
`Jwt:Key`, the SMTP credentials — ships in `appsettings.json` as an empty string and fails
`ValidateOnStart()` if it stays that way, so a pod that is missing one of them does not start,
let alone serve traffic. `deploy/kubernetes/secret.example.yaml` shows the **shape** these
secrets take as Kubernetes objects — placeholder values only, referenced by both Deployments
through individual `secretKeyRef` entries rather than `envFrom`, so a container gets exactly the
keys it composes and nothing else. `AppTemplate.Worker` **does** receive `Email__UserName` /
`Email__Password`: it composes `AppTemplate.Infrastructure.Email` too, for `IReminderNotifier`'s
adapter — see below and `AppTemplate.Worker.csproj`.

**`PasswordReset:ResetPasswordUrl`, `EmailChange:ConfirmEmailChangeUrl` and `Email:Host` are all
required by both hosts, not just the API.** `AppTemplate.Worker` composes
`AppTemplate.Infrastructure.Identity` for `IRefreshTokenMaintenance`'s sole adapter, and that
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

`docs/adr/0009` decided migrations run as an explicit step, never inside the serving process, and
names a Kubernetes `Job` as exactly the right shape for that step. `migration-job.yaml` is that
Job: it runs the self-contained `efbundle` executable `.github/workflows/release.yml`'s
`migration-bundle` job already produces, against the DDL-capable principal above. Two things this
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
  cheapest place to catch it after the fact, per the ADR's own consequence.

## The worker is its own Deployment, on purpose

`AppTemplate.Worker` answers no HTTP request — `MaintenanceBackgroundService` and
`ReminderBackgroundService` run on their own timers, nothing else. `worker-deployment.yaml`
therefore has no matching `Service` and no `Ingress` (there is no traffic to route), and no
readiness probe (readiness answers "safe to route traffic to", which does not apply to a process
nothing calls). It still shares `app-template-secrets` for the database connection and signing
key, and it is still a replica against the same `Database:MaxPoolSize` budget as the API — see
above.
