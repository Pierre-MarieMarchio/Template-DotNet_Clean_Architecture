# Architecture

Written by `dotnet run Tools/Tasks.cs docs-index` from the frontmatter of each page:
edit the pages, then run it again.

- [Architecture](README.md) - How this project is layered, which way its source dependencies point, and which tests hold the rule.
- [Domain and results](domain-and-results.md) - How an aggregate enforces its rules, how a use case reports an expected failure as a value, and how a fact becomes a domain event.
- [Feature shapes](feature-shapes.md) - Which of the two shapes a feature takes, an aggregate with use cases or a service over a store, and the one question that chooses.
- [HTTP API](http-api.md) - The contract every endpoint offers a client, from versioning and the error format to conditional writes, safe retries and collections.
- [Layers and projects](layers-and-projects.md) - Which projects make up each layer, what each holds, and how a project declares what it references.
- [Modules and boundaries](modules-and-boundaries.md) - What a feature or an infrastructure module may depend on, so that each one stays removable.
- [Persistence model](persistence-model.md) - How this project stores its data, from the schema per feature and the rows apart from the aggregates to the save, deletion and concurrency.
- [Ports and adapters](ports-and-adapters.md) - How the application reaches storage and the outside world only through ports, where each port is declared and what implements it.
- [Request pipeline](request-pipeline.md) - The pipeline every request crosses, in its order, and why each step sits where it does.
- [Shared mechanisms](shared-mechanisms.md) - The mechanisms a feature composes without writing them, such as the cache, the outbound HTTP budget, the clock and the current caller.
