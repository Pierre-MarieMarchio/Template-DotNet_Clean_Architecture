# Launcher audit: `dnx CleanArch.Create`

Audit of the launcher as someone discovering the template would meet it, run for real outside the
repository, on Windows 11, from packages built as `template-release.yml` builds them
("What dnx CleanArch.Create runs"), with every install in a dedicated hive.

## How it was run

- `dotnet pack .template.config/TemplatePackage.csproj` and
  `dotnet pack Launcher/CleanArch.Create/CleanArch.Create.csproj`, then
  `dotnet tool install CleanArch.Create --tool-path <tmp>`, as the release workflow does.
- Every run passed `--template-source <the .nupkg> --hive <tmp hive>`; nothing touched the global
  template installation.
- Interactive runs went through a real Windows pseudo-console (ConPTY, driven from Python with
  `pywinpty`, rendered with `pyte`), at 100 to 110 columns, in the console's default code page
  (850) and in UTF-8.
- Paths run: defaults with no setup; defaults with every setup step (bootstrap included); `--auth none`;
  going back with `esc` after a forced answer; `--web react` and `--web placeholder`; a refused
  combination passed as flags in a terminal; `NO_COLOR`; redirected output; no terminal without
  `--yes`; refused values, unknown options, a bad name, `web-app` outside a project, `--help`.
