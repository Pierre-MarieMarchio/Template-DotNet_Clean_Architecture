# 0012. On account deletion, each feature decides

Status: Accepted

## Context

When an account is deleted, the data that belongs to it lives in features the account module does
not know, written in transactions the account's deletion does not share. Deleting everything from
the account module would make it reach into every feature. Deleting nothing leaves data with no
owner. Some data should go; some must stay, without the person it named.

## Decision

- **The account module publishes a fact.** Once the deletion has committed, it publishes
  `UserAccountDeleted`, declared in the application core where every feature can consume it
  without naming the module that deleted the account.
- **Each feature owning user data declares its policy**, delete or anonymise, in a consumer of that
  fact. The consumer asks `IUserAccountDirectory` whether the account is really gone, is idempotent,
  and commits in a transaction of its own.
- **A sweep is the guarantee.** A leased pass on the maintenance timer asks each feature which owner
  ids still hold data and applies the same policy, so a fact that never reached a consumer costs an
  interval, not the data's fate ([ADR 0006](0006-events-after-commit-without-an-outbox.md)).
- **A new feature states its policy when it is generated:** `--deletion delete` or
  `--deletion anonymise`, required with `--access owner`.
<!--#if (localAuth)-->
- Deleting one's own account demands re-authentication.
<!--#endif-->
<!--#if (externalAuth)-->
- The trigger is an administrative deletion of the identity mapping. A deletion signal from the
  provider is this project's to wire to that call.
<!--#endif-->

## Consequences

- The account module knows no feature, and a feature added later joins by writing one consumer and
  one sweep query.
- Every feature this project ships deletes its rows.
- `SweepDeletedAccountDataUseCaseTests` holds the sweep, and each feature's consumer has its own
  test.

[Deleting an account](../features/accounts.md#deleting-an-account) is the account holder's view.

## Revisit when

Deletion must be confirmed to the account holder only once every feature has finished, or a
retention rule requires keeping some data after the account is gone.
