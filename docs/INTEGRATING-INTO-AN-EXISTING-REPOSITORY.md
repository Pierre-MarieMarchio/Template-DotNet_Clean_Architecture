# Integrating into an existing repository

The supported way to start is `dotnet new cleanarch-webapi -n Your.Name` into an empty directory,
which `README.md` covers and a CI job rehearses on every push. This document covers the other
case: the repository already exists, already has a history, and the generated project has to move
into it.

There are two shapes, and the choice is not a matter of taste.

**At the repository root.** The generated tree *is* the repository: its solution, its
`.github/workflows/`, its `Directory.Packages.props`, its `global.json`. Whatever the repository
already had at those paths is merged into them by hand. Every tool and every gate then works
exactly as documented, because every one of them assumes the solution and the repository root are
the same directory.

**In a subdirectory.** The generated tree becomes one service among several — `services/api/`, say.
Nothing above is merged, so the existing repository keeps its own root files. The price is paid in
the tooling instead: two of the four hygiene gates and one Tasks.cs target stop working until they
are told where the repository root actually is, and six documentation citations stop resolving.

Take the first shape unless the repository must hold more than this one service. The second one is
not hard, but everything it costs is listed below and none of it is optional.

## What this document promises

The subdirectory shape was carried out in full, in a throwaway git repository built to look like a
plausible host — its own README, `.gitignore`, LICENSE, workflow, `global.json`,
`Directory.Build.props`, a `Directory.Packages.props` with central package management already
switched on, and a class library in its own solution. The generated project was `Acme.Billing`,
moved to `services/api/`. Every failure quoted below is a failure that happened, with the exit code
it produced, and every fix is one that was applied and re-measured. The end state was:
`dotnet build` 0 warnings 0 errors, 3261 tests passing with 0 failing,
`dotnet format --verify-no-changes` clean, all four hygiene gates green with 3 workflows validated,
and `docker compose config` clean.

Five things are **not** measured here, and you should treat them as reasoning:

- **The root shape, end to end.** Its claim — that everything works as documented because the
  assumption every tool makes is satisfied — follows from what the subdirectory shape had to repair
  to reach the same place. No repository was actually assembled that way.
- **The release workflow.** Only the CI workflow was driven back to green. `release.yml` needs the
  same treatment and was not given it.
- **GitHub actually running the adapted workflow.** The gate that checks workflows structurally was
  satisfied; nothing was pushed to a real repository to watch it run.
- **The container build and `docker compose up` in the integrated layout.** `docker compose config`
  parsed and resolved; no image was built and nothing was started.
- **Any host repository but the synthetic one.** A host with several solutions, a `Directory.Build.props`
  that other projects depend on, or its own `.editorconfig` will meet more than this.

## What the tooling assumes, and why that is the whole story

Four assumptions decide everything below. Three were measured; the third was read.

**The repository root is the directory holding the solution.** `Tools/Tasks.cs` walks up from its
own source file until it finds the `.sln` and calls that directory the repository root — so a
generated project nested at any depth resolves its own root correctly, and every gate it invokes is
handed that directory rather than the git root. This is what makes the subdirectory shape work at
all, and also what makes it fail for workflows, which do not live there.

**GitHub reads `.github/workflows/` from the git root and nowhere else.** A workflow left inside the
subdirectory is a file that looks like CI and never runs.

**MSBuild takes the nearest `Directory.Build.props` and `Directory.Packages.props` walking up, and
stops.** The host's root copies therefore do not apply to the generated projects, and the generated
copies do not inherit from them. Measured: the build succeeded from the subdirectory with a host
`Directory.Packages.props` at the root that pins a package the generated projects never see. Read as
a consequence, not measured: a property the host repository relies on for all its projects silently
does not reach these ones.

**`.editorconfig` in the generated tree declares `root = true`**, so the host's own `.editorconfig`
stops at that boundary. Read, not measured.

## The subdirectory shape, step by step

### 1. Generate outside, then move

```bash
dotnet new install <path-to-this-repository>

cd /tmp                                   # anywhere outside both repositories
dotnet new cleanarch-webapi -n Acme.Billing

mkdir -p <host-repo>/services/api
cp -a Acme.Billing/. <host-repo>/services/api/
```

Generating straight into the host repository works too. Generating into a subdirectory of *this*
repository does not: the engine copies its own partial output and you get a nested duplicate.

### 2. Run bootstrap, and do not skip it

```bash
cd <host-repo>/services/api
dotnet run Tools/Tasks.cs bootstrap
```

`README.md` says this is mandatory. It is, and the reason is measurable: sorted `using` directives
depend on where the project's own namespace falls alphabetically among the third-party ones, which
the template cannot know in advance. Before this ran, `dotnet format --verify-no-changes` exited 2
on the moved tree; `dotnet format` then rewrote **11 files, 25 lines**, all of them import
ordering, and the verify pass exited 0. That check is the first step of the CI workflow you
inherit, so skipping it fails your first push.

One caveat, reproducible but not diagnosed: on the machine this was measured on, `bootstrap` exits 1
before it reaches the formatting, on `dotnet tool restore` — `Settings file 'DotnetToolSettings.xml'
was not found in the package`, with nothing extracted into the local package cache. The same failure
happens in this repository itself, so it is not something the integration causes, and CI runs the
same command green. Running the two halves separately works: `dotnet tool restore` when it will, and
`dotnet format` for the part that matters here.

### 3. Teach the host's `global.json` about the test runner

