# 0022. A presentation project reaches the core through the API or the application layer

Status: Accepted

## Context

The API is this project's first presentation, and not necessarily its last: a server-rendered site,
a desktop client or a second API may follow. Each needs the use cases. One that runs on the server
can call them in its own process, with the database and the modules beside it; one that runs on a
user's machine cannot, and must go through the HTTP contract. Two hosts of the same use cases also
raise what the second one composes, who its caller is, and whether it runs what only one instance
should run.

## Decision

- **Where it runs decides the path.** A presentation project that runs on the server, such as a
  Blazor Web App with server interactivity, references the application layer and the
  infrastructure modules it needs and calls the use cases in process. One that runs on the
  client, a web app or a desktop app, calls the API through a client generated from its document,
  and references neither layer.
- **Each host composes itself.** A host has its own composition root in its own project: the
  feature registrations, the modules, its caller's identity, its authentication. Nothing composes
  a host on another's behalf, and no host references another;
  `NoHost_ReferencesAnotherHost` holds it, and `OnlyAHost_ReferencesInfrastructureModules` treats
  every project with a `Program.cs` alike, but a client: a project that references the .NET client
  is no host, and references that client alone
  (`EveryClient_ReachesTheCoreThroughTheApiAlone`).
<!--#if (!jobsWorker)-->
- **Jobs and migrations stay in the API.** A second host runs no background job, as
  [ADR 0011](0011-jobs-run-in-the-api-under-a-lease.md) has it, and migrates nothing: the
  methods that do both live in the API, which no other host may reference. `TheJobs_RunInOneHost`
  holds that one host alone calls `AddBackgroundJobs`, the one that holds the jobs' module.
<!--#endif-->
<!--#if (jobsWorker)-->
- **The worker runs the jobs, the API migrates.** The worker is the one host that runs background
  jobs, as [ADR 0025](0025-jobs-run-in-a-worker-under-a-lease.md) has it, and the API the one that
  migrates: the methods that do each live in that host, which no other host may reference.
  `TheJobs_RunInOneHost` holds that one host alone calls `AddBackgroundJobs`, the one that holds
  the jobs' module.
<!--#endif-->
- **What two hosts genuinely share and is no composition** moves to the layer where it belongs,
  never to a project of its own: settings that describe a provider, or a mapping that knows no
  host, go to `Presentation.Core` while it stays free of ASP.NET Core; the rest each host keeps.
<!--#if (presentationBlazorServer)-->
- **The presentation project of this repository** composes the use cases in process. Its caller is
  the circuit's or the request's principal, given to each operation's own scope, so a use case
  never shares a database context across a circuit's lifetime.
<!--#endif-->
<!--#if (presentationAvalonia)-->
- **The presentation project of this repository** is a desktop client. It calls the API through
  the .NET client and composes only that client, its settings and its view models; its caller is
  whoever the API's token names.
<!--#endif-->

## Alternatives considered

- **One project that composes every host.** It would hold the feature lines once, and would make a
  host's composition a call into a project that serves several masters; a host that needs one
  module less or one adapter more would have to work around it.
- **Every presentation project through the API.** A server-rendered site would make an HTTP call
  for each use case, carry a token for each user, and duplicate the API's contract in a client,
  for a process that could call the use case directly.
- **Jobs in whichever host runs.** Two hosts would run every pass twice, and the lease would be the
  only thing between them, for passes that are not worth running twice.

## Consequences

- A feature's registration is one line in each host that composes features; `new-feature` prints
  the line for every host it finds.
- A setting the use cases read is set for every host that composes them.
- A host's identity, its sign-in and its session belong to it: the API keeps its bearer tokens,
  a server-rendered host its cookie, and each maps its principal to the same `UserId`.

## Revisit when

Several server hosts need the same composition so often that keeping it in step costs more than a
shared project would, or a host needs to run work only one instance may run.
