# Kubernetes manifests

Raw manifests, no Helm chart — that is a deliberate choice, not a placeholder for one. The
narrative — why each value is what it is, and the three durations that have to agree with each
other — lives in [`docs/DEPLOYMENT.md`](../../docs/DEPLOYMENT.md). Read that first; the comments
here assume it.

## Files

| File | What it is |
|---|---|
| `configmap-api.yaml` | Non-secret configuration for `AppTemplate.Api` |
| `configmap-worker.yaml` | Non-secret configuration for `AppTemplate.Worker` |
| `secret.example.yaml` | The **shape** of the secrets both hosts need — placeholder values, never real ones |
| `api-deployment.yaml` | The API: probes, shutdown handling, resource shape |
| `api-service.yaml` | `ClusterIP` in front of the API pods |
| `ingress.yaml` | TLS termination, HSTS, routing to the API service |
| `worker-deployment.yaml` | The maintenance/reminder worker: no Service, no Ingress, no readiness probe |
| `migration-job.yaml` | Applies pending EF Core migrations as a one-off `Job`, before either `Deployment` rolls |

## Apply order

`kubectl apply -f deploy/kubernetes/` does not order these against each other — it applies
whatever it finds in whatever order it lists the directory, and does not wait for a `Job` to
finish before touching a `Deployment`. That is fine for `configmap-*`, `secret.example.yaml`
(replaced with real values), `api-service.yaml` and `ingress.yaml`. It is **not** fine for
`migration-job.yaml`: the whole point of applying migrations as their own step
is that they finish, successfully, before any pod that expects the new schema
starts taking traffic. Run it as its own pipeline step:

```bash
kubectl apply -f deploy/kubernetes/secret.example.yaml   # with real values substituted
kubectl apply -f deploy/kubernetes/configmap-api.yaml -f deploy/kubernetes/configmap-worker.yaml
kubectl apply -f deploy/kubernetes/migration-job.yaml
kubectl wait --for=condition=complete job/app-template-migration --timeout=5m
kubectl apply -f deploy/kubernetes/api-deployment.yaml -f deploy/kubernetes/api-service.yaml -f deploy/kubernetes/ingress.yaml
kubectl apply -f deploy/kubernetes/worker-deployment.yaml
```

A CD pipeline that applies everything in one `kubectl apply -f deploy/kubernetes/` and hopes the
`Job` finishes before the `Deployment`'s pods pass their readiness probe is relying on timing,
not on an ordering guarantee — for a rolling update onto an already-running cluster it will
usually happen to work, and for a first install onto an empty database it usually will not.

## Namespace

None of these manifests set `metadata.namespace`. Pick one at apply time —
`kubectl apply -n <namespace> -f deploy/kubernetes/` — rather than hard-coding a name that only
suits one environment.
