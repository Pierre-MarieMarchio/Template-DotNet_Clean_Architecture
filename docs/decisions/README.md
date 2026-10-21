# Decisions

An architecture decision record (ADR) says why this project made one choice over another, in a page
of its own: the context, the decision, the alternatives it turned down, its consequences, and when
to revisit it. The code says what the project does; an ADR says why it does not do the obvious
alternative.

## When to write one

Write an ADR for a decision that passes all three tests:

- a competent team could reasonably have decided otherwise;
- a future maintainer must know its consequences before changing it;
- it is not already obvious from the code, the conventions or mainstream .NET practice.

A rule every change follows, or a fact about how something works, belongs on the page that
describes it, not here. Where a test can hold a decision, it does, and the ADR names the test: a
decision and the test that holds it change together, in the same commit.

## Numbering and status

The ADRs below shipped with the project and keep their numbers; a gap in the sequence is a decision
about a part this project does not have. This project's own ADRs are numbered from **1000**, the
next free number each time, and start from [the ADR template](../_page-templates/adr.md) as `Proposed`
until the team accepts them.

A decision is never edited into another. To replace one, write a new ADR and set the old one's
status to `Superseded by` with a link to the new one.

## Changing an inherited choice

[ADR 0001](0001-adopt-the-generated-baseline.md) lists the choices this project inherited: its
stack, tools, foundations and deployment shape. To reverse or replace one of them, write a product
ADR that names the row, then link that ADR from the row's "Changed by" cell. ADR 0001 stays
Accepted, and its table shows which inherited choices still hold.

## The decisions

| # | Decision | Area |
|---|---|---|
| [0001](0001-adopt-the-generated-baseline.md) | Adopt the generated baseline | Foundations |
| [0002](0002-two-feature-shapes.md) | Two feature shapes | Architecture |
| [0003](0003-use-cases-and-ports-are-explicit.md) | Use cases and ports are explicit | Architecture |
| [0004](0004-features-meet-through-ports-and-events.md) | Features meet through ports and events | Architecture |
| [0005](0005-the-domain-is-not-mapped-by-ef.md) | The domain is not mapped by EF | Persistence |
| [0006](0006-events-after-commit-without-an-outbox.md) | Events after commit, without an outbox | Architecture |
| [0007](0007-one-database-a-schema-per-feature.md) | One database, a schema per feature | Persistence |
| [0008](0008-optimistic-concurrency-end-to-end.md) | Optimistic concurrency, end to end | Persistence |
<!--#if (accounts)-->
| [0009](0009-ownership-is-in-the-query.md) | Ownership is in the query | Persistence |
<!--#endif-->
| [0010](0010-idempotency-key-is-a-lease-per-user.md) | An idempotency key is a lease per user | HTTP API |
| [0011](0011-jobs-run-in-the-api-under-a-lease.md) | Jobs run in the API, under a lease | Background work |
<!--#if (anyFeature && accounts)-->
| [0012](0012-account-deletion-each-feature-decides.md) | On account deletion, each feature decides | Accounts |
<!--#endif-->
<!--#if (localAuth)-->
| [0013](0013-a-session-is-a-refresh-token-family.md) | A session is a refresh-token family | Authentication |
| [0014](0014-anonymous-routes-reveal-nothing.md) | Anonymous routes reveal nothing | Authentication |
| [0015](0015-passkeys-stand-on-their-own.md) | Passkeys stand on their own | Authentication |
| [0016](0016-browser-refresh-token-in-a-cookie.md) | A browser's refresh token travels in a cookie | Authentication |
<!--#endif-->
<!--#if (externalAuth)-->
| [0017](0017-external-subjects-map-to-internal-owners.md) | External subjects map to internal owners | Authentication |
<!--#endif-->
<!--#if (files)-->
| [0018](0018-stored-files-are-keyed-granted-and-refused.md) | Stored files are keyed, granted and refused | Files |
<!--#endif-->
<!--#if (anyWeb)-->
| [0019](0019-one-generated-api-client.md) | One generated API client | Web front end |
<!--#endif-->

Add this project's own ADRs as rows at the end of the table, numbered from 1000.
