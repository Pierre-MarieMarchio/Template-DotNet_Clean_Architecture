# 0024. The worker takes the jobs out of the API

Status: Accepted; supersedes [0012](0012-where-the-jobs-run-is-an-option.md)

## Context

A project's background jobs run in the API unless a worker runs them
([the project's ADR 0011](../../decisions/0011-jobs-run-in-the-api-under-a-lease.md) and
[ADR 0025](../../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)). A worker is a host, as a
portal or an MCP server is, with a name, a port, a Compose service, an item template and a join
task; but it does not sit beside the API: it takes the jobs out of it, so generating it means
generating the API without them. The jobs live in this checkout once, in the API, where they build
and are tested, and a project generated without the worker must be able to take one later, when its
jobs outgrow the API.

## Decision

- **The worker is a value of `--presentation`.** `--presentation worker` writes `<Project>.Worker`
  from `.template.config/starters/worker`, moves the API's `Common/Jobs` and `Features/*/Jobs`
  folders and their unit tests into it and its test project, and conditions the API's composition,
  settings and Compose environment; the API keeps the maintenance routes. Without it the jobs run in
  the API, and a project whose hosts hold neither the API nor the worker gets the worker
  ([0020](0020-a-project-is-the-presentation-projects-it-holds.md)).
- **The move is two sources of `template.json`.** The API's source excludes the folders under
  `jobsWorker`, and two more copy them under the worker and its tests. Each namespace the moved
  files declare is renamed by a generated switch symbol whose value is the host's
  (`jobsNamespace<Folder>`), since a replacement given as a custom operation is not applied to the
  files a glob selects.
- **A server database only.** The worker is refused with `--database sqlite`
  (`workerNeedsServerDatabase`): an in-process lease cannot exclude the API's maintenance routes from
  the worker's pass, and a SQLite file serves one process.
- **The worker has a fixed name**, `<Project>.Worker`: `cleanarch-presentation --framework worker`
  takes no other `-n` (`workerIsNamedWorker`), and `presentationNameIsTaken` refuses `Worker` as the
  name of any other presentation project.
- **The worker is added later by an item template and a task.** The engine adds files and never moves
  or edits the project's own, so `cleanarch-presentation --framework worker -n Worker` writes the
  starter and ADR 0025, refusing a project without the API (`workerTakesTheJobsOfTheApi`), and the
  project's `presentation-join` does the rest of what generation does: it moves the jobs and their
  tests, renames their namespaces by the switch symbols' rule, and makes the edits the starter's
  `README.md` lists, which set ADR 0011 to `Superseded by` ADR 0025 rather than remove it, since a
  decision is never erased. It refuses while git reports a change to a file it would touch, since a
  move is undone by `git restore` alone, and builds the joined project in a copy first, so a job that
  needs a type of the API leaves the project unchanged.
- **Tests hold the move and the addition.** `TemplatePackagingTests` fails when a namespace declared
  under a moved folder has no switch symbol, or when the folders the worker's sources copy differ
  from those the API's source excludes. A launcher test generates the project with the worker and
  without it, adds the worker to the second, and requires the same tree but for ADR 0011, kept and
  superseded, and its row in the decisions index.

## Alternatives considered

- **An option of its own, `--jobs api|worker` or `--worker true|false`.** A second question about
  hosts beside the one `--presentation` asks, and a name that reads as an addition while it removes
  the jobs from the API.
- **No item template for the worker, the jobs moved by hand.** A page of steps no test runs, and a
  project that can never take the worker its jobs outgrow the API for.
- **Copies of the jobs in the worker's starter.** Every pass twice in the template, and the two
  copies drift.
- **The jobs in a library both hosts reference.** A project in every generated project, there only to
  be moved between hosts.
- **The jobs in whichever server host is chosen.** Every server host would carry the jobs' sources,
  settings and leases under a condition of its own, for a shape the worker gives.

## Consequences

- The jobs are the API's in this checkout. The worker's shape is compiled only in a generated
  project, so the acceptance legs that pass `--presentation worker` prove it, the generator round
  trip among them, and the launcher test that adds it builds it through the join.
- A line conditioned on `jobsWorker` and on another presentation project cannot be told apart by the
  item template, which does not know the project's other hosts, so such a line is conditioned on
  `jobsWorker` alone.
- A job folder under a new feature needs its switch symbol before a project generated with
  `--presentation worker` builds, and `TemplatePackagingTests` says so.

## Revisit when

The engine applies a replacement to the files of one source only, or a second kind of work leaves
the API as the jobs do.
