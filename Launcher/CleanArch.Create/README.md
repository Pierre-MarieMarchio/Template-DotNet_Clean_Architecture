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

In a terminal it opens on a banner and asks each question on a thread, each choice with its
description beside it and the default selected, and every answer stays on the thread. A dim line
under each question names its keys:

| Question | Keys |
|---|---|
| A choice | `↑`/`↓` move, `enter` selects, a choice's first letter jumps to it |
| Yes or no | `y`/`n`, `enter` takes the default |
| Several steps | `↑`/`↓` move, `space` checks or unchecks, `a` all, `enter` confirms |
| A text | type a value, `enter` keeps the default shown |
| Any but the first | `esc` goes back to the previous question, as do `←` in a list and `backspace` in an empty text |

Going back reopens the previous question with its answer selected, erases the answers after it, and
asks the following questions again, so a changed answer changes what they offer; a question answered
for you, as `--examples none` after `--auth none`, is passed over. A summary of every option comes
before anything is written; answering No to it goes back to the last question, and when every answer
came as a flag it stops without writing anything. `ctrl+c` stops at any point. A spinner runs while
the package installs and `dotnet new` generates, whose output shows only when it fails. With
`--yes`, without a terminal, or with `NO_COLOR` set, the output stays plain: no banner, no thread, no
spinner, the template engine's own lines.

## Set the project up

The last question, every step checked, offers the first steps the project's first run asks for by
hand. Each runs under its own spinner, in this order:

| Step | What it runs |
|---|---|
| First commit | `git init` unless the folder is in a git work tree already, then a commit of the generated files, `chore: generate from cleanarch-webapi <version>` |
| Bootstrap | `dotnet run Tools/Tasks.cs bootstrap`, which needs the network |
| Bootstrap commit | a commit of what bootstrap wrote, `chore: bootstrap` |
| Copy .env | `.env.example` to the git-ignored `.env`, unless one is there |

The commits carry your own git identity; without `user.name` or `user.email` they are skipped, with
the `git config` lines to run. A step that fails says why and leaves the next independent ones to
run, and a commit is skipped when a step before it did not succeed. The commands left at the end are
the ones still to run: the stack's, and those of each step that did not succeed. Without questions,
`--setup` runs every step; without it nothing is set up.

## Add a web app or a package

At the root of a generated project, or in any folder under it:

```bash
dnx CleanArch.Create web-app
dnx CleanArch.Create web-package
```

The project's authentication, workflows and package scope are read from its files. The first web app
also creates the pnpm workspace, and the launcher prints the lines its README asks to add by hand.
Both ask on the same thread, with the same summary, as a new project.

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
| `--setup` | after `new`, every step of [Set the project up](#set-the-project-up), without asking |
| `--template-source <path>` | a checkout of the template or its `.nupkg`, installed instead of the published package |
| `--hive <folder>` | the template engine's settings folder, passed as `--debug:custom-hive` |
