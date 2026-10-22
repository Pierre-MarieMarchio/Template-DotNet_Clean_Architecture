# 0020. One risk, one type of test

Status: Accepted

## Context

A suite grows by habit: a row per enum member, a test per null guard, a mapping test per field, an
ownership test per use case against a substitute that cannot be wrong. Each one is cheap. Together
they make every change touch tests that fail only when the line they copy is edited on purpose,
while the risks a client meets, a changed contract, a route left open, a double that drifted from
its adapter, stay with no test at all.

A team could reasonably aim for a test count or a coverage figure instead, or keep every test that
ever passed and let the review catch the rest.

## Decision

- **Each risk is held by one type of test**, the cheapest that can fail for its real cause: unit,
  architecture, integration, contract, end-to-end or acceptance. A second type is added only when it
  can fail for a cause the first cannot see. [Testing](../guides/testing.md) tables the types and the
  risks.
- **A population is one test.** It discovers its members, asserts that it found some, and names each
  offender; it never states their number.
- **What is not tested:** a framework or compiler guarantee, a restated constant or signature, an
  argument forwarded to a substitute, ownership decided by a substitute.
- **The contract is held where a client reads it.** The OpenAPI document is committed beside its
  test, and `OpenApiDocumentTests` fails on any difference from the document the API serves. Every
  error code has its row in the API reference, checked both ways by `ErrorCodeTableTests` and
  against its status by `ErrorCodeStatusTests`.
<!--#if (accounts)-->
- **Default deny is checked over every endpoint the host maps**, by
  `DefaultDenyAuthorizationTests`, not over a list kept by hand.
<!--#endif-->
- **A double that carries behaviour runs the contract tests of the adapter it replaces.**
<!--#if (files)-->
- **The file content ports do it** through `FileContentStoreContract` and
  `FileContentInventoryContract`, run against the in-memory double and every adapter.
<!--#endif-->
- **The stack as shipped is held by a small end-to-end suite**, one test per flow, run over HTTP
  against the Compose stack by `dotnet run Tools/Tasks.cs end-to-end`, apart from `verify`: no lower type
  fails for a broken Compose file, a mail that never reaches the inbox or a relay that drops a
  request, and none of them needs the whole stack built.
<!--#if (ci)-->
- **CI runs that suite as a blocking job.**
<!--#endif-->
- **Coverage is a tripwire.** The floor in `coverage.minimum` catches an area shipped with no test.
  It is never raised to reward tests, and a drop is answered by naming the risk left untested.
- **A new check is seen failing once**, for its real cause, before it counts.

## Consequences

- Adding an endpoint, a use case or a validator changes no list and no count: the tests over the
  whole surface pick it up.
- A change to a route, a contract or a summary lands with its new OpenAPI reference, written by
  `dotnet run Tools/Tasks.cs openapi`, and the review reads the difference a client will see.
<!--#if (postgreSql)-->
- Ownership and a controller's HTTP behaviour are proved against PostgreSQL, so a run without Docker
  no longer exercises them.
<!--#endif-->
<!--#if (sqlServer)-->
- Ownership and a controller's HTTP behaviour are proved against SQL Server, so a run without Docker
  no longer exercises them.
<!--#endif-->
<!--#if (sqlite)-->
- Ownership and a controller's HTTP behaviour are proved against a SQLite file, which needs no
  database container.
<!--#endif-->
- The coverage figure moves when decorative tests go; it is re-measured, not defended.
- A change to the stack, a mail or a relay is proved by `end-to-end` before it is done, since `verify` does
  not start the stack.
- The test-stack row of [ADR 0001](0001-adopt-the-generated-baseline.md) is unchanged.

## Revisit when

A defect reaches a deployment that a test of another type would have caught, or the integration
suite grows too slow to run on every change.
