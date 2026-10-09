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

## Generation options

One choice says which example features the project starts with, and three switches leave parts of
the template out, all on by default:

```bash
dotnet new cleanarch-webapi -n Acme.OrderManagement --examples none --files false --ci false --sonar false
```

| `--examples` | Generates |
|---|---|
| `all` (default) | `TodoLists`, `Reminders` and `Notes` |
| `notes` | `Notes` only: the simple shape, with no domain model and no other feature to lean on |
| `none` | no example feature: the modules, the mechanisms and their tests only |

`Reminders` is scheduled against a to-do item and does not compile without `TodoLists`, so the two
come together or not at all. What two features share follows its real users rather than the option:
the domain's and the application's `Common/Tagging/` stay whenever `Files` is kept, since stored files
are tagged by the same rule, and leave only when neither `TodoLists` nor `Files` is generated. A test
whose subject is an example's endpoint or aggregate — ownership isolation over to-do lists, the
idempotent replay of a list's creation, the save-pipeline tests written against a to-do list — leaves
with that example, so a project generated with `none` checks those mechanisms again only once a
feature of its own gives them a subject.

| Option | Leaves out when `false` |
|---|---|
| `--files` | the `Files` feature in every layer, the storage module and its unit-test project, the MinIO services in Compose, the `Files` and `FileWorker` settings, the `files` schema of the business migration and the tests that name any of it |
| `--ci` | the GitHub Actions build and release workflows, and the workflow gate in `Tools/` |
| `--sonar` | the local SonarQube profile in Compose, the scanner image and its task, the scanner tool in the local tool manifest, and the SonarQube Cloud workflow (generated only when `--ci` is also true) |

What is left out is absent, not disabled: no project, file, configuration section, table or test
names it, and the generated solution builds, passes its Docker-free tests and reports no pending
model change for either context. The template engine reads a condition written inside each file
type's own comment syntax, and `.template.config/template.json` excludes whole paths; in this
repository those conditions are plain comments, so it builds and tests as the default generation
does.

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

**The four example features ship by default.** `TodoLists`, `Reminders`, `Files` and `Notes` are the
worked examples for [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md): one aggregate with child entities,
one flat, one whose two halves live in different stores — metadata in PostgreSQL, bytes behind a
port to an S3-compatible or local file store — and one with no domain model at all, a service over a store. `--files false`
leaves `Files` out and `--examples` chooses among the other three, so a project that knows it wants
none of them never receives them.
[`REMOVING-THE-EXAMPLE-FEATURES.md`](REMOVING-THE-EXAMPLE-FEATURES.md) is the procedure for a project
generated with them that later wants them gone, and says what stops being demonstrated once they go.

`Files` is the one where deleting is not the only sensible answer: unlike the other two it is also
a working capability, so a project that stores files re-points it at its own bucket rather than
removing it.

**The whole of `Tests/` arrives too, fixtures included.** That is the delivery mechanism rather than
a gap, and it is why there is no test-kit package to reference — [`TESTING.md`](TESTING.md#what-a-derived-project-inherits)
says which fixtures can be leaned on unchanged and which two you own from the first commit.

<!--#if (ci)-->
**And the whole gate.** `.github/workflows/` and everything under `Tools/` come with it, so a
generated project can judge itself from a clone. Two things about that are worth setting
deliberately rather than inheriting: `coverage.minimum` states a floor measured against *this*
repository's suite, and the Sonar workflow analyses nothing until the repository variables it names
are set. [`BUILD-AND-CI.md`](BUILD-AND-CI.md) covers both.
<!--#else
**And the whole gate.** Everything under `Tools/` comes with it, so a
generated project can judge itself from a clone. Two things about that are worth setting
deliberately rather than inheriting: `coverage.minimum` states a floor measured against *this*
repository's suite, and the Sonar workflow analyses nothing until the repository variables it names
are set. [`BUILD-AND-CI.md`](BUILD-AND-CI.md) covers both.
#endif-->

**Two documents want replacing before you deploy.** `SECURITY.md`'s reporting section tells a finder
to file a public issue, which is right for a template and wrong for your production system; and
`README.md` describes this template rather than your product.

## Adding features to the generated project

The feature generator works under the new name, because it reads the product prefix from the tree
rather than assuming `AppTemplate`: in a project generated as `Acme.OrderManagement`,
`dotnet run Tools/Tasks.cs new-feature Orders Order --access owner` writes
`Acme.OrderManagement.*` namespaces. [`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md#the-generator)
describes its choices. It writes a manifest of what it created, with a hash of each file, in a
`.feature-manifest` folder of the generated project; the template itself ships none.

<!--#if (ci)-->
## The generation flow is a CI gate

`.github/workflows/ci.yml`'s `template` job runs this exact install → generate → build → test
sequence, under a different name, on every push. A template that generates a broken project is
worse than a repository to clone, so that flow is a gate rather than a manual step.

<!--#endif-->
## If the repository already exists

Two shapes work and they cost different things —
[`INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md`](INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md) measures
both. In short: at the repository root everything works exactly as documented, and in a
subdirectory two hygiene gates and one task have to be told where the repository root actually is.
