# Using this as a `dotnet new` template

This repository is itself a `dotnet new` template. Install it from a clone, or from the repository
path directly, then generate a project under your own name.

```bash
dotnet new install <path-to-this-repository>
dotnet new cleanarch-webapi -n Acme.OrderManagement
cd Acme.OrderManagement

dotnet run Tools/Tasks.cs bootstrap   # required once, and the first thing to run
```

`dotnet new uninstall <path-to-this-repository>` removes the template again.

**Generate outside the template repository.** `dotnet new` reads the template from the path you
installed, so generating into a subdirectory of that path makes the engine copy its own partial
output and you get a nested duplicate.

## What the name substitutes into

`AppTemplate` is the token every project name, namespace, `.sln`/`.csproj` file name and
Docker/Compose identifier is derived from. `-n Acme.OrderManagement` yields:

| | Becomes |
|---|---|
| Projects | `Acme.OrderManagement.Domain`, `Acme.OrderManagement.Api`, and so on for every project in the tree |
| Solution | Acme.OrderManagement.sln |
| Root namespace | `Acme.OrderManagement.*` |
| Compose project and image name | `acme-ordermanagement` — lower-cased, dots and underscores turned into hyphens |

Every project GUID is regenerated, so two generated projects never collide if they are opened side
by side. `.template.config/template.json` holds the token, the casing generators and the GUID list;
`Tests/Architecture/AppTemplate.Architecture.Tests` guards that list, so adding a project means
adding its GUID there.

## `bootstrap` is not optional

Sorted `using` directives are ordered alphabetically, so where a project's own namespace falls
relative to `FluentValidation`, `Microsoft.*` and the rest depends on the name you chose.
`AppTemplate` sorts one way, `Acme.OrderManagement` another. Until `bootstrap` runs once,
`dotnet format --verify-no-changes` fails — and since that is the *first* step of the CI workflow
you inherit, your first push fails with it. One run fixes it permanently, and it also restores the
local tools the gate needs.

This cannot be fixed in the template itself: no single committed ordering is correct for every
possible name. Commit the result before anything else.

## What a generated project inherits

**The three example features ship by default.** `TodoLists`, `Reminders` and `Files` are the worked
examples for [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md): one aggregate with child entities, one
flat, and one whose two halves live in different stores — metadata in PostgreSQL, bytes behind a
port to an S3-compatible object store. There is no generator switch to exclude them.
[`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md) is the verified procedure and
says what stops being demonstrated once they go.

`Files` is the one where deleting is not the only sensible answer: unlike the other two it is also
a working capability, so a project that stores files re-points it at its own bucket rather than
removing it.

**The whole of `Tests/` arrives too, fixtures included.** That is the delivery mechanism rather than
a gap, and it is why there is no test-kit package to reference — [`TESTING.md`](TESTING.md#what-a-derived-project-inherits)
says which fixtures can be leaned on unchanged and which two you own from the first commit.

**And the whole gate.** `.github/workflows/` and everything under `Tools/` come with it, so a
generated project can judge itself from a clone. Two things about that are worth setting
deliberately rather than inheriting: `coverage.minimum` states a floor measured against *this*
repository's suite, and the Sonar workflow analyses nothing until the repository variables it names
are set. [`BUILD-AND-CI.md`](BUILD-AND-CI.md) covers both.

**Two documents want replacing before you deploy.** `SECURITY.md`'s reporting section tells a finder
to file a public issue, which is right for a template and wrong for your production system; and
`README.md` describes this template rather than your product.

## The generation flow is a CI gate

`.github/workflows/ci.yml`'s `template` job runs this exact install → generate → build → test
sequence, under a different name, on every push. A template that generates a broken project is
worse than a repository to clone, so that flow is a gate rather than a manual step.

## If the repository already exists

Two shapes work and they cost different things —
[`INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md`](INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md) measures
both. In short: at the repository root everything works exactly as documented, and in a
subdirectory two hygiene gates and one task have to be told where the repository root actually is.
