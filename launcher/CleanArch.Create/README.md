# CleanArch.Create

Creates a .NET 10 project on Clean Architecture, an HTTP API or hosts without one, by asking for
each option of the `cleanarch-webapi` template, then runs `dotnet new` with the answers. It creates
projects and nothing else: to add a web app, a web package or a presentation project to one, run
`dotnet cleanarch add web-app`, `add web-package` or `add presentation` in it. Run inside a project,
the launcher says so.

## Create a project

You need a .NET 10 SDK (10.0.100 or later), which runs a tool without installing it:

```bash
dnx CleanArch.Create
```

The launcher asks for the name, then each option, with the template's own choices, defaults and
descriptions. The name it offers is the current folder's, made a valid project name (`my-shop`
becomes `MyShop`, `acme.shop` becomes `Acme.Shop`), or the template's default when that fails.
Taking it with `enter` generates the project in the current folder; another name generates it in a
folder of that name under the current one. When the target folder already holds files, `.git`
aside, it asks, as `create-vite` does: `Cancel operation`, the default; `Remove existing files and continue`, which
deletes everything but `.git` once the summary is accepted; or `Ignore files and continue`, which
generates over them with `dotnet new --force`, the template's files replacing those of the same name
and the others staying. `--overwrite` answers `Remove` without asking. It offers only what the template generates with the answers given so far:
a choice the template refuses whatever follows is never offered, and a
question left with a single answer, as `--examples none` after `--auth none`, is answered with it and
the answer that leaves it. A combination of flags the template refuses is explained before anything
is written, and its options are asked again. A project name the template refuses, one under two
characters or one that does not start with a letter from A to Z, or a project name with a character other than the letters
A to Z, digits and dots, such as an underscore, is refused at its own question. It installs the `CleanArch.WebApi.Templates`
package at its own version when another one or none is installed, generates the project, then prints
the commands that do the same, with `--template-source` and `--hive` when they were given, and the
first steps of the project's `README.md`, as plain text. After questions, those commands are the
launcher's own, with `--yes`, and `dotnet new`'s; without questions, only `dotnet new`'s. The last
line says `All set.`, or `Almost there.` when a step did not succeed.

In a terminal it opens on a banner and asks each question on a thread, each choice with its
description beside it and the default selected, and every answer stays on the thread. The thread
uses only glyphs every console font carries, so no marker is drawn off the rail by a fallback font,
and the output is UTF-8 whatever the console's code page. A dim line under each question names its
keys:

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
came as a flag it stops without writing anything. `ctrl+c` stops at any point: before the summary
nothing is written, and during the setup the launcher prints the commands that finish it. A spinner
runs while the package installs and `dotnet new` generates, whose output shows only when it fails. With
`--yes`, without a terminal, or with `NO_COLOR` set, the output stays plain: no banner, no thread, no
spinner, the template engine's own lines.

## Set the project up

The last question, every step checked, offers the first steps the project's first run asks for by
hand. Each runs under its own spinner, in this order:

| Step | What it runs |
|---|---|
| First commit | `git init` unless the folder is in a git work tree already, then a commit of the generated files, `chore: generate from cleanarch-webapi <version>` |
| Bootstrap | `dotnet tool restore`, then `dotnet cleanarch bootstrap`, which need the network; its line names what it creates for the options chosen |
| Bootstrap commit | a commit of what bootstrap wrote, `chore: bootstrap` |
| Copy .env | `.env.example` to the git-ignored `.env`, unless one is there |

The commits carry your own git identity; without `user.name` or `user.email` they are skipped, with
the `git config` lines to run. A step that fails says why and leaves the next independent ones to
run, and a commit is skipped when a step before it did not succeed. The commands left at the end are
the ones still to run: the stack's, and those of each step that did not succeed. When Bootstrap is
chosen, the plain output leaves out the instructions `dotnet new` ends with, which ask for it. Without
questions, `--setup` runs every step; without it nothing is set up.

## Without questions

Every question is a flag named like the template's option; `--yes` takes the default of each one not
given, or the only value the template leaves it, as `--examples none` and `--files false` after
`--auth none`. Without `--yes`, a choice with no default takes its only value the same way. Each value
taken this way is printed with the answer that leaves it. Without a terminal, every answer is a flag or `--yes` is passed. `dnx` takes `--yes`,
`--help` and `--version` for itself, so every argument of the launcher follows `--`;
`dnx CleanArch.Create -- --help` lists the options with the values the template generates and their
defaults.

```bash
dnx CleanArch.Create -- new -n Acme.Shop --auth external --examples notes --yes
```

| Option | Meaning |
|---|---|
| `-n`, `--name` | the project's name, the current folder's by default |
| `-o`, `--output` | where the project is written: `./<name>` when `-n` is given, the current folder otherwise; a folder that holds files is refused without questions, unless `--overwrite` is given |
| `--overwrite` | empties a target folder that holds files, `.git` excepted, without asking, then generates in it |
| `-y`, `--yes` | the default of every option not given, or the only value the template leaves it |
| `--setup` | after creating, every step of "Set the project up" above, without asking |
| `--template-source <path>` | a checkout of the template or its `.nupkg`, installed instead of the published package |
| `--hive <folder>` | the template engine's settings folder, passed as `--debug:custom-hive` |
| `-h`, `--help` | the options, with the values the template generates and their defaults |
