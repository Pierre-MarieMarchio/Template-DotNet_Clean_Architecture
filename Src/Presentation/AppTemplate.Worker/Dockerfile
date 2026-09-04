# syntax=docker/dockerfile:1
#
# Mirrors Src/Presentation/AppTemplate.Api/Dockerfile exactly in structure and reasoning; see that
# file for the rationale behind each choice. Only the project paths and image metadata differ,
# and there is deliberately no HEALTHCHECK here — a background worker exposes no HTTP endpoint
# to probe. An orchestrator should watch the process itself (container exit code / restart policy)
# rather than a synthetic liveness endpoint added just to satisfy this file.
#
# Build context is the REPOSITORY ROOT, not this directory.
# (AppTemplate.Worker.csproj sets <DockerfileContext>..\..\..</DockerfileContext>.)
#
#   docker build -f Src/Presentation/AppTemplate.Worker/Dockerfile -t app-template-worker:local .
#
# Keep these two ARGs aligned with Src/Presentation/AppTemplate.Api/Dockerfile: both hosts ship
# from the same Microsoft.* package band in Directory.Packages.props (currently 10.0.10).
ARG DOTNET_SDK_TAG=10.0.302-noble
ARG DOTNET_RUNTIME_TAG=10.0.10-alpine3.23

# ---------------------------------------------------------------------------
# build — restore + publish. Same reasoning as the API's Dockerfile: build on the host
# architecture, publish portable IL, and let only the runtime stage be architecture-specific.
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG} AS build

ARG BUILD_CONFIGURATION=Release

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src

# 1. Restore inputs only — see the API's Dockerfile for why these three files must land here,
#    before `dotnet restore`, under Central Package Management.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]

# 2. Project files. Every project the worker references transitively must appear here, at its
#    real path. AppTemplate.Infrastructure.Email is deliberately absent: neither maintenance task
#    sends mail, and this host does not compose that module.
COPY ["Src/Domain/AppTemplate.Domain/AppTemplate.Domain.csproj",                                                     "Src/Domain/AppTemplate.Domain/"]
COPY ["Src/Application/AppTemplate.Application/AppTemplate.Application.csproj",                                      "Src/Application/AppTemplate.Application/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence/AppTemplate.Infrastructure.Persistence.csproj",     "Src/Infrastructure/AppTemplate.Infrastructure.Persistence/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Identity/AppTemplate.Infrastructure.Identity.csproj",           "Src/Infrastructure/AppTemplate.Infrastructure.Identity/"]
COPY ["Src/Presentation/AppTemplate.Worker/AppTemplate.Worker.csproj",                                               "Src/Presentation/AppTemplate.Worker/"]

RUN dotnet restore "Src/Presentation/AppTemplate.Worker/AppTemplate.Worker.csproj"

# 3. Sources. .editorconfig is required for the same reason noted in the API's Dockerfile:
#    Directory.Build.props enforces code style and warnings-as-errors in the build.
COPY . .

# 4. Publish, in one pass — no separate `dotnet build` stage. --no-restore is correct because
#    step 2 already restored and nothing since then changed the package graph.
RUN dotnet publish "Src/Presentation/AppTemplate.Worker/AppTemplate.Worker.csproj" \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        -p:UseAppHost=false \
        -o /app/publish

# ---------------------------------------------------------------------------
# final — runtime. Same base image and the same USER $APP_UID reasoning as the API's Dockerfile:
# the alpine variant defines APP_UID but does not set USER, so this line is load-bearing, not
# decoration.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG} AS final

ARG DOTNET_RUNTIME_TAG
ARG TARGETARCH
ARG TARGETPLATFORM

LABEL org.opencontainers.image.title="AppTemplate.Worker" \
      org.opencontainers.image.description="Clean Architecture .NET template — background maintenance worker" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.base.name="mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG}" \
      io.apptemplate.target-platform="${TARGETPLATFORM}" \
      io.apptemplate.target-arch="${TARGETARCH}"

# The aspnet runtime image is heavier than the plain dotnet/runtime image this console host
# would strictly need, but it keeps the base identical to the API's — one image to patch, one
# CVE feed to watch — for a process that runs on a schedule, not under load, where the extra
# ASP.NET Core assemblies on disk cost nothing at runtime.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /app
COPY --from=build /app/publish .

# Run unprivileged, same as the API.
USER $APP_UID

ENTRYPOINT ["dotnet", "AppTemplate.Worker.dll"]
