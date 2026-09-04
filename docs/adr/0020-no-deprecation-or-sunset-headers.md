# 0020 — No `Deprecation`/`Sunset` headers while one version ships

Status: Accepted

## Context

The API is versioned by URL segment — `TodoListsController` is routed at
`api/v{version:apiVersion}/todo-lists` and carries `[Asp.Versioning.ApiVersion("1.0")]` —
and `Program.cs` sets `ReportApiVersions = true`, so every response already carries an
`api-supported-versions` header listing what exists. RFC 8594 defines two further
headers for the case where a version is retiring: `Deprecation`, a date after which a
version is deprecated, and `Sunset`, a date after which it stops being served.

Exactly one version ships today. There is no second version yet, and therefore no
retirement date for the first one — any date this template shipped in code would be
invented, not known.

## Decision

**No `Deprecation` or `Sunset` header is emitted.** `api-supported-versions` keeps doing
the job this template can actually do: telling a client which versions exist right now.

## Consequences

- A client can already discover the current version set from every response; it cannot
  yet learn a retirement date, because none exists.
- Nothing here has to be revisited to add a version — `ReportApiVersions` already reports
  whatever set of versions is registered, so a second version shows up in
  `api-supported-versions` automatically the day it ships.
- A template that emitted these headers today would have to choose between an invented
  date (worse than no header) and an empty header with no date (a header clients would
  quickly learn carries no information and ignore, which is worse than not sending it —
  it looks like a signal and is not one).

## Alternatives rejected

- **Emit `Sunset` with a far-future placeholder date.** A placeholder a deployment forgets
  to update is worse than no header: it reads as a real commitment.
- **Emit the headers with no date, just to have the shape ready.** RFC 8594 requires
  `Sunset` to carry an HTTP-date; a header with no date is not a valid `Sunset` header, so
  this is not actually "the shape ready", it is a malformed header shipped for its own
  sake.

## Revisit when

A second API version ships and the first is given a retirement date. At that point the
dates are a deployment fact — when this specific installation decided to retire v1 — not
a constant in source, so they belong in configuration alongside `ApiVersion`, read at
startup, not hardcoded into the versioning setup.
