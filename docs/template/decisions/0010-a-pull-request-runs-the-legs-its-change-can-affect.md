---
type: decision
title: 0010. A pull request runs the legs its change can affect
description: Why a pull request runs only the acceptance legs its changed paths can affect, and how a selector chooses them.
status: Accepted
---

# 0010. A pull request runs the legs its change can affect

Status: Accepted

## Context

The acceptance matrix generates every shape the options produce and gates each one as an adopter
would. Its web legs build images and start the whole stack, so with every leg on every pull request
to `develop`, a page of template documentation waits as long as a change to the domain.
The `develop` ruleset requires each leg's check by name, `Template acceptance (<leg>)`. A workflow
filtered by `paths:` never starts, and a job skipped by its `if:` reports one check under its
unexpanded name, so either leaves the required checks pending and the pull request blocked. Most
changes reach only some legs: a starter reaches the legs that generate it, an overlay its provider's
legs, a template-only page none.

## Decision

- **A selector chooses the legs.** `.template.config/acceptance/SelectLegs.cs` reads the pull
  request's changed paths, the rules of `.template.config/acceptance/rules.json` (path globs to tags,
  `none` or `all`, the first match deciding) and the legs of `.template.config/acceptance/matrix.json`
  with their tags, and runs the legs carrying a tag a path names.
- **Every leg reports.** The `template` job's matrix is the legs to run, and a `skipped` job of the
  same name reports each other leg green with one echo step, so every required check arrives.
- **Doubt runs everything.** A path no rule matches, a change to the selector, its data or the
  workflow, an empty or unreadable diff, the `full-acceptance` label, a manual run, a push to `main`
  and the nightly run of `develop` run every leg.
- **`develop` is proved whole once a night.** A push to `develop` starts no run; the scheduled run
  proves the merged tree, and only when `develop` has moved since the last scheduled run that
  finished green or red, so a quiet day costs no leg.
- **A label other than `full-acceptance` runs no leg.** The run it starts chooses none and reports
  none, so no required leg turns green unproved, and it runs in a group of its own, so it cancels no
  run in progress; the `full-acceptance` label alone runs every leg.
- **Tests hold the data.** `AcceptanceSelectionTests` checks the legs against the required checks,
  every starter and variant folder for a rule, every tag for a leg, every leg's tags against its
  options, and every `none` path against the package's exclusions; `Tools/CheckWorkflows.cs`
  refuses a `paths:` filter and a job-level `if:` on a pull request's matrix job that no job of the
  same name complements.

## Alternatives considered

- **Every leg on every pull request.** Nothing to keep in step, but the slowest leg sets the pace of
  every change, a typo in a template page included.
- **A `paths:` filter per workflow.** No code, but a workflow that does not start leaves its required
  checks pending, and one workflow per leg would duplicate the steps once per leg.
- **A fast subset on pull requests, the full matrix nightly.** A break in a skipped shape is found
  after the merge, by someone else, instead of on the change that made it. Only the run after the
  merge is nightly here: the pull request keeps every leg its change can reach.
- **The full matrix on every push to `develop`.** Each merge would hold every runner the account
  allows for the length of the slowest leg, and the pull requests open at the time would wait behind
  it, for a tree whose changed paths its own pull request has already proved leg by leg.
- **Dropping the `labeled` type.** The `full-acceptance` label would start nothing; GitHub offers no
  filter on the label name before the workflow starts.
- **A job-level `if:` that skips the whole run on a label.** The jobs skipped report under their
  own names, which the ruleset requires, and could replace a red check with a skipped one.
- **Fewer required legs.** Optional legs pass unnoticed when red; the gate would prove less.
- **A path-filter action from the marketplace.** It answers which paths changed, not which legs they
  reach, and still needs the complement job; the rules would live in the workflow, untested.

## Consequences

- A change that reaches some legs waits for those alone; one under `Src`, `Tests` or the shared pages
  still runs them all, as every leg builds them.
- A new leg, starter or variant folder costs edits outside the workflow: its entry and tags in
  `.template.config/acceptance/matrix.json`, its rule in `.template.config/acceptance/rules.json`,
  and, for a required leg, the list in `AcceptanceSelectionTests` and the ruleset, which the test
  cannot read.
- A rule that says `none` or a tag too narrow lets a broken shape merge; the nightly run catches it
  the next night, with up to a day's merges to search, instead of at the merge.
- A red night reaches the person who last changed the cron line by GitHub's mail, if they receive
  workflow failures, and nobody else; the run stays under the Actions tab of `develop`.
- A label other than `full-acceptance` re-runs `Pack the template package` and
  `Template refuses unsupported options`, whose names the ruleset requires, on the same tree.
- The skipped legs show green on the pull request: a reviewer reads `Select the acceptance legs` to
  know which legs ran.

## Revisit when

The selection misses a break that the nightly run then finds, a night's merges are too many to search
for the one that broke it, the account's runner limit rises enough for a full run on each push to
`develop`, or GitHub lets a required check be satisfied by a skipped job or filters a workflow by
label.
