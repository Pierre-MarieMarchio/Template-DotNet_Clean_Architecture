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
dotnet cleanarch format      # repair the tree: UTF-8 BOM rules, LF endings, usings ordering
dotnet cleanarch format --check # fail where format would write instead of writing
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
dotnet cleanarch openapi     # write the OpenAPI reference, then regenerate the .NET client with the pinned Kiota
dotnet cleanarch openapi --check # fail where it would write, instead of writing
dotnet cleanarch bootstrap   # format the solution, restore the local tools, create each context's initial migration
dotnet cleanarch run         # dotnet run --project Src/Presentation/<Initials>.Api
dotnet cleanarch compose up  # docker compose up -d --wait
dotnet cleanarch compose down # docker compose down
dotnet cleanarch end-to-end  # the stack from .env.example, the end-to-end tests, then down --volumes
dotnet cleanarch end-to-end --keep-stack # the same, leaving the stack running
dotnet cleanarch sonar       # SonarCloud, where the project file records sonar true
dotnet cleanarch add feature Widgets Widget --access owner --deletion delete # a feature in every layer, composed
dotnet cleanarch add field Widgets "Code:string(20)='none'"                  # fields added to a feature add feature wrote
dotnet cleanarch add use-case Invoices ApproveInvoice --kind command         # one operation on a rich feature
dotnet cleanarch add setting Invoices InvoiceLimits --keys "MaxItems:int(1..100)=10"
dotnet cleanarch add event Invoices InvoicePaidDomainEvent --unconsumed "Nothing reacts to it yet"
dotnet cleanarch remove feature Widgets          # report what removing it involves, writing nothing
dotnet cleanarch remove feature Widgets --apply  # remove it, as its manifest records it
```

`format` walks the project root (skipping `bin`, `obj`, `node_modules`, `plans` and the dot
folders but `.github` and `.template.config`), saves each C# file as UTF-8 with a BOM and each
Markdown file without one, both with LF endings, and orders the usings of each C# file, System
first. It prints each file it saves; `--check` prints them on stderr and fails instead of
writing.

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
