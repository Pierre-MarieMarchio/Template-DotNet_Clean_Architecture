# Feature shapes

A feature in this project takes one of two shapes: an aggregate with use cases, or a service over a
store. Both are first-class, and one question chooses between them.

## The question

**Does the feature have rules that protect a state?** A forbidden transition, uniqueness within a
collection, a cap, a consistency between two fields. If it does, it takes the rich shape; if it does
not, the simple one. Size and importance do not enter into it.

A feature with no rule that is given an aggregate pays for a mapper, a tracker and a repository that
guard nothing. A feature with a rule that is given a service has nowhere to put the rule.

## The simple shape

The feature has no domain folder. One application service, `I<Feature>Service`, holds one method per
operation and works over two ports: a store that stages writes and a queries port that projects
reads. The persistence record is the only model.

The rest of the architecture still applies. The owner comes from `ICurrentUser`, never from the body,
and every store and queries method names it. The store loads the owner's row and stages the change,
and `IUnitOfWork` commits it. When the feature keeps a version, the store checks the caller's
`If-Match` against the row, and the database refuses a write whose version moved in between.

<!--#if (notes)-->
`Notes` is this shape: `INoteService` over `INoteStore` and `INoteQueries`, with `NoteRecord` as its
only model.
<!--#endif-->

`FeatureIsolationTests.AFeatureWithoutADomainFolder_IsAServiceOverAStore` holds the shape: a feature
may omit its domain folder only while it is a service over its storage ports, with no use cases.

## The rich shape

The aggregate lives in `AppTemplate.Domain`, with its value objects, its events and its repository
contract. The application holds one use case per operation, each in its own folder with its command
or query, its named interface and its validator, plus a queries port for the reads. Persistence maps
the aggregate onto row records through a mapper and a tracker, so the domain never sees EF Core.

<!--#if (todoLists)-->
`TodoLists` is this shape: the `TodoList` aggregate, `ITodoListRepository`, `ITodoListQueries`, and a
folder per operation such as `CreateTodoList` and `CompleteTodoItem`.
<!--#endif-->
<!--#if (files)-->
`Files` is this shape too: a stored file's states and transitions are rules the aggregate protects.
<!--#endif-->

`new-use-case` adds one operation to a feature of this shape, as
[adding a feature](../guides/adding-a-feature.md#add-a-use-case) shows.

## Side by side

| | Simple | Rich |
|---|---|---|
| Domain | nothing | aggregate, value objects, events, repository contract |
| Application | one service, one method per operation | one use case per operation, behind its own interface |
| Ports | a store for writes, queries for reads | the repository (domain), queries for reads |
| Persistence | the record is the model; the store stages it | record, mapper, tracker |
| Validation | FluentValidation | FluentValidation, then the aggregate's invariants |
| Commit | `IUnitOfWork` | `IUnitOfWork`, the same boundary |
<!--#if (postgreSql)-->
| Concurrency | the row's `xmin` | the root's `xmin` |
<!--#endif-->
<!--#if (versionCounter)-->
| Concurrency | the row's `Version` counter | the root's `Version` counter |
<!--#endif-->

<!--#if (canAddFeature)-->
The generator writes either shape: `new-feature --model record` the simple one, and
`--model aggregate` the rich one, which needs `--concurrency etag` and at least one write operation.
[Adding a feature](../guides/adding-a-feature.md) walks through both.
<!--#endif-->

## From simple to rich

A simple feature that acquires a rule now has a state to protect. Promotion is the only bridge
between the shapes, and it changes the inside of the feature, not its surface:

1. Extract the aggregate into the domain, with a factory and a rehydration path that run the same
   invariants, and declare its repository contract beside it.
2. Replace the store with a repository. The record stays; add the mapper and the tracker. The write
   half of the store moves into the repository, and the queries port stays.
3. Split the service into use cases, one folder per operation. Keep a service only when the
   operations share a gate, such as loading an owned aggregate.
4. Point the controller at the use cases.

Routes, contracts and responses do not change, nor do `ETag`, `If-Match`, `Idempotency-Key` and the
owner scoping: none of them belonged to the aggregate. The migration is empty unless the new rule
needs a column or an index.

## Decisions

- [0002. Two feature shapes](../decisions/0002-two-feature-shapes.md)
- [0005. The domain is not mapped by EF](../decisions/0005-the-domain-is-not-mapped-by-ef.md)
