# 0005. A project carries one database provider

Status: Accepted

## Context

The template targets PostgreSQL, and teams that adopt it run SQL Server, or want a file database while
they develop. Two shapes were open. A project could carry every provider and pick one with a setting,
with the packages, types and SQL of all of them compiled in. Or the template could hold every provider
in its own source, each branch behind a generation condition, the way most .NET templates do. In both,
code for a provider the template's own gate never runs sits in the checkout, and a registration for two
providers on one builder throws.

## Decision

- **One provider per project, chosen at generation.** `--database` names it; a project holds that
  provider's package, types and SQL and no other's, and `template-acceptance.yml` greps each leg for
  the others. It is a generation choice, not a setting, so no configuration key is added.
- **PostgreSQL is the compiled default.** Its provider files are in the checkout: built, analysed,
  tested and covered by the template's own gate, and excluded from a project that picked another
  database. A second in-tree provider would compile unexercised and pull `coverage.minimum` down.
- **The other providers are overlays, not in-tree branches.** Each is a set of whole files under
  `.template.config/variants/database-<name>/` that replace the PostgreSQL file at the same path, with
  the same type names and public members; the acceptance legs that generate them build, test and prove
  them. The contract is in [Maintaining the template](../maintaining-the-template.md#database-providers).

How each provider keeps the row version and the feature schemas is the project's decision, in
[0008](../../decisions/0008-optimistic-concurrency-end-to-end.md) and
[0007](../../decisions/0007-one-database-a-schema-per-feature.md); how a project gets its first
migration is in [migrations](../../guides/migrations.md).

## Consequences

- A generated project reads as written for its database, and the pages condition on the provider.
- Every overlay file is a copy of its PostgreSQL counterpart, so a change to one is a change to the
  other, and the history of both paths finds a miss.
- Each provider adds a leg to the acceptance matrix and its own leak check; SQL Server's pulls a
  large image and takes the longest.
- A project cannot switch provider by editing a setting: it moves by generating again and carrying
  its own code across.

## Revisit when

A provider waiting for a stable EF Core release gets one (MySQL, as
[Maintaining the template](../maintaining-the-template.md#database-providers) says), or an overlay
diverges from the PostgreSQL files enough that a seam should move into the shared code.
