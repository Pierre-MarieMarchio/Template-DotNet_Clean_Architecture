# 0028 — A folder vocabulary for every project, not only four

Status: Accepted — **amends [0025](0025-closed-folder-vocabulary-per-layer.md)**

## Context

[0025](0025-closed-folder-vocabulary-per-layer.md) closed the set of folder names a feature may
hold, per layer, and said "nothing outside the set without a new ADR". Its *Revisit when* named the
trigger precisely: "A feature needs a folder outside this vocabulary for something that is not the
repository/store split 0024 already covers — that is the signal to write the next ADR, not to open
the folder quietly." Two things turned up at once that the record could not answer.

**A counter that belongs to a feature and to no table.** `ReminderDiagnostics` is the adapter behind
`IReminderDiagnostics`: one OpenTelemetry counter, `apptemplate.reminders.missed_cancellations`,
which [0026](0026-correctness-does-not-depend-on-event-delivery.md) relies on to make the absent
outbox observable. It has no dependency on EF Core, on `AppDbContext`, or on a connection — its only
imports are `System.Diagnostics.Metrics` and its port's namespace. It sat in an `Observability`
folder under the persistence project's `Common`, which is wrong
twice over: `Common/` is the half of the persistence project that is supposed to know no feature,
and this file names one in its type, its counter, and its port.

Nothing caught it. `ModuleDependencyTests.ThePersistenceMechanisms_KnowNoFeature` forbids a
dependency on `AppTemplate.Domain.Features.TodoLists` or on
`AppTemplate.Infrastructure.Persistence.Features` — and `ReminderDiagnostics` depended on neither.
It reached its feature through `AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics`,
a namespace the forbidden list never mentioned. The rule was checking two of the three doors.

**A whole project outside the vocabulary.** `LayoutConventionTests._vocabulary` listed four
projects: Application, Domain, Persistence, Api. `AppTemplate.Worker` was not among them, and its
guard — `checkedLayers.ShouldBe(_vocabulary.Count)` — compares the count of layers walked to the
count of entries in the dictionary, so it detects a `Features/` directory that has disappeared and
never a project that was never listed. The Worker had drifted accordingly: its two background loops
lived in `Common/Maintenance/` and `Common/Reminders/`, putting two feature names under the one word
that means "transverse to every feature", in the only project in `Src/Presentation/` with no
`Features/` directory at all. `CONTRIBUTING.md` promised "Four layers, and each one has the same
shape" over a table that omitted the Worker entirely.

## Decision

**`Observability/` joins the persistence layer's feature vocabulary, every project with a
`Features/` directory is checked, and a project whose features need no subfolder is listed with an
empty vocabulary rather than left out.**

- `Persistence/Features/<F>/` gains `Observability/` — for an adapter that reports on a feature
  without storing anything for it. `ReminderDiagnostics` is the case; a counter, a meter, or a trace
  source belonging to one feature is the shape.
- `Src/Presentation/AppTemplate.Worker` enters `_vocabulary` with **the empty list**. Its features
  hold a `BackgroundService`, its options and its metrics side by side, with no subfolder, and the
  correct vocabulary for that today is "none". An empty list is not the same as an absent entry: the
  first subfolder anyone adds fails the test, which is the point.
- The Worker's two loops move to `Features/Maintenance/` and `Features/Reminders/`, with their tests
  mirroring them. `Common/Observability/` and `Common/Security/` stay where they are — a telemetry
  setup and a `BackgroundCurrentUser` that refuses to invent a principal are genuinely transverse.
- `ThePersistenceMechanisms_KnowNoFeature` gains a third forbidden namespace,
  `AppTemplate.Application.Features`, so that the door `ReminderDiagnostics` walked through is shut.

## Consequences

- The question "where does the adapter for a port that needs neither the database, nor the mail
  relay, nor Identity go?" now has a written answer for the one shape that has actually occurred.
  It is still unanswered in general, and that is deliberate: one example generalises to nothing.
- Adding a project under `Src/` no longer silently escapes the layout rule for the projects that
  have features — but `AppTemplate.Infrastructure.{Identity,Email,InMemory}` organise themselves
  without `Features/` at all (`Identity` by technical concern: `Bearer/`, `Notifications/`,
  `Options/`, `Templates/`, `Tokens/`, `Users/`), so the walk skips them exactly as before. Their
  layout is held by review, not by a test, and giving them the `Common/` + `Features/` shape is a
  larger change than this record makes.
- `Common/` itself is still unchecked, in every layer. `Src/Application/AppTemplate.Application/Common/`
  is the one that shows it: it is the only `Common/` in the repository with loose files at its root.
  That is a separate correction, and this record does not make it.
- The empty vocabulary for the Worker will look like an oversight to a reader who does not read the
  comment beside it. The comment is therefore load-bearing and says so.

## Alternatives rejected

- **Leaving `ReminderDiagnostics` under `Common/Observability/` and documenting it as an exception.**
  It was already documented as one, in two places, and the documentation is what let it stay wrong:
  a folder under `Common/` whose lifetime is indexed on one feature is not common, however well the
  exception is written up.
- **Moving `ReminderDiagnostics` into `AppTemplate.Worker`**, its only exporter, which would let the
  meter name be a shared constant instead of a literal copied into `WorkerObservabilityExtensions`. Rejected:
  `FireDueRemindersUseCase` takes `IReminderDiagnostics` in its constructor, so every host composing
  the application layer needs an adapter for it, and the API composes the persistence module without
  composing the Worker. The duplicated literal is the cheaper of the two costs.
- **Inventing a vocabulary for the Worker's features** — `Loops/`, `Services/`, `Options/` — so its
  entry would not be empty. That is the guess [0025](0025-closed-folder-vocabulary-per-layer.md)
  exists to prevent: five files that read perfectly well side by side would be split across three
  folders to satisfy a table.
- **Widening `ThePersistenceMechanisms_KnowNoFeature` to forbid every namespace containing the word
  `Features`.** Rejected as a string rule standing in for a dependency rule; naming the three
  namespaces is longer and says what it means.

## Revisit when

A second feature needs `Observability/` in the persistence layer — at that point the folder is a
pattern rather than one file's home, and it is worth asking whether these adapters belong in the
persistence project at all, since neither of them will touch the database. Revisit the Worker's
empty vocabulary the day a third background loop arrives: three loops with three files each may
genuinely want a subfolder, and that is the conversation this empty list is designed to force.
