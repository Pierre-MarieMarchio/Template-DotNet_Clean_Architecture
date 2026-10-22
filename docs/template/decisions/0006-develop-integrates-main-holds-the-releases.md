# 0006. develop integrates, main holds the releases

Status: Accepted

## Context

The template ships as two NuGet packages that people install by version, so every version must be
built from a commit that passed the whole gate, and its notes must say what changed in a generated
project. Three branch models were open. One trunk, `main`, with versions as tags on it, as most
.NET templates do. Git Flow's `develop` and `main`, which merges `develop` into `main` for each
release and back again for the version bump. Or `develop` and `main` with `main` only following the
releases. Releases could be cut by hand, raising the version and writing the notes, or by
release-please from the conventional commits the history already uses. Publishing to nuget.org could
read an API key stored as a secret, or trust the workflow's own identity.

## Decision

- **`develop` integrates.** It is the default branch; every change reaches it by a pull request from
  a `<type>/<subject>` branch, rebased, so its history stays linear, and the branch is kept.
- **`main` holds the released commits only.** The release workflow fast-forwards it to each release
  commit; nobody pushes to it, and it never takes a merge, so it never needs merging back.
- **release-please cuts the versions.** It keeps a release pull request on `develop` that raises
  `templateVersion` and writes the changelog entry from the commits; merging it tags `v<version>`.
- **An earlier .NET major gets a `net<major>.0` branch**, cut from its last tag when `develop` moves
  to the next major, and its fixes reach it by pull request.
- **nuget.org trusts the workflow.** Trusted Publishing exchanges the workflow's identity for a key
  that lives minutes; the `nuget` environment holds the push until a reviewer approves it.

## Consequences

- The tip of `main` is always the latest release, and its history is the list of releases.
- A commit's type decides whether it reaches the changelog and how far the version moves, so a
  wrong type is a wrong release note: the type is part of the review.
- Merging the release pull request publishes, after the `nuget` approval; there is no separate tag
  to push.
- No secret can leak a publishing key, and a release cannot reach nuget.org without a reviewer.
- A pull request release-please opens with `GITHUB_TOKEN` starts no workflow, so its checks do not
  run on it; it changes `CHANGELOG.md`, `template.json`'s version and the manifest only.

## Revisit when

Several maintainers release concurrently and need release branches of their own, or a fix must
reach an earlier minor version, which `net<major>.0` branches do not cover.
