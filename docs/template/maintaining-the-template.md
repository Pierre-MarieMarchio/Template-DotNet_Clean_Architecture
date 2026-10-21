# Maintaining the template

This repository builds, tests and runs as the default generation does: every condition is a comment
the compiler ignores. A change is done when the project's gate passes here and every generated shape
still holds.

## The template gates

`dotnet run Tools/Tasks.cs verify` runs the project's gate plus what only the template carries:

| Gate | Checks |
|---|---|
| `Tools/CheckDocPaths.cs` | every repository path in a Markdown code span exists, and every `docs/` path a workflow, script or `.http` file names; `CHANGELOG.md` and the plans folder are exempt |
| `Tools/CheckWorkflows.cs` | what a YAML parse misses: a dangling `needs:`, an action on a mutable tag, an image without a digest, a missing `permissions:`, an undeclared `$VAR`, a path absent from disk |
| `Tools/CheckComments.cs` | no comment outside the allowlist, one guidance line at a time in configuration files, no Markdown narrating the repository's history |
| `web-lockfiles --check` | each generated web shape's lockfile equals what the pinned pnpm writes for it; needs pnpm (`corepack enable`) |

Each gate runs its `--self-test` first, against fixtures that must make it fire and fixtures that
must leave it silent, and fails when it examined nothing. A rule added to a gate comes with the fixture
that fails without it. These three files are excluded from generation, and the project's `hygiene`
task asks the filesystem which gates its tree carries, so one task serves both.

CI runs them in `.github/workflows/ci.yml`, under `templateMaintenance` blocks a generated project
does not receive.

## The acceptance matrix

`.github/workflows/template-acceptance.yml` packs the template package once, and per leg installs
that `.nupkg`, generates under another name, asserts the source name leaked nowhere, bootstraps,
builds with warnings as errors, runs the Docker-free tests, checks every generated context for
pending model changes, and runs the generated project's `hygiene`. The defaults leg and the smallest
project's leg also run the integration suites, on the runner's Docker through Testcontainers. A web
leg also checks its lockfile, adds a second app and a package with the item templates, scaffolds,
runs the workspace's install, lint, test and build, checks a second `web-scaffold` changes nothing,
starts the whole stack and asks each app for its page, its headers and an answer only the API gives.

| Leg | Options |
|---|---|
| defaults | none |
| auth none, smallest project | `--auth none --examples none --files false` |
| external auth, notes | `--auth external --examples notes` |
| local auth, no files, no ci, no sonar | `--auth local --examples none --files false --ci false --sonar false` |
| external auth, all examples | `--auth external --examples all --files true` |
| external auth, no examples, no files | `--auth external --examples none --files false` |
| web placeholder, smallest project | `--web placeholder --auth none --examples none --files false` |
| web react, local auth | `--web react` |
| web angular, external auth, notes | `--web angular --auth external --examples notes` |
| web react back office, smallest local project | `--web react --web-audience admin --examples none --files false` |
| web react, then an angular back office | `--web react`, then `cleanarch-webapp --framework angular --audience admin` |
| no web, then a react app and its workspace | none, then `cleanarch-webapp --framework react --workspace` |

A leg without a web app asserts no workflow, Dependabot entry, Compose file or ignore file names a
workspace. The leg that adds a first app runs that assertion, then `cleanarch-webapp --workspace` and
the lines `Src/Web/README.md` lists, and is a web leg from there. `--workspace` only adds files, and
the engine refuses to overwrite one, so everything a web app needs from the API ships with every
project and the web jobs are `web.yml`, not jobs of `ci.yml`. A separate job asserts each refused combination exits non-zero and writes no file. The legs
are the shapes the options produce, not their cross product, and run with `fail-fast: false`.

## Adding an option

1. Declare the parameter in `.template.config/template.json` with its `displayName`, and a computed
   symbol when pages and code need a name for a combination. A hidden option or a long name goes in
   `dotnetcli.host.json`. A visible option gets its label in `ide.host.json`, which Visual Studio
   reads and which hides every symbol it does not list; `TemplatePackagingTests` refuses a refusal
   shown or an option missing there.
2. Exclude whole paths with a source modifier under a condition; condition content inside a file in
   that file's own comment syntax.
3. A combination that cannot hold together is a hidden required parameter, and the same condition in
   `refusedOptions`, so the `#error` variant catches a hand-passed bypass.
4. Add the leg that generates the new shape, and a refusal to the refusal job.
5. Condition every page whose truth changes, and judge each in every form it can take.

## Writing docs that condition

