# 0028. The author answers for every line

Status: Accepted

## Context

Assistants write code, commit messages and pull request descriptions, and most of them sign what
they write by default: a `Co-Authored-By` trailer naming the tool, a "Generated with" line, a robot
emoji. GitHub renders that trailer as a co-author with the tool's avatar, which claims a
co-authorship a tool cannot carry. The signature says which tool produced a change; it says nothing
about who understands it. A tool cannot be asked why a line is there, cannot be woken when it breaks,
and carries no responsibility for it.

A reviewer still benefits from knowing where a tool wrote code, since that is where to look harder.
A team could reasonably record the tool in the history, in the pull request, in both, or say
nothing.

## Decision

- **Commits, squash-merge messages and pull request descriptions name their human author only.**
  They carry no `Co-Authored-By` trailer naming an AI tool, whichever tool it is, no "Generated
  with" line and no emoji signature. The rule is for commits and pull requests written by people,
  with or without a tool; a bot that opens its own, such as Dependabot or release-please, keeps its
  messages as it writes them, emoji included, since a bot did write them.
- **The person who opens the pull request answers for every line of it**, generated or not, and
  understands it well enough to explain and maintain it. Code only a tool understands has no place
  here, and a reviewer may ask the author to explain any line.
- **When a tool wrote part of a change, the author should say so in two places**, a recommendation
  and never a requirement:
  - a commit trailer, `Assisted-by: AGENT_NAME:MODEL_VERSION [TOOL1] [TOOL2]`, for example
    `Assisted-by: Claude:claude-opus-5-5`. `AGENT_NAME` is the tool or framework and `MODEL_VERSION`
    the exact model; optional analysis tools may follow, and basic tools such as git, compilers and
    editors are not listed. GitHub does not render the trailer, so no tool shows as a contributor, and
    git keeps it for `git log --grep` and `git blame`. A squash merge keeps the trailer in its message.
  - an "Assistance" section in the pull request: which tool, which parts it wrote (files or
    functions), which parts were written by hand, and how they were checked.
  Nothing is declared when no tool wrote code; a one-line completion does not count.
- **The rule is written where assistants read it.** The pull request template carries it, and
  [Contributing](../../CONTRIBUTING.md#pull-requests) states it. `PullRequestTemplateTests` holds the
  template.
<!--#if (agentFiles)-->
- **`AGENTS.md` tells the agents themselves** to add the `Assisted-by` trailer and never a
  `Co-Authored-By`.
<!--#endif-->

## Alternatives considered

- **`Co-Authored-By` the tool**: it shares on paper a responsibility a tool cannot carry, GitHub
  shows the tool as a contributor, and it gives a reviewer nothing to act on.
- **The trailer alone, as the Linux 7.2 coding-assistants guidance asks** (`Documentation/process/coding-assistants.rst`): it
  forbids an agent from adding `Signed-off-by`, since only a human certifies the origin of a
  contribution and takes full responsibility for it, and it asks that the `Assisted-by` tag state
  the assistance, after rejecting `Co-developed-by`. Alone, it tells a reviewer that a tool took
  part but not which parts.
- **The description alone**, the direction of the kernel's in-development "Tool-Generated Content"
  guidelines: transparency about the tools, the prompts, the portions affected and the testing, "if
  in doubt, choose transparency". Alone, it is lost with a squash merge or a change of host, while
  git keeps a trailer.
- **Saying nothing**: assistants add the `Co-Authored-By` trailer by default, so a rule left unwritten
  is broken by the first change one of them writes.

## Consequences

- A commit or a pull request that shows a tool as a co-author or signs itself with one is amended
  before it merges; the author is the one asked.
- An author uses any tool, and none is forbidden. The cost is on the author: a change is read, run
  and understood before it is proposed.
- The history can be searched for assisted changes, and a reviewer reads the Assistance section to
  know where to look harder. Declaring is a recommendation: no test can tell whether a tool wrote a
  line, or who understood a diff.
- `PullRequestTemplateTests` holds only that the template exists, forbids the attribution, carries
  the Assistance section and names the trailer.

## Revisit when

A reviewer needs to know which tool wrote a change to judge it, or the platform hosting this project
records tool assistance itself, apart from the commit message.
