# Guides

Written by `dotnet cleanarch docs-index` from the frontmatter of each page:
edit the pages, then run it again.

- [Adding a feature](adding-a-feature.md) - How to add a feature of your own, in the shape its rules call for, with its tests and its page.
- [Adding a pipeline behaviour](adding-a-pipeline-behaviour.md) - How to add a behaviour that runs around every use case, at a place in the pipeline a test holds.
- [Adding a project](adding-a-project.md) - How to add a project to the solution so that it builds, ships in each host's image, has its tests and is read by every structural rule.
- [Adding a setting](adding-a-setting.md) - How to add a setting a deployment chooses, bound from its own section and checked before the host serves anything.
- [Build and CI](build-and-ci.md) - Which command builds, checks and tests this project, what the gate holds, and what each workflow runs.
- [Deployment](deployment.md) - How to ship this project to an environment other than your machine, from its images and migrations to its secrets and instances.
- [Migrations](migrations.md) - How to create, check and apply the migrations of each database context.
- [Reaching another feature](reaching-another-feature.md) - How one feature reads, changes or reacts to another through a port or a domain event, so that each stays removable.
- [Removing features](removing-features.md) - How to remove a feature, an example or another part this project no longer needs.
- [Rotating the signing key](rotating-the-signing-key.md) - How to replace the key that signs access tokens without refusing a signed-in caller.
- [Running the app host](running-the-app-host.md) - How to run the local stack from the Aspire app host, with every host's logs, traces and metrics in one dashboard.
- [Testing](testing.md) - Which type of test holds which risk, where a new test goes, and how the suites run.
- [Upgrading](upgrading.md) - How to see what a newer version of the package that generated this project changes here before taking any of it.
