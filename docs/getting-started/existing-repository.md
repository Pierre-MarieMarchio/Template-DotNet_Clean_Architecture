# Moving into an existing repository

This project can become a repository of its own, or move into one that already has a history. Do this
once, right after generation and bootstrap.

## At the root or in a subdirectory

| Shape | What you do | What it costs |
|---|---|---|
| At the root | the tree becomes the repository; merge by hand what both already have | nothing in the tooling: every task and gate assumes the solution sits at the repository root |
| In a subdirectory, such as `services/api/` | copy the tree there; the host keeps its own root files | the host's `global.json` learns the test runner; the workflows move to the git root |

Take the root unless the repository must hold more than this service. At the root, the files to merge
are the ones a host is likely to have already: `README.md`, `.gitignore`, `global.json`,
`Directory.Build.props`, `Directory.Packages.props` and `.editorconfig`.

Three facts decide the subdirectory shape:

- `Tools/Tasks.cs` finds the repository root by walking up to the solution, so every task works from
  the subdirectory unchanged.
- MSBuild stops at the nearest `Directory.Build.props` and `Directory.Packages.props`, and
  `.editorconfig` declares `root = true`: the host's root files do not reach these projects, and these
  do not reach the host's.
<!--#if (ci)-->
- GitHub reads workflows from `.github/workflows` at the git root only.
<!--#endif-->

## In a subdirectory, step by step

1. Copy the tree, bootstrapped and committed, into the subdirectory:

   ```bash
   mkdir -p <host-repo>/services/api
   cp -a <this-project>/. <host-repo>/services/api/
   ```

2. Add the test runner to the host's root `global.json`. `dotnet` resolves `global.json` upward from
   where it runs, so a test run started at the host's root otherwise fails on every test project with
   "Testing with VSTest target is no longer supported":

   ```json
   {
     "test": { "runner": "Microsoft.Testing.Platform" }
   }
   ```

<!--#if (ci)-->
3. Move the workflows to the git root, renaming them if the host has its own:

   ```bash
   mv services/api/.github/workflows/ci.yml      .github/workflows/api-ci.yml
   mv services/api/.github/workflows/codeql.yml  .github/workflows/api-codeql.yml
<!--#if (serverDatabase)-->
   mv services/api/.github/workflows/release.yml .github/workflows/api-release.yml
<!--#endif-->
   ```

<!--#endif-->
<!--#if (ci && anyWeb)-->
   Move `web.yml` the same way.

<!--#endif-->
<!--#if (ci && sonar)-->
   Move `sonarqube.yml` the same way.

<!--#endif-->
<!--#if (ci)-->
   A workflow runs from the git root, so prefix every path its steps name (the solution,
   `Tools/Tasks.cs`, `coverage.minimum`, `global.json`) with the subdirectory.

<!--#endif-->
4. Merge `.github/dependabot.yml` into the host's, at the git root, each `directory` prefixed with
   the subdirectory. Dependabot reads that one file only.

5. Keep Compose in the subdirectory. Its build contexts are relative to the compose file, so nothing
   needs re-pointing; copy `.env.example` to `.env` beside it.

## Verify

From the subdirectory:

```bash
dotnet run Tools/Tasks.cs verify
docker compose config -q
```

Then, from the host's root, `dotnet test services/api/AppTemplate.sln` proves the runner reached
the host's `global.json`.

`coverage.minimum` holds the floor this project's own suite was measured against. It stays right on
the first day; re-measure it once your own code lands, as [testing](../guides/testing.md#coverage)
explains.
