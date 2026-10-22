# 0007. GitHub Packages mirrors nuget.org

Status: Accepted

## Context

Both packages are published to nuget.org, where `dotnet new install` and `dnx` find them. The
repository page's Packages panel lists GitHub Packages only, so it read "No packages published"
beside a project whose packages anyone can install. GitHub's NuGet feed also asks for a token to
install, even from a public repository, so it cannot be where people are sent.

## Decision

- **nuget.org is the feed.** Every page tells people to install from it, and its `publish` job is
  the one the `nuget` environment's reviewer approves.
- **GitHub Packages mirrors it.** The release workflow's `mirror` job runs after `publish`, with
  the workflow's own token and `packages: write`, and pushes the same two `.nupkg` files to
  `nuget.pkg.github.com`; their `RepositoryUrl` links them to this repository, so the panel lists
  them. A version already there is skipped, and no symbol package goes, as GitHub takes none.

## Alternatives considered

- **Hide the Packages panel and show nuget.org badges in the README.** No second feed, but the
  repository page then says nothing about the packages where GitHub users look for them.
- **GitHub Packages as the only feed.** Installing would need a personal token on every machine,
  which a template's first command cannot ask for.

## Consequences

- A release reaches GitHub Packages only once nuget.org has it, so the two never disagree on what
  was released; a refused `nuget` approval publishes nowhere.
- No secret is added: the mirror uses the workflow's `GITHUB_TOKEN`.
- Versions released before the mirror existed reach GitHub Packages only if pushed by hand.

## Revisit when

GitHub's NuGet feed serves public packages without a token, or the Packages panel can list a
package hosted on nuget.org.
