# 0036. Mappings are written by hand

Status: Accepted

## Context

A request crosses several shapes on its way through: a contract the API publishes, a message, a
domain type, a DTO a use case answers, a row a table stores. Each crossing is a mapping, and most
of them copy properties of the same names. A mapping library writes that copy for the team:
AutoMapper by reflection at run time, Mapperly by a source generator at compile time, whose output
the compiler checks and a reader can open.

The copy is also where two guarantees live. A field added inside the application must not reach the
published contract by that edit alone, and a row must become an aggregate through the aggregate's own
factory, leaving the version and the audit stamps to the save. A mapping that pairs members by name
does both silently when the names happen to match.

## Decision

- **Every mapping is a method written by hand**, on the side that produces the type: a response
  mapping under the API's `Contracts/Mapping/`, a DTO mapping under the application's
  `Model/Mapping/`, a row mapper under the persistence module's `Rows/Mapping/`. A property reaches
  the other side only through a line that names it.
- **No mapping library.** `BannedPackageTests` refuses AutoMapper and Mapperly (`Riok.Mapperly`) in
  every project and in `Directory.Packages.props`.

## Alternatives considered

- **AutoMapper.** Mappings resolved at run time, a configuration a reader cannot follow with the
  IDE, and a missing member found by a test or in production rather than by the compiler.
- **Mapperly.** Compile-time and readable, and its strict mode fails on a member left unmapped; but
  it pairs members by name, so a property added on both sides reaches the contract with no line in
  the diff, and a row mapper needs attributes to construct through the aggregate's factory and to
  skip the stamps, which is the same knowledge as the hand-written lines in a second form.
- **Serialising the application's types directly.** No mapping at all, and a contract that changes
  whenever a DTO does.

## Consequences

- A mapping is code like any other: the compiler checks it, `F12` reaches it, and the review reads
  each field a contract gains.
- Mappings cost lines, and a property added to an aggregate needs its line in the mapper. A mapper
  that forgets one loses data without failing anything, so a row mapper's tests walk the aggregate's
  type and fail on a property they do not find round-tripped.
- Changing this decision is removing a package from `BannedPackageTests` together with this ADR.

[Layers and projects](../architecture/layers-and-projects.md) says where each mapping lives.

## Revisit when

Mappings grow to a share of each change that review no longer reads, or a generator can be told to
map only the members a mapping names.
