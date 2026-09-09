# Documentation

Every document here has one subject. This page is how you find the one you need.

## Start here

| If you are… | Read |
|---|---|
| looking at this repository for the first time | [`../README.md`](../README.md) — what it is, and a five-minute run |
| about to run it locally | [`GETTING-STARTED.md`](GETTING-STARTED.md) |
| generating a project from it | [`USING-THE-TEMPLATE.md`](USING-THE-TEMPLATE.md) |
| writing a client against the API | [`API-CONVENTIONS.md`](API-CONVENTIONS.md), then [`API-REFERENCE.md`](API-REFERENCE.md) |
| about to write code in it | [`PROJECT-LAYOUT.md`](PROJECT-LAYOUT.md), then [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md) |
| about to deploy it | [`../SECURITY.md`](../SECURITY.md), then [`DEPLOYMENT.md`](DEPLOYMENT.md) |

## By subject

**Running it**

| Document | Subject |
|---|---|
| [`GETTING-STARTED.md`](GETTING-STARTED.md) | Compose, running on the host, and a register-to-first-request walkthrough |
| [`USING-THE-TEMPLATE.md`](USING-THE-TEMPLATE.md) | `dotnet new`, what the project name substitutes into, and the one command that is not optional |
| [`INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md`](INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md) | moving a generated project into a repository that already has a history |
| [`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md) | deleting `TodoLists`, `Reminders` and `Files` once you have read them |

**The HTTP surface**

| Document | Subject |
|---|---|
| [`API-CONVENTIONS.md`](API-CONVENTIONS.md) | what holds for every endpoint: versioning, default-deny, error codes, `ETag`, `Idempotency-Key`, collection queries, rate limits |
| [`API-REFERENCE.md`](API-REFERENCE.md) | the endpoints themselves, one table per feature |

**Understanding the code**

| Document | Subject |
|---|---|
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | the four layers, the dependency rule, and the reasoning — including what is deliberately absent |
| [`PROJECT-LAYOUT.md`](PROJECT-LAYOUT.md) | which project holds what, and the folder rules the architecture tests enforce |
| [`CONVENTIONS.md`](CONVENTIONS.md) | naming, visibility, where a contract lives, and the comment policy |
| [`DECISIONS.md`](DECISIONS.md) | the choices a reasonable person could have made differently, and the test holding each one |

**Changing the code**

| Document | Subject |
|---|---|
| [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md) | the vertical, end to end, with the real signatures at each step |
| [`TESTING.md`](TESTING.md) | the test stack, how to run it, and the sharp edges that have cost time here |
| [`MIGRATIONS.md`](MIGRATIONS.md) | two contexts, two histories, and the commands for each |
| [`BUILD-AND-CI.md`](BUILD-AND-CI.md) | the gate, the tools under `Tools/`, the workflows, the container image, the supply chain |

**Shipping it**

| Document | Subject |
|---|---|
| [`../SECURITY.md`](../SECURITY.md) | what the template provides, and the longer half: what a deployment still owes |
| [`CONFIGURATION.md`](CONFIGURATION.md) | every configuration key, its default, its validated range, and what happens when it is wrong |
| [`DEPLOYMENT.md`](DEPLOYMENT.md) | running it on Kubernetes, and why the manifests' numbers are what they are |
| [`../deploy/kubernetes/README.md`](../deploy/kubernetes/README.md) | the manifests themselves, and the order to apply them in |

## `plans/`

[`plans/`](plans/) holds closed plans of record: what a piece of work set out to do, what it
decided, and what it refused. They are kept because the reasoning is worth more than the diff,
and they are read for that reasoning alone.

**They are not documentation of the current tree.** A plan states what was true when it was
written, and a plan superseded by a later one is not edited to agree with it. Everything above
describes the repository as it is; anything in `plans/` that disagrees with it is out of date by
construction. [`plans/README.md`](plans/README.md) lists them.
