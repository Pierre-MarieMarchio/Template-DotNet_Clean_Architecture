# CleanArch.Create

Creates a .NET 10 HTTP API on Clean Architecture by asking for each option of the
`cleanarch-webapi` template, then runs `dotnet new` with the answers. Run inside a project it
generated, it adds a web app or a web package the same way.

## Create a project

You need a .NET 10 SDK (10.0.100 or later), which runs a tool without installing it:

```bash
dnx CleanArch.Create
```

The launcher asks for the name, the folder and each option, with the template's own choices,
defaults and descriptions. It offers only what the template generates with the answers given so far:
a choice the template refuses whatever follows, as `--database mysql`, is never offered, and a
question left with a single answer, as `--examples none` after `--auth none`, is answered with it and
the refusal that says why. A combination of flags the template refuses is explained before anything
is written, and its options are asked again. It installs the `CleanArch.WebApi.Templates` package at
its own version when another one or none is installed, generates the project, then prints the
equivalent command and the first steps.

## Add a web app or a package

At the root of a generated project, or in any folder under it:

```bash
dnx CleanArch.Create web-app
dnx CleanArch.Create web-package
```

The project's authentication, workflows and package scope are read from its files. The first web app
also creates the pnpm workspace, and the launcher prints the lines its README asks to add by hand.

## Without questions

Every question is a flag named like the template's option; `--yes` takes the default of each one not
given. Without a terminal, every answer is a flag or `--yes` is passed.

```bash
dnx CleanArch.Create new -n Acme.Shop --auth external --examples notes --yes
dnx CleanArch.Create web-app -n BackOffice --framework react --audience admin --yes
```

| Option | Meaning |
|---|---|
| `-n`, `--name` | the project, app or package name |
| `-o`, `--output` | where `new` writes the project, `./<name>` by default |
| `-y`, `--yes` | the default of every option not given |
| `--template-source <path>` | a checkout of the template or its `.nupkg`, installed instead of the published package |
| `--hive <folder>` | the template engine's settings folder, passed as `--debug:custom-hive` |
