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
dotnet cleanarch build       # dotnet build of the solution at the project root
dotnet cleanarch test        # dotnet test of the solution at the project root
dotnet cleanarch test --no-integration
                            # build every test project, run only the ones that need no Docker daemon
dotnet cleanarch coverage    # dotnet test with coverage, then the floor coverage.minimum states
```

Each command prints the exact `dotnet ...` line it runs, then forwards its exit code.

`help --json` lists only what the project file makes true: a command or an option the file
makes false does not appear, and a command that does not apply is refused with the reason.
