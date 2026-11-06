# CleanArch.Cli

The tool of a project generated from the `CleanArch.WebApi.Templates` package, run as
`dotnet cleanarch`. It finds the project by the project file at its root, in the working
directory or the nearest folder above it, and says so when it is outside a project.

```bash
dotnet cleanarch             # describe this project
dotnet cleanarch --help
dotnet cleanarch --version
dotnet cleanarch help        # what this project's commands are
dotnet cleanarch help --json # the commands and options that apply to this project, as JSON
dotnet cleanarch format      # dotnet format of the solution, the Markdown normalised, then the naming check
dotnet cleanarch format --check # dotnet format --verify-no-changes over what the build does not check, and the Markdown
dotnet cleanarch build       # dotnet build of the solution at the project root
dotnet cleanarch test        # dotnet test of the solution at the project root
dotnet cleanarch test --no-integration
                            # build every test project, run only the ones that need no Docker daemon
dotnet cleanarch coverage    # dotnet test with coverage, then the floor coverage.minimum states
dotnet cleanarch verify      # the whole gate: the repository checks, the build, every test,
                             # the pending-model-changes checks and the vulnerable-packages audit
dotnet cleanarch add web-app --name Backoffice --framework vite-react
                             # add a web app: dotnet new cleanarch-webapp, then its join step
dotnet cleanarch add web-package --name tokens
                             # add a shared package to the web workspace
dotnet cleanarch add presentation --name Portal --framework blazor-server
                             # add a presentation project: dotnet new cleanarch-presentation, then its join and scaffold steps
dotnet cleanarch add web-app --dry-run
                             # print the commands it would run, write nothing
dotnet cleanarch migrations add AddTodoItemPriority            # dotnet ef migrations add, at the project root
dotnet cleanarch migrations add AddTodoItemPriority --check    # dotnet ef migrations has-pending-model-changes
dotnet cleanarch migrations bundle                             # dotnet ef migrations bundle, for each context
dotnet cleanarch database update                               # dotnet ef database update, at the project root
dotnet cleanarch openapi     # write the OpenAPI reference, then the .NET client and the web schema the project holds
dotnet cleanarch openapi --no-build # write the reference from the API's last build
dotnet cleanarch openapi --check # fail where it would write, instead of writing
dotnet cleanarch bootstrap   # format the solution, restore the local tools, create each context's initial migration
dotnet cleanarch run         # dotnet run --project Src/Presentation/<Initials>.Api
dotnet cleanarch compose up  # docker compose up -d --wait
dotnet cleanarch compose down # docker compose down
dotnet cleanarch end-to-end  # the stack from .env.example, the end-to-end tests, then down --volumes
dotnet cleanarch end-to-end --keep-stack # the same, leaving the stack running
dotnet cleanarch end-to-end --configuration Release # the tests' configuration, Debug by default
dotnet cleanarch sonar       # SonarCloud, where the project file records sonar true
dotnet cleanarch add feature Widgets Widget --access owner --deletion delete # a feature in every layer, composed
dotnet cleanarch add field Widgets "Code:string(20)='none'"                  # fields added to a feature add feature wrote
dotnet cleanarch add use-case Invoices ApproveInvoice --kind command         # one operation on a rich feature
dotnet cleanarch add setting Invoices InvoiceLimits --keys "MaxItems:int(1..100)=10"
dotnet cleanarch add event Invoices InvoicePaidDomainEvent --unconsumed "Nothing reacts to it yet"
dotnet cleanarch remove feature Widgets          # report what removing it involves, writing nothing
dotnet cleanarch remove feature Widgets --apply  # remove it, as its manifest records it
dotnet cleanarch adopt       # write cleanarch.json for a project generated before the tool
dotnet cleanarch adopt --dry-run # print the file it would write, and write nothing
dotnet cleanarch eject verify # copy the verify command's source into Tools/verify.cs, which then runs it
dotnet cleanarch eject verify --dry-run # say what it would write, and write nothing
```

