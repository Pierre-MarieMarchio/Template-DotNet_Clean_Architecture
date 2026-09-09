# Plans of record

A plan of record states what a piece of work set out to do, what it decided, and — the half worth
more than the diff — what it refused and why. These are kept for that reasoning.

**None of them describes the tree as it stands.** A plan states what was true when it was written,
and one superseded by a later plan is not edited to agree with it. For the current repository read
[`../README.md`](../README.md) and the documents it lists; where a plan disagrees with them, the
plan is the out-of-date half.

**This directory does not ship.** `.template.config/template.json` excludes it, so a project
generated from this template inherits none of it — these plans record how the template came to be
shaped as it is, which is the template's history and not a derived product's. Everything a derived
project needs is in the documents [`../README.md`](../README.md) lists.

What a derived project does inherit is the convention: measurements, rejected options and
arbitrations belong in a document rather than in a comment above a method, which is what
[`../CONVENTIONS.md`](../CONVENTIONS.md) requires and what a plan of record is the long form of.

| Plan | What it decided |
|---|---|
| [`AUTH-SEPARATION.md`](AUTH-SEPARATION.md) | authentication becomes its own application project over its own storage: two contexts, two migration histories, one database |
| [`DERIVED-PROJECT-ERGONOMICS.md`](DERIVED-PROJECT-ERGONOMICS.md) | what a project derived from this template should not have to write itself, and the feature scaffolder that answers it |
| [`SDK-SPLIT-PLAN.md`](SDK-SPLIT-PLAN.md) | the wave-by-wave plan for splitting the template's reusable half out of its example half |
| [`SDK-SPLIT-TARGET.md`](SDK-SPLIT-TARGET.md) | the layout that split was aiming at, project by project |
| [`SDK-SPLIT-DECISIONS.md`](SDK-SPLIT-DECISIONS.md) | the decisions taken along the way, and what each alternative would have cost |
| [`SDK-SPLIT-BLAST-RADIUS.md`](SDK-SPLIT-BLAST-RADIUS.md) | what the split was going to break, and the gaps it left open |
| [`SDK-SPLIT-HANDOFF.md`](SDK-SPLIT-HANDOFF.md) | the state of the work at each handover, and the hazards not to rediscover |
