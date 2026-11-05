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
dotnet cleanarch bootstrap   # format the solution, restore the local tools, create each context's initial migration
dotnet cleanarch run         # dotnet run --project Src/Presentation/<Initials>.Api
dotnet cleanarch compose up  # docker compose up -d --wait
dotnet cleanarch compose down # docker compose down
dotnet cleanarch end-to-end  # the stack from .env.example, the end-to-end tests, then down --volumes
dotnet cleanarch end-to-end --keep-stack # the same, leaving the stack running
dotnet cleanarch sonar       # SonarCloud, where the project file records sonar true
```

Each command prints the exact command line it runs, runs it at the project root and forwards its
exit code. `run` and `end-to-end` exist only where the project file records a presentation of
framework `api`; `sonar` only where it records `sonar` true.

`help --json` lists only what the project file makes true: a command or an option the file
makes false does not appear, and a command that does not apply is refused with the reason.
