# Decisions

Written by `dotnet run Tools/Tasks.cs docs-index` from the frontmatter of each page:
edit the pages, then run it again.

- [Decisions](README.md) - When a choice deserves an ADR, how ADRs are numbered and superseded, and which decisions this project holds.
- [0001. Adopt the generated baseline](0001-adopt-the-generated-baseline.md) - Accepted. Which stack, tools, foundations and deployment shape this project inherited, and what changing each one costs.
- [0002. Two feature shapes](0002-two-feature-shapes.md) - Accepted. Why a feature takes one of two shapes, an aggregate or a service over a store, and the question that chooses.
- [0003. Use cases and ports are explicit](0003-use-cases-and-ports-are-explicit.md) - Superseded. Why a use case was an interface a controller called directly, with no dispatcher, until Mediator messages replaced it.
- [0004. Features meet through ports and events](0004-features-meet-through-ports-and-events.md) - Superseded. Why a feature used another only through its public port or its domain events, before ports their owner declares replaced the rule.
- [0005. The domain is not mapped by EF](0005-the-domain-is-not-mapped-by-ef.md) - Accepted. Why EF maps row records and never the domain types, and how an aggregate is persisted through rows, a mapper and a store.
- [0006. Events after commit, without an outbox](0006-events-after-commit-without-an-outbox.md) - Accepted. Why domain events are published in process after the commit, at most once, with no outbox and no retry.
- [0007. One database, a schema per feature](0007-one-database-a-schema-per-feature.md) - Accepted. Why every context shares one database and one connection string, with a schema of its own per feature.
- [0008. Optimistic concurrency, end to end](0008-optimistic-concurrency-end-to-end.md) - Accepted. Why a write is checked against the version the client read, from the database's version to the ETag a client sends back.
- [0009. Ownership is in the query](0009-ownership-is-in-the-query.md) - Accepted. Why the owner is in every query's predicate, so another caller's record answers 404 exactly as a missing one.
- [0010. An idempotency key is a lease per user](0010-idempotency-key-is-a-lease-per-user.md) - Accepted. Why an idempotency key is stored per user and claimed as a lease, so a retried create is answered once and a reused key is refused.
- [0011. Jobs run in the API, under a lease](0011-jobs-run-in-the-api-under-a-lease.md) - Accepted. Why the periodic jobs run as timers in the API process, and how a lease keeps two instances from running the same pass.
- [0012. On account deletion, each feature decides](0012-account-deletion-each-feature-decides.md) - Accepted. Why a deleted account is a fact the account module publishes, and each feature decides whether to delete or anonymise its records.
- [0013. A session is a refresh-token family](0013-a-session-is-a-refresh-token-family.md) - Accepted. Why a session is the family of refresh tokens one sign-in starts, and how its owner lists and ends it.
- [0014. Anonymous routes reveal nothing](0014-anonymous-routes-reveal-nothing.md) - Accepted. Why the anonymous routes answer the same way whether or not an account exists, and tell the address by mail instead.
- [0015. Passkeys stand on their own](0015-passkeys-stand-on-their-own.md) - Accepted. Why a passkey signs a caller in on its own, with the ceremony state sealed and carried by the client.
- [0016. A browser's refresh token travels in a cookie](0016-browser-refresh-token-in-a-cookie.md) - Accepted. Why a browser may ask, request by request, for its refresh token in a cookie rather than in the body.
- [0017. External subjects map to internal owners](0017-external-subjects-map-to-internal-owners.md) - Accepted. Why a provider's issuer and subject map to an owner id of this project's own, which the features store instead of the subject.
- [0018. Stored files are keyed, granted and refused](0018-stored-files-are-keyed-granted-and-refused.md) - Accepted. Why a stored file lives under a random key bound to its digest, is reached through a narrow grant, and stays unread until its inspection passes.
- [0019. One generated API client](0019-one-generated-api-client.md) - Accepted. Why every web app calls the API through one TypeScript client generated from the API's own OpenAPI document.
- [0020. One risk, one type of test](0020-one-risk-one-type-of-test.md) - Accepted. Why each risk is held by one type of test, the cheapest that can fail for its real cause.
- [0021. SQLite is a development database](0021-sqlite-is-a-development-database.md) - Accepted. Why a project on SQLite starts in Development only, on one file that one process holds.
- [0022. A presentation project reaches the core through the API or the application layer](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) - Accepted. Why where a presentation project runs decides whether it calls the API or the use cases in process.
- [0023. A .NET client generated by Kiota](0023-a-dotnet-client-generated-by-kiota.md) - Accepted. Why the .NET presentation projects call the API through one client that Kiota generates from the OpenAPI reference.
- [0024. A server-rendered web app runs on its own server](0024-a-server-rendered-web-app-runs-on-its-own-server.md) - Accepted. Why a server-rendered web app is served by its own Node server in place of nginx, and how it relays the API.
- [0025. Jobs run in a worker, under a lease](0025-jobs-run-in-a-worker-under-a-lease.md) - Accepted. Why the periodic jobs run in a worker process apart from the API, and how a lease keeps two instances from running the same pass.
- [0026. An access key stands for its owner where a token cannot be refreshed](0026-an-access-key-stands-for-its-owner-where-a-token-cannot-be-refreshed.md) - Accepted. Why a client that cannot refresh a token signs in with an access key its owner creates, lists and revokes.
- [0027. An MCP server acts for the caller its credential names](0027-an-mcp-server-acts-for-the-caller-its-credential-names.md) - Accepted. Why the MCP server runs the use cases in process for the caller its credential names, over streamable HTTP.
- [0028. The author answers for every line](0028-the-author-answers-for-every-line.md) - Accepted. Why a commit or a pull request names its human author only, with an Assisted-by trailer for the tool that helped.
- [0029. A project folder carries the product's initials, not its name](0029-a-project-folder-carries-the-products-initials-not-its-name.md) - Accepted. Why a project's folder carries the product's initials in place of its name, and how the initials are drawn.
- [0030. Any known advisory fails the build](0030-any-known-advisory-fails-the-build.md) - Accepted. Why a known advisory on any package, transitive ones included, fails the build whatever its severity.
- [0031. The feature generator composes what it writes](0031-the-feature-generator-composes-what-it-writes.md) - Accepted. Why the feature generator makes the edits that compose a feature into the shared files instead of printing them.
- [0032. The app host follows Compose](0032-the-app-host-follows-compose.md) - Accepted. Why Compose stays the reference of the local stack and the Aspire app host follows it, service for service.
- [0033. Use cases are Mediator messages, and ports are explicit](0033-use-cases-are-mediator-messages-and-ports-are-explicit.md) - Accepted. Why a use case is a Mediator message and its handler, and a feature's ports stay explicit interfaces.
- [0034. Features meet through ports their owner declares, and events](0034-features-meet-through-ports-their-owner-declares-and-events.md) - Accepted. Why a feature reaches another only through a port that other declares or through its domain events.
- [0035. Folders take their names from a closed vocabulary](0035-folders-take-their-names-from-a-closed-vocabulary.md) - Accepted. Why every project names its folders from a closed list of words, each level answering one question.
- [0036. Mappings are written by hand](0036-mappings-are-written-by-hand.md) - Accepted. Why every mapping is a method written by hand on the side that produces the type, with no mapping library.
- [0037. A web app is deployed apart, behind a relay to the API](0037-a-web-app-is-deployed-apart-behind-a-relay-to-the-api.md) - Accepted. Why each web app is a deployable of its own that relays its API calls, rather than files the API serves.
- [0038. Deleting removes rows](0038-deleting-removes-rows.md) - Accepted. Why a deletion removes the row and its children, and no record carries a deleted flag.
- [0039. A role is checked by the host, not by the use case](0039-a-role-is-checked-by-the-host-not-by-the-use-case.md) - Accepted. Why a role is a policy each host checks, and a use case never reads the caller's roles.
- [0040. The signing key rotates through previous keys that only validate](0040-the-signing-key-rotates-through-previous-keys-that-only-validate.md) - Accepted. Why one key signs access tokens while the previous keys only validate, and how a rotation uses them.