- Not run for real: **Ctrl+C**. ConPTY started from a process without a console does not deliver
  the interrupt (a control run of `ping -t` ignored it too), so it was only read in the code. See
  [Ctrl+C](#ctrl-c).

## Defects, by severity

Each one: what you see, why it confuses, and the fix. "Fixed" names the test that holds it; every
such test was seen failing once, either on `HEAD` or by putting the defect back.

### Severe

**1. On a default Windows console, the thread is unreadable.** *Fixed.*
- Seen: in code page 850 (the default for a French or Western European Windows), `◆ ◇ ● ○ ◼ ▌` came
  out as `?` or vanished, and since the printed width no longer matched what Spectre counted, each
  answer left two or three stale copies of the question above it and the choice columns broke.
- Why it confuses: the first screen of a first run looks broken.
- Fix: `Terminal.Open()` sets `Console.OutputEncoding` to UTF-8 before the console is created.
- Test: `TerminalTests.ARedirectedOutput_IsUtf8_AndKeepsEachMessageOnOneLine`, which runs the
  launcher under `chcp 850` (red on `HEAD`: byte `0x82` for `é`).

**2. The thread's markers sit off the bar, whatever the code page.** *Fixed.*
- Seen: `◆ ◇ ◼ ◻`, the rounded corners `╭╮╰╯` and the braille spinner `⠋` are not in WGL4, the set
  every console font draws itself, so the terminal takes them from a fallback font that places them
  off the `│` rail.
- Fix: `♦` (asking) and `◊` (answered), `■ □` for the steps, square borders, and a spinner turning
  through box-drawing glyphs (`RailSpinner`).
- Test: `GuidedFlowTests.EveryGlyphDrawn_IsOneConsoleFontsCarry_SoTheThreadStaysAligned` (red with
  `◆`, and with the braille spinner).

**3. Redirected output is hard-wrapped at 80 columns.** *Fixed.*
- Seen: with output piped or logged, errors and the "same, without questions" commands were cut at
  column 80 with trailing spaces, so a copied command no longer ran.
- Fix: with output redirected, the console width is 4096.
- Test: the same `TerminalTests` test (red on `HEAD`).

**4. The setup's commands left could not succeed.** *Fixed.*
- Seen (a real run, `git add` failing on `Filename too long`): the commands left were `git init`
  (although `git init` had succeeded), `git add -A`, the first commit, then a bare
  `git commit -m "chore: bootstrap"` with no `git add` before it: `Distinct()` had dropped the second
  `git add -A`. That commit would fail anyway, since the first commit takes everything bootstrap wrote.
- Fix: `git init` only when it failed or did not run; the bootstrap commit, skipped after a failed
  first commit once bootstrap ran, says the first commit will hold its changes and leaves nothing to
  run; `ProjectSetup.Left` keeps each `git add -A` and lists only `git config` and `git init` once;
  a `Filename too long` failure explains itself and offers `git config --global core.longpaths true`.
- Tests: `ProjectSetupTests.AFailedFirstCommit_AfterASuccessfulInit_LeavesNoInit_AndItsCommitTakesWhatBootstrapWrote`,
  `ProjectSetupTests.TheCommandsLeft_StageBeforeEachCommit_AndConfigureGitOnce`.

### Major

**5. A forced answer told you to type a flag.** *Fixed.*
- Seen: `Example features  none · --auth none refuses the example features, which each belong to a
  caller: add --examples none.` The launcher had already answered for you; "add --examples none" is
  an instruction for the command line.
- Fix: `none · the only option when Authentication is none`, from the options behind the refusal.
- Tests: `GuidedFlowTests.ARefusedAnswer_IsAnsweredOnTheThread_WithTheRefusalThatLeavesIt`,
  `QuestionnaireTests.InATerminal_AuthNone_AnswersWhatItRefuses_AndOffersNoAdministrators`,
  `QuestionnaireTests.InATerminal_AWebAppOfAPublicApi_IsForItsUsers`.

**6. A taken output folder was refused after every question and the summary.** *Fixed.*
- Seen: answering a dozen questions, confirming the summary, then `'…' is not empty: choose another
  --output.` and a stop, in a terminal where `--output` had never been typed.
- Fix: the folder is checked at its own question (`That folder already has files in it.`), and an
  app name already in `Src/Web/apps` at the app-name question. The late check stays for flags.
- Tests: `GuidedFlowTests.AnOutputFolderWithFiles_IsRefusedAtItsQuestion_NotAfterTheSummary`,
  `GuidedFlowTests.AnAppNameTaken_IsRefusedAtItsQuestion`.

**7. The last block printed raw Markdown.** *Fixed.*
- Seen: backticks, `| Service | Address |` and `|---|---|`, `<http://localhost:8080>`,
  `[First run](docs/getting-started/first-run.md)`, and in plain mode the ```` ```bash ```` fences.
- Fix: `ReadmeText` turns the README section into text: no backticks, a link as
  `First run (docs/…)`, an autolink as its URL, a table as aligned columns, fences dropped.
- Tests: `ReadmeTextTests` (three tests), and `GuidedFlowTests.WithYes_…` asserts no fence.

**8. `--help` did not list a single option of the template.** *Fixed.*
- Seen: "Every option of the template is a flag of the same name, e.g. --auth none". Nothing said
  which options exist or what they take.
- Fix: `--help` ends with each project option, the values the template generates (`mysql` left
  out, as the questions leave it out) and its default.
- Test: `LauncherTests.TheHelp_ListsTheTemplatesOptions_WithTheValuesItGenerates`.

**9. The "same, without questions" commands dropped `--hive`.** *Fixed.*
- Seen: a run with `--hive` printed commands that would install into the global settings.
- Fix: the launcher line keeps `--hive`, the `dotnet new` line gets `--debug:custom-hive`.
- Test: `LauncherTests.TheCommandsPrinted_KeepTheHiveGiven`.

**10. PR #100: `dotnet new` would ask you to run bootstrap just before the launcher runs it.** *Fixed ahead.*
- Seen (from `gh pr diff 100` and a template carrying the same post action): `dotnet new` prints
  `Processing post-creation actions...`, `Description: …`, `Manual instructions: In the project folder,
  run dotnet run Tools/Tasks.cs bootstrap…`. In plain mode the launcher relays it, then runs
  bootstrap itself.
- Fix: `RelayedOutput` leaves out an instruction naming the command the launcher is about to run,
  with its description, and the heading when nothing is left under it. Without Bootstrap among the
  steps, it is relayed as is. The guided thread never showed it.
- Tests: `RelayedOutputTests` (three tests), `GuidedFlowTests.WithYesAndSetup_EveryStepRuns_InPlainLines`
  and `GuidedFlowTests.WithYes_NothingIsSetUp_AndTheOutputIsTheTemplateEnginesThenTheCommands`.

### Minor

**11. Windows paths shown with backslashes.** *Fixed.* The default output folder was `.\Name`
(quoted as `-o ".\Name"` and `cd ".\Name"`) and the web app's README `Src\Web\README.md`, where
the help and the docs say `./<name>` and `Src/Web/README.md`. Both are now forward slashes.
Tests: `GuidedFlowTests.ANewProject_IsABannerThenAThread_ASummary_TheSetUp_AndTheCommandsLeft` and
`GuidedFlowTests.AWebApp_ReadsTheProject_AndOffersAPublicApiNoAdministrators`, both red on `HEAD`
on Windows.

**12. A refused combination of flags did not say what happens next.** *Fixed.* The warning now ends
with `Asking again: Authentication, Example features.` Test:
`QuestionnaireTests.InATerminal_ARefusedCombination_IsExplained_ThenItsOptionsAreAskedAgain`.

**13. The wording read as generated text.** *Fixed.* Every string the launcher owns was rewritten
in plain English: errors (`Unknown option --databse.`, `'bogus' isn't a command.`, `Couldn't start
git. Is it installed and on your PATH?`), the name rules (`use letters, digits and dots, with each
part starting with a letter, like Acme.Shop.`), the setup steps, the closing (`All set. Now run:`,
`Next time, skip the questions with`), the summary row (`Setup steps`). A rule shown under the input
starts with a capital. Tests: the flow and questionnaire tests quote the new texts.

**14. The banner.** *Changed, at the owner's choice.* A square frame with `CLEANARCH` in block
letters, sky-blue to violet, the version in the frame and `Clean Architecture for .NET 10, minus the
setup.` under it; below 80 columns, a single coloured line. Test: the banner assertions of
`GuidedFlowTests.ANewProject_…`.

**15. Two tests failed on Windows on `HEAD`, a third by luck.** *Fixed.* Besides the two in item
11, `LauncherTests.AnExistingOutputFolder_WithFiles_IsRefusedBeforeAnythingIsInstalled` depended on
the temporary path's length: past 80 columns the message wrapped and the assertion missed it. The
tests that read a message now set the console width.

## Decisions for the owner

These change the flow, a default or the template, so they were left as they are.

1. **`--yes` and a question left with one answer.** In a terminal, `--auth none` answers
   `--examples none` and `--files false` by itself. With `--yes`,
   `dnx CleanArch.Create -n X --auth none --yes` stops with "The template can't generate a project
   with these options", because `--yes` takes the defaults `all` and `true`. Proposal: `--yes` takes
   the only value left when the template leaves one, as the questions do.
2. **Every setup step is checked by default.** Bootstrap takes minutes and needs the network, and the
   two commits use the user's global git identity in a fresh repository. A newcomer pressing
   `enter` gets all of it. Keep, or check only the commit and `.env`?
3. **Option descriptions in `template.json`** (out of scope here, shown on every question). Proposals:
   - `--database`: "A project carries that provider only; MySQL is not generated by this version of
     the template." names a choice that is never offered. Proposal: "The database the project runs
     on. The project only carries this one."
   - `--auth` `none`: "Needs --examples none and --files false, whose features belong to a caller."
     The launcher answers both for you. Proposal: "No accounts: every endpoint is public. Leaves out
     the example features and the Files module."
   - `--auth`: "Who the API's callers are. none generates no Auth module and makes every endpoint
     public on purpose." Proposal: "How callers sign in to the API."
   - The refusal descriptions end with "add --examples none" or "choose --database postgresql": right
     for `dotnet new` and the launcher's flag errors, which is where they still show.
4. **`(refused by --auth-none-needs-examples-none)`** in a flag error names a hidden parameter that
   means nothing to a newcomer; `using-the-template.md` lists those names in "Refused by". Keep it
   for the cross-reference, or drop it from the launcher?
5. **"All set." after a failed step.** The closing heading says "All set. Now run:" even when the
   first commit failed; the failed step is right above, in yellow. Proposal: "Almost there. Now run:"
   when a step did not succeed.
6. **"Next time, skip the questions with" after `--yes`.** Printed even when every answer came as a
   flag, where it repeats the command just typed. Drop it when nothing was asked?
7. **The generated README after PR #100.** With a React or Angular app, the "Run it" section will
   say the app's folder holds only `scaffold.json` "until bootstrap runs". After the launcher ran
   bootstrap, that sentence is shown in the closing block although it no longer applies. The README
   needs either no such sentence or one the launcher can leave out.
8. **"once, after a fresh clone; commit what it changes"** on the bootstrap line of the generated
   README reads wrong right after generating; PR #100 changes it to "once, right after generating".
9. **The WGL4 glyph rule** is held by a test and stated in the launcher's README. It is a choice
   another team could make otherwise: does it deserve a template ADR?
10. **Paths too long for git on Windows.** The deepest generated path is about 135 characters
    (`Tests/Integration/<Name>.Infrastructure.Auth.IntegrationTests/Features/Sessions/Adapters/…`). In
    a folder deeper than about 120 characters, the first commit fails without `core.longpaths`. The
    launcher now says so; shortening those folders would be a template change.

## Ctrl+C

Read in the code only (see "How it was run"). `Program.cs` restores the cursor on `CancelKeyPress`
and lets the process end. A live question is left on screen as drawn, with no closing line, and a
`dotnet new` or bootstrap under a spinner is interrupted with it (same console group). Nothing is
written before the summary, so an interrupt during the questions leaves no files. During setup it
can leave a project without its commits: rerunning the commands of the closing block is the way
back, but none is printed. Worth a real check in Windows Terminal and on Linux.

## Also seen

- `dotnet test --solution Launcher/CleanArch.Create.slnx -m:2` runs **no test** (exit code 5): the
  testing platform forwards `-m:2` to the xunit executable, which reads it as a filter. Build with
  `-m:2`, then run `dotnet test --no-build`, or run without `-m`.
- The checkout's branch changed under this session (from `develop` to
  `dependabot/nuget/dot-config/develop/dotnet-ef-10.0.14`, rebased and committed by another
  session). The launcher's files are identical on both, so the changes stay valid; they sit in the
  index of whichever branch is checked out.