This is the failure that costs the most time if you meet it without warning. The generated
`global.json` carries a `test` block naming `Microsoft.Testing.Platform` as the runner. The host
repository has its own `global.json` at its root, and `global.json` is resolved from the current
directory upward — so which one applies depends on where you stand.

Measured, from the host repository root:

```
dotnet test services/api/Acme.Billing.sln
  error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform
          on .NET 10 SDK and later.
```

Twelve times, once per test project. From `services/api` the same command passed. The difference is
not the SDK version — both directories selected the same SDK on that machine — it is the `test`
block. Add it to the host's root `global.json`:

```json
{
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

After which the run from the repository root passed: 3261 succeeded, 0 failed, 3 skipped.

Those three skips are correct and will follow you. They are the template-packaging rules, which call
`Assert.Skip` when there is no `.template.config/` directory — a generated project is not the
template, and those three rules only mean something about the template.

### 4. Move the workflows to the git root, and expect two gates to go red

```bash
mv services/api/.github/workflows/ci.yml      .github/workflows/api-ci.yml
mv services/api/.github/workflows/release.yml .github/workflows/api-release.yml
rmdir services/api/.github/workflows services/api/.github
```

Renaming them is worth doing if the host already has workflows of its own; nothing depends on the
names.

Two gates then fail, and both are right to.

`Tools/CheckWorkflows.cs`, pointed at the solution directory, finds no workflow at all and exits 1
with `no workflow file was examined, so this check proves nothing`. That is its anti-vacuity rule
working: a validator that examined nothing has established nothing.

`Tools/CheckDocPaths.cs` exits 1 on four citations that no longer resolve — six occurrences across
`README.md`, `CONTRIBUTING.md` and `docs/DEPLOYMENT.md`, all naming the workflow files by their old
paths. Rewrite them to where the files now are. Written as prose rather than in backticks they stop
being checkable claims about the tree, which is honest for a path that now lives above the scanned
root; the alternative is to keep them in backticks and teach that gate a second base directory.
After the rewrite it exited 0, checking 296 path references.

### 5. Point the workflow gate at the git root, and let it dictate the rest

```bash
dotnet run services/api/Tools/CheckWorkflows.cs .
```

Run from the repository root, this gate becomes the checklist for the whole adaptation. It verifies
that every file a `run:` step names exists on disk, so it reports, one by one, every path in the
moved workflow that still assumes a root it no longer has. Measured: 11 findings in the moved CI
workflow, each naming the job and the step, plus one against the host repository's own pre-existing
workflow for having no `permissions:` block.

Prefix each of those paths with the subdirectory. Do **not** reach for
`defaults: run: working-directory:` instead — GitHub would be satisfied and the gate would not,
because it resolves the paths as written and knows nothing about that key. Prefixing keeps both
happy and keeps the check alive.

One expected snag: the gate's path pattern is anchored on the top-level directory names of a
repository whose solution sits at its root, with no left-hand boundary — so a path under the
subdirectory is matched from its first recognised segment onwards, resolved as if it sat at the root,
and reported as missing. Two optional prefixes in that pattern fix it, and
took the count from 12 findings to 2. The last two were a path named inside two log messages rather
than passed to a command, and the host's own workflow. With both settled the gate exited 0, having
validated 3 workflows — the two moved ones and the host's.

That last finding is a feature rather than an accident. Adopting this repository's gates means the
host's existing workflows start being held to the same standard.

### 6. Point the grouped hygiene target at the git root too

`dotnet run Tools/Tasks.cs hygiene` still exited 1 after all of the above, for the same reason as
step 4: it hands the solution directory to every gate, including the one whose subject now lives two
levels up. One line in its `Gates` method fixes it —

```csharp
Step("dotnet", "run", Gate(repoRoot, "CheckWorkflows.cs"), Path.Combine(repoRoot, "..", ".."));
```

— after which the target exited 0 with all four gates green.

### 7. Compose

The compose file's build contexts are relative to the compose file itself, so the whole subtree
moves as a unit and nothing needs re-pointing. It does need its environment file beside it, in the
subdirectory rather than at the repository root:

```bash
cd services/api
cp .env.example .env
docker compose config -q
```

Measured: exit 1 without the copy, exit 0 with it and no warnings.

### 8. What is left at the host's root

The generated tree carries eight files that a host repository is likely to have already: README.md,
LICENSE, `.gitignore`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`,
`coverage.minimum` and `coverage.runsettings`. In the subdirectory shape all of them stay inside the
subdirectory and only `global.json` needs anything done to it, per step 3. Nothing needs merging,
and the two solutions coexist untouched — the host's at its root, the generated one at
`services/api`.

## Verification

Run all of it, from the subdirectory unless stated:

```bash
dotnet run Tools/Tasks.cs bootstrap        # once, before anything else
dotnet build Acme.Billing.sln
dotnet test Acme.Billing.sln
dotnet format Acme.Billing.sln --verify-no-changes
dotnet run Tools/Tasks.cs hygiene
cd ../.. && dotnet run services/api/Tools/CheckWorkflows.cs .
```

What that produced when this document was written: build 0 warnings 0 errors; 3261 passing, 0
failing, 3 skipped; formatting clean; four gates green; 3 workflows validated, 0 problems.

The coverage gate is the one thing left to decide rather than repair. `coverage.minimum` states a
floor measured against this repository's own test suite. It is still the right floor for the
generated project on day one, and it stops being the right floor the moment you add code of your
own — re-measure and re-state it rather than carrying the number over unexamined. `CONTRIBUTING.md`
says how that file is meant to be set.
