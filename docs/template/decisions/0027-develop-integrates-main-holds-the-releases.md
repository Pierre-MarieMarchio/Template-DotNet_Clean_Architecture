# 0006. develop integrates, main holds the releases

Status: Accepted; supersedes [0006](0006-develop-integrates-main-holds-the-releases.md)

## Context

The template ships as two NuGet packages that people install by version, so every version must be
built from a commit that passed the whole gate, and its notes must say what changed in a generated
project. Three branch models were open. One trunk, `main`, with versions as tags on it, as most
.NET templates do. Git Flow's `develop` and `main`, which merges `develop` into `main` for each
release and back again for the version bump. Or `develop` and `main` with `main` only following the
releases. Releases could be cut by hand, raising the version and writing the notes, or by
release-please from the conventional commits the history already uses. Publishing to nuget.org could
read an API key stored as a secret, or trust the workflow's own identity. A working branch, once
merged, could be kept as a record of the change or deleted.

## Decision

- **`develop` integrates.** It is the default branch, so Dependabot, a pull request opened from the
  web and a contributor's fork target it; every change reaches it by a pull request from a
  `<type>/<subject>` branch cut from `develop`, rebased, so its history stays linear.
- **A working branch is deleted once merged.** Its commits are on `develop`, and the pull request
  keeps its discussion and its checks; the branches left are the work in progress.
- **`main` holds the released commits only.** The release workflow fast-forwards it to each release
  commit; nobody pushes to it, and it never takes a merge, so it never needs merging back.
- **release-please cuts the versions.** It keeps a release pull request on `develop` that raises
  `templateVersion` and writes the changelog entry from the commits; merging it tags `v<version>`.
- **An earlier .NET major gets a `net<major>.0` branch**, cut from its last tag when `develop` moves
  to the next major, and its fixes reach it by pull request.
- **nuget.org trusts the workflow.** Trusted Publishing exchanges the workflow's identity for a key
  that lives minutes; the `nuget` environment holds the push until a reviewer approves it.

## Alternatives considered

- **One trunk, `main`, with versions as tags.** The fewest branches, but the branch people land on
  holds unreleased commits beside the released ones, and a fix to a release waits behind whatever
  merged since.
- **Git Flow, `develop` merged into `main` for each release and back.** Two merges per release, a
  `main` whose history interleaves them, and a version bump that has to travel back to `develop`.
- **Versions and notes written by hand.** The changelog then depends on someone remembering every
  change since the last release, which the conventional commits already say.
- **An API key for nuget.org stored as a secret.** A long-lived key that can leak, and that publishes
  without a reviewer.
- **Keeping a working branch after its merge.** A record the merged commits and the pull request
  already hold, and a branch list where finished work hides the work in progress.

## Consequences

- The tip of `main` is always the latest release, and its history is the list of releases.
- A commit's type decides whether it reaches the changelog and how far the version moves, so a
  wrong type is a wrong release note: the type is part of the review.
- Merging the release pull request publishes, after the `nuget` approval; there is no separate tag
  to push.
- No secret can leak a publishing key, and a release cannot reach nuget.org without a reviewer.
- A pull request release-please opens with `GITHUB_TOKEN` starts no workflow, so its checks do not
  run on it; it changes `CHANGELOG.md`, `template.json`'s version and the manifest only.
- A merged branch is gone from the remote and from the clones that prune; its pull request still
  names it, and its commits stay reachable from `develop`.

## Revisit when

Several maintainers release concurrently and need release branches of their own, or a fix must
reach an earlier minor version, which `net<major>.0` branches do not cover.
