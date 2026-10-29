# Documentation

Which page answers your question, and where a new page goes.

## How these pages are organised

| Folder | A page belongs here when the reader is |
|---|---|
| [getting-started](getting-started/overview.md) | new to this project, reading once and in order |
| [architecture](architecture/README.md) | learning the rules every layer, feature and endpoint follows |
| features | using, configuring or removing one capability |
| guides | doing a task again: add, remove, migrate, test, ship |
| reference | looking up a setting, a route, a code or a folder |
| [decisions](decisions/README.md) | asking why a choice was made |

## Getting started

| Page | Answers |
|---|---|
| [Overview](getting-started/overview.md) | what this project is, and how it was generated |
| [First run](getting-started/first-run.md) | from a clone to an authenticated call and a first feature |
| [Existing repository](getting-started/existing-repository.md) | moving this project into a repository that already exists |

## Architecture

| Page | Answers |
|---|---|
| [Overview](architecture/README.md) | the layers, the dependency rule, what is deliberately absent |
| [Layers and projects](architecture/layers-and-projects.md) | which projects make each layer, and how the host is composed |
| [Feature shapes](architecture/feature-shapes.md) | an aggregate, or a service over a store |
| [Modules and boundaries](architecture/modules-and-boundaries.md) | what a feature or a module may depend on |
| [Ports and adapters](architecture/ports-and-adapters.md) | how the application reaches storage and the outside world |
| [Domain and results](architecture/domain-and-results.md) | how rules are enforced, failures reported and facts published |
| [Persistence model](architecture/persistence-model.md) | how data is mapped, saved and protected from lost updates |
| [Request pipeline](architecture/request-pipeline.md) | what happens to every request on its way in and out |
| [HTTP API](architecture/http-api.md) | the contract every endpoint offers a client |
| [Shared mechanisms](architecture/shared-mechanisms.md) | the cache, outbound HTTP, the clock, the current user |

## Features

| Page | Answers |
|---|---|
| [Authentication](features/authentication.md) | who can call the API, and how a caller proves it |
<!--#if (accounts)-->
| [Accounts](features/accounts.md) | how an account lives, from registration to deletion |
<!--#endif-->
<!--#if (files)-->
| [Files](features/files.md) | how files are stored, inspected and served |
<!--#endif-->
| [Background jobs](features/background-jobs.md) | what runs on a timer, and on which instance |
| [Email](features/email.md) | how mail is sent, and in which language |
| [Observability](features/observability.md) | logs, metrics, traces and health checks |
<!--#if (anyWeb)-->
| [Web front end](features/web-frontend.md) | the web apps beside the API, and signing users in from them |
<!--#endif-->
<!--#if (presentationBlazorServer)-->
| [Presentation project](features/presentation.md) | the server-rendered project beside the API, and signing people in to it |
<!--#endif-->
<!--#if (presentationAvalonia)-->
| [Presentation project](features/presentation.md) | the desktop app beside the API, and signing in to the API from it |
<!--#endif-->
<!--#if (presentationMcp)-->
| [Presentation project](features/presentation.md) | the MCP server beside the API, its tools, and who may call them |
<!--#endif-->

## Guides

| Page | Answers |
|---|---|
| [Adding a feature](guides/adding-a-feature.md) | generating a feature and finishing it by hand |
| [Removing features](guides/removing-features.md) | taking out a feature, the examples, Files or authentication |
| [Adding a setting](guides/adding-a-setting.md) | a value a deployment chooses, validated at start-up and documented |
| [Migrations](guides/migrations.md) | adding, checking and applying a migration |
| [Testing](guides/testing.md) | running the suites and writing new tests |
| [Build and CI](guides/build-and-ci.md) | the gate, locally and in CI |
| [Deployment](guides/deployment.md) | shipping the image to an environment |

## Reference

| Page | Answers |
|---|---|
| [Configuration](reference/configuration.md) | what each setting does |
| [API](reference/api.md) | every route and every error code |
| [Project layout](reference/project-layout.md) | where a file goes, and what it is called |

## Decisions

[The decision index](decisions/README.md) lists every ADR. [ADR 0001](decisions/0001-adopt-the-generated-baseline.md)
records the choices this project started with.

## Adding a page

| You add | Where | Start from |
|---|---|---|
| A feature | `features/<feature>.md`, and a row under Features above | [the feature template](_page-templates/feature.md); `new-feature` writes it for you |
| A how-to | `guides/<task>.md`, and a row under Guides | [the how-to template](_page-templates/how-to.md) |
| A decision | `decisions/NNNN-<title>.md`, numbered from 1000, and a row in the decision index | [the ADR template](_page-templates/adr.md) |
| A setting | a section in the configuration reference | the table shape there |
| A route or a code | a row in the API reference | the table shape there |

<!--#if (agentFiles)-->
[AGENTS.md › Writing docs](../AGENTS.md#writing-docs) holds the rules for writing them.
<!--#endif-->