Project pages reach every generated project, so each is written for each shape. Inside Markdown:

```markdown
<!--#if (files)-->
The object store keeps the bytes.
<!--#endif-->
<!--#if (!files)-->
No object store runs.
<!--#endif-->
```

- Use only real symbols of `template.json`, negated with `!` for the other branch; never a bare
  else, elseif or a marker inline in a sentence.
- A branch holds whole sentences, whole list items or whole table rows; a table's header stays outside
  any branch that can leave rows without it.
- No marker inside a mermaid fence. A diagram shows the full shape, unconditioned, and says so.
- A page about an absent part is excluded by a path condition, not emptied by markers; its links from
  shared pages are conditioned the same way.
- A link from a shared file (`CONTRIBUTING.md`, `AGENTS.md`, `SECURITY.md`) to `docs/template/` sits
  in a `templateMaintenance` block, which is always false at generation.
- Text that names a token (`AppTemplate`, `app-template`, the web app tokens) is replaced in a
  generated project; write a token only where its replacement reads right.
- Project pages say "this project"; only `docs/template/` and the repository `README.md` say "the
  template".

Read a page in the default form, in `--auth none --examples none --files false`, and in each form its
markers distinguish: no broken link or anchor, no marker left, no orphan punctuation, no page about an
absent module.

## Web sources and lockfiles

One folder per framework, `Src/Web/apps/PlaceholderSite`, `ReactSite` and `AngularSite`, is the source
of both `--web` and `cleanarch-webapp`: the project template keeps the chosen one and renames it from
`--web-name`, the item template renames it from `-n`. Each name is a token in three spellings (for
example `PlaceholderSite`, `placeholder-site`, `PLACEHOLDER_SITE`). A framework folder holds only the
wiring, its `scaffold.json` and its overlay; the app itself comes from the framework's official CLI at
bootstrap.

The repository's `Src/Web/pnpm-lock.yaml` locks every app at once, so it is excluded from generation.
`dotnet run Tools/Tasks.cs web-lockfiles` writes the lockfile of each shape holding a `package.json`
into `.template.config/variants/web-<App>/Src/Web/`; a framework app gets none, since `web-scaffold`
writes it where the app is created. Run the task after any change to the union lockfile, Dependabot's
included; `--check` fails until you do.

## The package

`.template.config/TemplatePackage.csproj` packs the three templates into one NuGet package,
`CleanArch.WebApi.Templates`; its `PackageId` is the one place that names it.

```bash
dotnet pack .template.config/TemplatePackage.csproj --output artifacts/package
```

| Facet | Where |
|---|---|
| Content | every file of the checkout under `content/`, minus what generation always leaves out, the scratch folders (`.git`, `.claude`, `bin`, `obj`, `node_modules`, `TestResults`, `artifacts`), the plans and the package project; `TemplatePackagingTests` holds the list against `template.json` |
| Version | the `templateVersion` symbol of `.template.config/template.json`, and nowhere else; it replaces `0.0.0-checkout` in a generated project's overview, as [template decision 0002](decisions/0002-nothing-about-the-template-reaches-a-project.md) allows |
| Metadata | id, authors, description, tags, licence, the repository `README.md` and `.template.config/icon.png`, all in the package project |

To release, raise `templateVersion`, merge, and push the tag `template-v<version>`.
`.github/workflows/template-release.yml` then refuses a tag that does not name the version, packs,
installs the package alone in a scratch hive, generates and builds the defaults, and uploads the
`.nupkg` as the `template-package` artifact. Its `github-release` job, the only one with
`contents: write`, creates the tag's GitHub Release with the `.nupkg` attached, as a prerelease when
the version has a suffix. Its publishing job is skipped until the repository variable
`PUBLISH_TEMPLATE_TO_NUGET` is `true`, and reads `NUGET_API_KEY` from the `nuget` environment; a
version already on nuget.org fails the push. Nothing in the workflow runs but on a tag push.

## Changelog

`CHANGELOG.md` follows Keep a Changelog and stays here. Record what changes a generated project: a
layer's shape, a rule the tests hold, a setting, a default that affects security or deployment. A major
version means adopting the change in an existing project takes manual work.

## Decisions

- [0001. Options remove parts and refuse combinations](decisions/0001-options-remove-parts-and-refuse-combinations.md)
- [0002. Nothing about the template reaches a project](decisions/0002-nothing-about-the-template-reaches-a-project.md)
- [0003. Framework apps come from official CLIs](decisions/0003-framework-apps-come-from-official-clis.md)
