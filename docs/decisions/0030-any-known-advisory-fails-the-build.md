# 0030. Any known advisory fails the build

Status: Accepted

## Context

NuGetAudit compares every package a project restores, direct or transitive, with the advisories
NuGet publishes, and reports each as a warning from NU1901 (low) to NU1904 (critical). Its default
reports only the direct packages and only from `low` up, as warnings the build lets through. This
project builds with `TreatWarningsAsErrors`, so what the audit reports decides whether the build
passes, and a team could reasonably have set the audit to report less, or to report without failing:
a new advisory then never breaks a build that passed the day before.

The other side is a project that is created today. If an advisory already stands on a dependency
this project pins, the project is born vulnerable; a build that passes in silence hides that from
the one person who can still choose to start elsewhere or to raise the dependency before anything
is written on top of it.

## Decision

- **Every advisory fails the build**: `Directory.Build.props` sets `NuGetAudit` to `true`,
  `NuGetAuditMode` to `all`, so transitive packages count, and `NuGetAuditLevel` to `low`, under
  `TreatWarningsAsErrors`.
- **The answer to a failure is to raise the dependency**: a version pin in
  `Directory.Packages.props`, a transitive one when the package nobody references is the vulnerable
  one, with a guidance line saying when it can go.
- **When no fixed version exists or can be taken yet**, the advisory is named in a
  `NuGetAuditSuppress` item in `Directory.Build.props`: that one advisory's URL, with a guidance line
  beside it giving the date and the reason, removed when a fixed version ships. The audit level is
  never lowered and the NU190x codes are never put in a `<NoWarn>`.
- `NuGetAuditPolicyTests` holds the three settings and refuses a `NoWarn` or a `WarningsNotAsErrors`
  naming NU1901 to NU1904 in any project or props file.

## Alternatives considered

- **Level `moderate` or `high`**: fewer builds break, and the ones that do are the serious ones. A
  low advisory on a dependency still reaches every instance of the project, and the line between
  low and moderate is the advisory author's judgement, not this project's.
- **Reporting as a warning that does not fail the build**: nothing breaks, and nobody reads a
  warning that has stood in the output for weeks; the project would be born vulnerable and say so
  where no one looks.
- **Mode `direct`**: the packages listed in the pins are few and the advisories on them are rare;
  most advisories arrive through a package nobody references, which this mode never reports.

## Consequences

- A build can fail the day an advisory is published, with no change to the code: the first build
  after it, here and in CI, stops until the dependency is raised.
- A project generated that day can fail its first build, until the dependency is raised, by its
  owner or by a newer release of the template it came from; a project already generated sees the same failure in `verify` and in CI.
- Every failure ends in a decision recorded in `Directory.Packages.props` or in a dated
  `NuGetAuditSuppress`, so the list of what the project accepts is the list of those two.
- A change that relaxes any of the three settings, or silences a code, changes this decision.

## Revisit when

NuGet separates advisories on a package's reachable code from the rest, so the audit can fail on
those only, or the cost of a failing build the day an advisory is published outweighs a project
being born vulnerable.