`format` runs `dotnet format` over the solution, which applies `.editorconfig` (C# files as UTF-8
with a BOM, LF endings, the usings' order) and leaves the code it marks as generated, the
migrations and the generated .NET client among it. It then saves each Markdown file as UTF-8
without a BOM, with LF endings, walking the project root but `bin`, `obj`, `node_modules`, `plans`
and the dot folders other than `.github` and `.template.config`, and prints each file it saves.
Last, it runs `dotnet format --diagnostics IDE1006 --verify-no-changes`: `dotnet format` cannot
rename a symbol, so `format` names each file, line and name that breaks a naming rule and fails
while one is left. It stops at the first step that fails.

`format --check` writes nothing: it runs `dotnet format --verify-no-changes` over the style rules
the build does not enforce (IDE0003, IDE0049), then names on stderr each Markdown file `format`
would save and fails while one is left.

`build`, `test`, `coverage` and `verify` take `--configuration` (Debug by default) and `-m:<N>`,
which caps the build's nodes and the test modules run at once on a machine short of memory; with
it, `test` builds first, then runs the tests it built. `test` and `verify` leave out a test project
that targets Windows alone where they run elsewhere, and say so.

A command that runs an external tool prints the exact line it runs, runs it at the project root
and forwards its exit code.

`verify` chains its steps and stops at the first failing one, forwarding its exit code; its steps
follow the project file: the client check runs where the file records `client`, the web
workspace's install, audit, lint, test and build where it records a `web` workspace, and the
identity context's pending-model-changes check where it records `auth: local`.

`add` asks the questions of the item template it runs, from the manifests of the tool's own
version, and answers the ones about the project from the project file, never from the project's
files. It installs `CleanArch.WebApi.Templates` at the version the project file records, runs the
item template, then runs the project's own join step (`web-join`, `presentation-join`) and, for a
presentation, its scaffold step: these steps belong to the command and are not commands of their
own. Each takes `--name`, `--yes`, `--template-source`, `--hive`, `--dry-run` and one `--<option>`
per option of its template; `--dry-run` prints the install, the template and the steps it would run
and writes nothing. `add web-package` exists only where the project file records a `web` workspace.

`run` and `end-to-end` exist only where the project file records a presentation of framework
`api`; `sonar` only where it records `sonar` true.

`end-to-end` gives the stack, the tests and the browser smoke the variables the task gives them,
each only where it is unset: with external auth `EXTERNAL_AUTH_AUTHORITY` and
`EXTERNAL_AUTH_AUDIENCE`, plus `EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID` where a web app renders on the
server and `EXTERNAL_AUTH_BROWSER_CLIENT_ID` where one runs on WebAssembly; with files
`JOBS_FILES_INSPECT_INTERVAL`. The smoke reads `SMOKE_ENV_FILE=.env.example`, and a failing smoke
says to install the packages and Chromium. A failing step prints the stack's logs, and the stack
comes down unless `--keep-stack`.

`help --json` lists only what the project file makes true: a command or an option the file
makes false does not appear, and a command that does not apply is refused with the reason.

Each `add` command takes `--dry-run`, which prints the files and the edits it would make and
writes and runs nothing; without it, it writes every file and edit or none, records them in
the feature's manifest under `.feature-manifest/` where it has one (`add feature` writes it,
naming the tool and its version), then prints and runs each step that follows, such as the
build and the migration. `remove feature`
only reports unless given `--apply`. `add feature` and `add field` do not apply where the
project file records `auth: none`, since every feature they write needs an authenticated
caller, and are refused with that reason.

`adopt` is for a project generated before the tool, which has no project file yet: it finds the
folder that holds one solution beside `Tools/Tasks.cs`, reads the options, the web workspace and
the presentation projects from the files, and the template's version from the provenance sentence
of `docs/getting-started/overview.md` (or `--template-version` where that sentence is gone), then
writes `cleanarch.json` and prints it. It writes nothing where a fact cannot be read from the files,
naming each one, and it is refused where `cleanarch.json` exists. It creates a file once rather
than keeping one in step, so it takes `--dry-run`, as a generator does, and no `--check`.

## This project's own commands

A file-based app `Tools/<name>.cs` runs as `dotnet cleanarch <name>`, through
`dotnet run Tools/<name>.cs -- <arguments>`, which the tool prints before running it from the
working directory; the app receives every argument after its name, `--help` among them, and the
tool forwards its exit code. A new name adds a command; a built-in name replaces the built-in
command, even one the project file refuses. `help` describes each of them as this project's own
command, and `help --json` gives each a `local` key naming its file, with `replacesBuiltIn` true
for one that replaces a built-in command.

Only a file named as a command is one: lower-case words joined by hyphens, as `end-to-end` is.
`Tools/Tasks.cs`, the generators such as `Tools/NewFeature.cs` and every other file named in
PascalCase stay scripts run with `dotnet run`, and a file named `help` is no command: `help` stays
the tool's, since it says which commands are this project's own.

## Ejecting a command

`eject <command>` copies a built-in command's source into `Tools/<command>.cs`, a file-based app
that the project then owns and that runs in place of the built-in command, as the section above
says; that command stops following the tool's releases. The tool ships the source it is built
from, and the app holds the command's file with every file of the tool it needs, in the tool's
namespace, under a list of built-in commands that holds that one command alone; it pins
System.CommandLine at the version the tool is built with. The commands it offers are the ones this
project's file makes true: `help`, `eject` and `adopt` stay the tool's, a command the project file
refuses has nothing to eject, and a `Tools/<command>.cs` already there is never overwritten. It is
a generator, so it takes `--dry-run`.
