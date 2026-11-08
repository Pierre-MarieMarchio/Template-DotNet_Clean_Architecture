# Decisions

Written by `dotnet cleanarch docs-index` from the frontmatter of each page:
edit the pages, then run it again.

- [Decisions](README.md) - Why an ADR is a last resort, when a choice still deserves one, how ADRs are numbered and superseded, and which decisions this project holds.
- [0001. The domain is not mapped by EF](0001-the-domain-is-not-mapped-by-ef.md) - Accepted. Why EF maps row records and never the domain types, and how an aggregate is persisted through rows, a mapper and a store.
- [0002. Events after commit, without an outbox](0002-events-after-commit-without-an-outbox.md) - Accepted. Why domain events are published in process after the commit, at most once, with no outbox and no retry.
- [0003. Use cases are Mediator messages, and ports are explicit](0003-use-cases-are-mediator-messages-and-ports-are-explicit.md) - Accepted. Why a use case is a Mediator message and its handler, and a feature's ports stay explicit interfaces.
- [0004. Ownership is in the query](0004-ownership-is-in-the-query.md) - Accepted. Why the owner is in every query's predicate, so another caller's record answers 404 exactly as a missing one.
- [0005. An MCP server acts for the caller its credential names](0005-an-mcp-server-acts-for-the-caller-its-credential-names.md) - Accepted. Why the MCP server runs the use cases in process for the caller its credential names, over streamable HTTP.
