# 0001. Options remove parts and refuse combinations

Status: Accepted

## Context

The template generates one project from several options: the authentication mode, the examples,
Files, CI, Sonar, a web app and its audience. Each option could switch a part off at run time, as a
flag the project carries for ever, or remove it at generation. Some combinations also make no
sense: example features and Files belong to a caller, so they cannot exist in a public API without
accounts, and a back-office web app needs administrators to sign in. The `dotnet new` engine has no
constraint between parameters.

## Decision

- **An option removes, it does not disable.** What an option leaves out is absent from the
  generated project: its code, tests, configuration sections, documentation and ADRs. Whole paths
  are excluded by conditions in `template.json`; content shared by several shapes is conditioned
  with template markers.
- **A forbidden combination is refused at generation.** Each refusal is a hidden parameter that the
  combination makes required: `authNoneNeedsExamplesNone`, `authNoneNeedsFilesFalse`,
  `adminWebNeedsAccounts`. The engine stops and prints the parameter's description, which names the
  option to change.
- **A refusal that slips through cannot build.** The same combinations select the `refused`
  variant, which adds a source file holding an `#error` naming the fix.
- **An option that names parts may take several values.** `--presentation` names each host the
  project holds, repeated once per value; a condition reads `presentation == "<value>"` as "among
  the values", and a refusal may read a combination of them
  ([0018](0018-a-project-is-the-presentation-projects-it-holds.md)).

## Consequences

- A generated project has no flag to discover and no dead branch to delete; what it received is
  what it runs.
- Every option multiplies the shapes the template must prove. The acceptance workflow generates,
  builds and tests a leg per shape that matters, and a separate job asserts that each refused
  combination exits non-zero and writes nothing.
- Every conditioned page is judged in every form it can take.
- Adding an option is a symbol, its path conditions, its markers and an acceptance leg, as
  [Maintaining the template](../maintaining-the-template.md#adding-an-option) lists.

## Revisit when

The engine gains a constraint between parameters, which would replace the hidden parameters, or the
number of legs needed to prove the shapes outgrows what CI can run.
