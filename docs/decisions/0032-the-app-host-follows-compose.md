# 0032. The app host follows Compose

Status: Accepted

## Context

This project runs its local stack with Docker Compose, and Compose is also the shape a deployment
copies: its services, images and settings. The Aspire app host offers what Compose lacks while
working: one command that starts the hosts as processes a debugger attaches to, and a dashboard that
shows their logs, traces and metrics. Two descriptions of one stack drift unless one of them is the
reference and a test holds the other to it.

## Decision

- Compose stays the reference; the app host follows it. Every service of `docker-compose.yml` and of
  the files it includes is a resource of `Src/AppHost/AT.AppHost` under the same name, and the
  reverse; each container runs the image its service pins, tag and digest alike, read from the Compose
  files when the app host starts; each setting a Compose file gives a host, the app host gives it too;
  each web app listens on the port its `Src/Web/apps/<App>/compose.yml` publishes, which
  `cleanarch.json` records. `LocalStackParityTests` holds the four, and lists by name each service or
  setting the app host leaves out, with its reason.
- The app host starts the hosts `cleanarch.json` records, not a list of its own, so a host or a web
  app the project gains later joins the stack without an edit to it.
- A secret comes from an Aspire parameter, generated and kept in the app host's user secrets, or set
  by the developer there; never from a value in the code.
- The dashboard and the orchestrator come from the Aspire CLI the app host's SDK pins, which `dnx`
  runs; no workload and no global tool are installed. `global.json` pins the SDK under
  `msbuild-sdks`, which Dependabot raises, and its version is the version of every Aspire package,
  which `TheAppHostSdk_IsPinnedInGlobalJson_AtTheVersionOfEveryAspirePackage` demands.

## Alternatives considered

- **The app host as the reference, Compose generated from it** with Aspire's publisher: the
  deployment would follow the orchestrator's model of the stack, and every change to an image or a
  setting would go through a generator the reviewers do not read.
- **Two descriptions kept in step by review**: a bumped image or a new setting reaches one of them
  only, and the stack a developer runs stops being the one a deployment runs.
- **The images written into the app host as well**: every Dependabot pull request on a Compose image
  would fail until someone copied the reference across.
- **A hand-written list of hosts in the app host**: a host added later with `cleanarch-presentation`
  or a web app added with `cleanarch-webapp` would be missing from it until someone remembered.
- **The orchestrator from NuGet packages**, Aspire's earlier default: Aspire now warns on it, and
  silencing the warning is a silence this project does not keep.

## Consequences

- A Dependabot pull request that bumps an image of the Compose files changes the app host's image
  with it, and its `aspire` group raises the SDK in `global.json` and the packages in one pull
  request.
- The first build of the app host downloads the Aspire CLI through `dnx`, which needs the network
  once per version.
- The app host's containers keep no data between runs: a run starts from an empty database, which
  the API migrates in Development.
- SQLite is refused with the app host: its file lives in a volume Compose shares between containers,
  which the processes the app host starts cannot share.

## Revisit when

Aspire can import a Compose file as its model, so that one description serves both, or the
deployment moves away from Compose.
