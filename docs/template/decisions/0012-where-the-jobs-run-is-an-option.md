# 0012. Where the jobs run is an option

Status: Accepted

## Context

`--presentation` writes one project beside the API: a server host on the application layer or a
desktop client of the API, with its name, port, Compose service, item template and join task. A
worker for the background jobs is a host too, but it does not sit beside the API: it takes the jobs
out of it, which [the project's ADR 0011](../../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
places there, so generating it means generating the API without them.

## Decision

- **Where the jobs run is an option.** `--jobs api|worker`, `api` by default. `worker` writes
  `<Project>.Worker` from `.template.config/starters/worker`, moves the API's `Common/Jobs` and
  `Features/*/Jobs` folders and their unit tests into it and its test project, and conditions the
  API's composition, settings and Compose environment; the API keeps the maintenance routes.
- **The move is two sources of `template.json`.** The API's source excludes the folders under
  `jobsWorker`, and two more copy them under the worker and its tests. Each namespace the moved
  files declare is renamed by a generated switch symbol whose value is the host's
  (`jobsNamespace<Folder>`), since a replacement given as a custom operation is not applied to the
  files a glob selects.
- **A server database only.** `--jobs worker` is refused with `--database sqlite`
  (`workerNeedsServerDatabase`): an in-process lease cannot exclude the API's maintenance routes from
  the worker's pass, and a SQLite file serves one process.
- **The worker has a fixed name**, `<Project>.Worker`, which `presentationNameIsTaken` refuses as a
  presentation's name.
- **No item template moves the jobs.** The engine adds files and never moves or edits the project's
  own; the background jobs page says how to move them by hand.
- **Tests hold the move.** `TemplatePackagingTests` fails when a namespace declared under a moved
  folder has no switch symbol, or when the folders the worker's sources copy differ from those the
  API's source excludes.

## Alternatives considered

- **`--worker true|false`.** It names a project rather than the choice it makes, and reads as an
  addition while it removes the jobs from the API.
- **`--presentation worker`.** A project could not have a worker beside a Blazor app, and a
  presentation adds a host without taking anything from the API.
- **`--host` with several values.** One option for hosts that share nothing: a worker changes where
  existing code runs, a presentation adds a project.
- **Copies of the jobs in the worker's starter.** Every pass twice in the template, and the two
  copies drift.
- **The jobs in a library both hosts reference.** A project in every generated project, there only
  to be moved between hosts.

## Consequences

- The jobs are the API's in this checkout. The worker's shape is compiled only in a generated
  project, so the acceptance legs that pass `--jobs worker` prove it, the generator round trip
  among them.
- A job folder under a new feature needs its switch symbol before a project generated with
  `--jobs worker` builds, and `TemplatePackagingTests` says so.
- Adding an option costs what [Maintaining the template](../maintaining-the-template.md#adding-an-option)
  lists.

## Revisit when

The engine applies a replacement to the files of one source only, or a second kind of work leaves
the API as the jobs do.
