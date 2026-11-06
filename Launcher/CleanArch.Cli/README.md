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
dotnet cleanarch migrations add AddTodoItemPriority            # dotnet ef migrations add, at the project root
dotnet cleanarch migrations add AddTodoItemPriority --check    # dotnet ef migrations has-pending-model-changes
dotnet cleanarch migrations bundle                             # dotnet ef migrations bundle, for each context
dotnet cleanarch database update                               # dotnet ef database update, at the project root
```

`format` walks the project root (skipping `bin`, `obj`, `node_modules`, `plans` and the dot
folders but `.github` and `.template.config`), saves each C# file as UTF-8 with a BOM and each
Markdown file without one, both with LF endings, and orders the usings of each C# file, System
first. It prints each file it saves; `--check` prints them on stderr and fails instead of
writing.

`build`, `test` and `coverage` each print the exact `dotnet ...` line they run, then forward its exit code.

`verify` prints each line it runs and runs it at the project root. It chains its steps and stops at the first failing one, forwarding its exit code; its steps
follow the project file: the client check runs where the file records `client`, the web workspace's
install, audit, lint, test and build where it records a `web` workspace, and the identity context's
pending-model-changes check where it records `auth: local`.

Each command prints the `dotnet ef` line it runs, runs it at the project root and forwards its exit code.

`help --json` lists only what the project file makes true: a command or an option the file
makes false does not appear, and a command that does not apply is refused with the reason.
