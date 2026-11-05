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
dotnet cleanarch verify      # the whole gate: the repository checks, the build, every test,
                             # the pending-model-changes checks and the vulnerable-packages audit
```

Each command prints the line it runs, runs it at the project root and forwards its exit code.
`verify` chains its steps and stops at the first failing one, forwarding its exit code; its steps
follow the project file: the client check runs where the file records `client`, the web workspace's
install, audit, lint, test and build where it records a `web` workspace, and the identity context's
pending-model-changes check where it records `auth: local`.

`help --json` lists only what the project file makes true: a command or an option the file
makes false does not appear, and a command that does not apply is refused with the reason.
