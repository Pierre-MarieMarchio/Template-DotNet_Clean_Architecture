# 0012 — HSTS is owned by the ingress, not by the application

Status: Accepted

## Context

The application sends every other response-security header itself —
`X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options` and a default-deny
`Content-Security-Policy` — so the obvious next step is to add `app.UseHsts()` and be done.

`Strict-Transport-Security` is not like the others. Its three parameters are commitments
about a **whole domain, for a duration measured in months**, and none of them is knowable
from inside a process serving one path prefix:

- **`max-age`** tells every browser that saw the header to refuse plaintext to this host
  until it expires. It cannot be shortened retroactively; the only way back is to serve a
  `max-age=0` for at least as long as the original value was cached.
- **`includeSubDomains`** commits every sibling name under the domain — a status page, a
  legacy service, a `staging.` host — to TLS as well. The application does not know they
  exist, let alone whether they have certificates.
- **`preload`** asks for the domain to be baked into browser binaries. Removal takes
  months to ship. A template that shipped `preload` would burn a domain for whoever ran it
  once.

There is also a mechanical reason. TLS terminates upstream: the container listens on plain
HTTP on 8080 and `UseHttpsRedirection` is deliberately absent for that reason. So the
application does not see the TLS connection the header is about, and
`UseHsts()` would either be skipped as non-HTTPS or emit a domain-wide promise on the
strength of a forwarded header.

## Decision

**The application does not emit `Strict-Transport-Security`.** `UseHsts()` is not called and
no configuration switch turns it on. The component terminating TLS — ingress controller,
load balancer, CDN, reverse proxy — owns the header, because it is the component that knows
the certificate, the domain, and the sibling names.

**A deployment that terminates TLS is therefore required to send HSTS itself.** This is not
an optional hardening step left to taste: without it there is no HSTS anywhere, and the
first request to `http://` stays downgradeable. Concretely, on an nginx ingress:

```yaml
nginx.ingress.kubernetes.io/hsts: "true"
nginx.ingress.kubernetes.io/hsts-max-age: "31536000"
nginx.ingress.kubernetes.io/hsts-include-subdomains: "false"
nginx.ingress.kubernetes.io/hsts-preload: "false"
```

Start with a short `max-age` (300), confirm every host under the name serves TLS, then
raise it. Turn `includeSubDomains` on only after auditing the siblings, and treat `preload`
as a separate, deliberate decision with its own review.

## Consequences

- The application cannot make a promise it has no way to keep, and no fork of this template
  inherits a `max-age` somebody chose for an example.
- HSTS is configured once per domain, next to the certificate and the redirect, which is
  where the facts it depends on already live.
- The cost is real: **HSTS is now something a deployment can forget**, and forgetting it is
  silent — nothing fails, the site simply stays downgradeable on first contact. The
  requirement is stated here and in `Program.cs` beside the missing call, so the omission is
  at least visible to a reader; it is not, and cannot be, enforced by a test in this
  repository.
- `/health` and `/health/ready` stay plain, un-redirected HTTP, which is what the
  orchestrator's probe needs.

## Alternatives rejected

- **`app.UseHsts()` with the framework defaults** (`max-age` 30 days, no subdomains, no
  preload). Looks harmless and is the most common choice. It still commits the domain for a
  month on the strength of a value nobody chose, and behind terminated TLS the middleware is
  reading a forwarded scheme to decide whether to promise it.
- **`UseHsts()` driven by a `Hsts` configuration section**, so the operator supplies
  `max-age` and the flags. This is defensible, and it is the option closest to being right —
  it was rejected because it puts the knob one layer below the component that has the
  answer, duplicating a setting the ingress already needs for its own redirect, and creating
  a state where the two disagree. Two sources for one domain-wide promise is worse than one.
- **Emit HSTS only when the forwarded scheme is `https`.** Makes the header depend on a
  header, which is exactly the trust chain `ReverseProxyOptions` exists to keep narrow.
- **Document HSTS as optional and say nothing further.** That is the half-state this record
  refuses: a reader would conclude the template had considered it and decided it was
  unnecessary, rather than that the obligation moved.

## Revisit when

The application starts terminating TLS itself — a single-container deployment with a
certificate mounted into it and Kestrel configured for HTTPS. At that point the process does
know the domain and the certificate, the ingress is gone, and `UseHsts()` behind an explicit
configuration section becomes the right answer instead of the second-best one.
