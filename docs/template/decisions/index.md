# Decisions

Written by `dotnet cleanarch docs-index` from the frontmatter of each page:
edit the pages, then run it again.

- [0001. A project carries one database provider](0001-a-project-carries-one-database-provider.md) - Accepted. Why a generated project holds one database provider, chosen at generation, and no other provider's code.
- [0002. A pull request runs the legs its change can affect](0002-a-pull-request-runs-the-legs-its-change-can-affect.md) - Accepted. Why a pull request runs only the acceptance legs its changed paths can affect, and how a selector chooses them.
- [0003. A project runs its tasks from a pinned .NET tool](0003-a-project-runs-its-tasks-from-a-pinned-dotnet-tool.md) - Accepted. Why a project runs its tasks from a .NET tool pinned at the template's version, while another tool only creates projects.
- [0004. A web app comes from a starter, static or server-rendered](0004-a-web-app-comes-from-a-starter-static-or-server-rendered.md) - Accepted. Why a web app comes from a starter, one shared wiring and a folder per framework, static or rendered on the server.
