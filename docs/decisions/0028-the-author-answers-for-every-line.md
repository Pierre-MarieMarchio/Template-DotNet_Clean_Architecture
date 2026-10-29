# 0028. The author answers for every line

Status: Accepted

## Context

Assistants write code, commit messages and pull request descriptions, and most of them sign what
they write by default: a `Co-Authored-By` trailer naming the tool, a "Generated with" line, a robot
emoji. The signature says which tool produced a change; it says nothing about who understands it. A
tool cannot be asked why a line is there, cannot be woken when it breaks, and carries no
responsibility for it.

A team could reasonably record the tool in the history for transparency, or say nothing and let each
author decide.

## Decision

- **Commits, squash-merge messages and pull request descriptions name their human author only.**
  They carry no `Co-Authored-By` trailer naming an AI tool, whichever tool it is, no "Generated
  with" line and no emoji signature.
- **The person who opens the pull request answers for every line of it**, generated or not, and
  understands it well enough to explain and maintain it. Code only a tool understands has no place
  here, and a reviewer may ask the author to explain any line.
- **The rule is written where assistants read it.** The pull request template carries it as a
  checklist line and [Contributing](../../CONTRIBUTING.md#pull-requests) states it.
  `PullRequestTemplateTests` holds the template.
<!--#if (agentFiles)-->
- **`AGENTS.md` repeats the rule for the agents themselves.**
<!--#endif-->

## Alternatives considered

- **`Co-Authored-By` the tool**: it shares on paper a responsibility a tool cannot carry, and it
  gives a reviewer nothing to act on.
- **An `Assisted-by: <agent>:<model>` tag, as the Linux kernel asks**: the kernel's guidance on
  coding assistants forbids an agent from adding `Signed-off-by`, since only a human certifies the
  origin of a contribution and takes full responsibility for it, and it asks for the tag for
  transparency, after rejecting `Co-developed-by`. This project keeps the human responsibility and
  drops the tag: what a reviewer needs is an author who can explain the change, not the name of the
  tool behind it.
- **Saying nothing**: assistants add the trailer by default, so a rule left unwritten is broken by
  the first change one of them writes.

## Consequences

- A commit or a pull request that shows a tool's signature is amended before it merges; the history
  holds no tool name, and the author is the one asked.
- An author uses any tool, and none is forbidden. The cost is on the author: a change is read, run
  and understood before it is proposed.
- The rule is a convention of review and a template line; no test can tell who understood a diff.
  `PullRequestTemplateTests` holds only that the template exists and carries the rule.

## Revisit when

A reviewer needs to know which tool wrote a change to judge it, or the platform hosting this project
records tool assistance itself, apart from the commit message.
