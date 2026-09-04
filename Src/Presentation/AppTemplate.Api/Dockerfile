# syntax=docker/dockerfile:1
#
# The syntax directive floats on purpose: it selects the BuildKit *frontend*,
# which parses this file and is never part of the produced image. Only the base
# images below — which do ship in the image — are pinned to a patch version.
#
# Build context is the REPOSITORY ROOT, not this directory.
# (Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj sets <DockerfileContext>..\..\..</DockerfileContext>.)
#
#   docker build -f Src/Presentation/AppTemplate.Api/Dockerfile -t app-template-api:local .
#
# Base image tags are pinned to a patch version on purpose. Floating tags such as
# `10.0` silently change the contents of a "reproducible" build, and make it
# impossible to tell from the Dockerfile whether a CVE has been picked up yet.
# Bump these two ARGs deliberately; keep DOTNET_RUNTIME_TAG's patch level aligned
# with the Microsoft.* package band in Directory.Packages.props (currently 10.0.10).
ARG DOTNET_SDK_TAG=10.0.302-noble
ARG DOTNET_RUNTIME_TAG=10.0.10-alpine3.23


# ---------------------------------------------------------------------------
# build — restore + publish. Runs on the BUILD machine's architecture
# (--platform=$BUILDPLATFORM) so that cross-building for arm64 does not drag the
# whole SDK through QEMU emulation. The publish output is portable IL, so one
# build serves every target architecture; only the runtime image below is
# architecture-specific, and its tag is a multi-arch manifest.
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG} AS build

ARG BUILD_CONFIGURATION=Release

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src

# 1. Restore inputs ONLY. This layer stays cached until a project file or a
#    version pin changes, which is what makes incremental builds cheap.
#
#    global.json, Directory.Build.props and Directory.Packages.props MUST land
#    here, before `dotnet restore`: the repository uses Central Package
#    Management, so every PackageReference is version-less and restore fails
#    outright ("NU1008 / versions must be centrally managed") if
#    Directory.Packages.props is absent. global.json pins the SDK feature band.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]

# 2. Project files, one per project, laid out under Src/<layer>/. Every project the API
#    references transitively must appear here, and at its real path: restore reads the
#    ProjectReference graph, so a missing or misplaced entry fails this layer rather than
#    being silently picked up by the `COPY . .` further down. AppTemplate.Infrastructure.InMemory is
#    deliberately absent — it is a test-double module the API does not reference.
COPY ["Src/Domain/AppTemplate.Domain/AppTemplate.Domain.csproj",                                                     "Src/Domain/AppTemplate.Domain/"]
COPY ["Src/Application/AppTemplate.Application/AppTemplate.Application.csproj",                                      "Src/Application/AppTemplate.Application/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence/AppTemplate.Infrastructure.Persistence.csproj",     "Src/Infrastructure/AppTemplate.Infrastructure.Persistence/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Identity/AppTemplate.Infrastructure.Identity.csproj",           "Src/Infrastructure/AppTemplate.Infrastructure.Identity/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Email/AppTemplate.Infrastructure.Email.csproj",                 "Src/Infrastructure/AppTemplate.Infrastructure.Email/"]
COPY ["Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj",                                                     "Src/Presentation/AppTemplate.Api/"]

# Restoring the API transitively restores the five projects it references.
#
# Deliberately NO `--mount=type=cache` for ~/.nuget/packages here: the restored
# assets would live outside the layer, and the later `dotnet publish --no-restore`
# would fail if that cache were pruned between the two RUN steps. Docker's own
# layer cache already gives the same speed-up, keyed exactly on the project files
# copied above, with no such failure mode.
RUN dotnet restore "Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj"

# 3. Sources. .editorconfig comes along with this COPY and is required:
#    Directory.Build.props sets EnforceCodeStyleInBuild + TreatWarningsAsErrors,
#    so without it the container build would apply different code-style rules
#    than the developer's machine.
COPY . .

# 4. Publish. There is deliberately NO separate `dotnet build` stage: the old
#    Visual Studio scaffold compiled the whole solution twice and threw the first
#    result away. `dotnet publish` compiles and lays out the app in one pass.
#    --no-restore is correct here because step 2 already restored, and the
#    package graph cannot have changed since (only .cs files arrived in step 3).
RUN dotnet publish "Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj" \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        -p:UseAppHost=false \
        -o /app/publish


# ---------------------------------------------------------------------------
# final — runtime.
#
# Variant choice: aspnet:*-alpine.
#   * It defines APP_UID=1654 and creates the matching `app` user, but — verified
#     against the published image config — it does NOT set `User`, so the alpine
#     variant runs as root unless told otherwise. The `USER $APP_UID` below is
#     therefore load-bearing, not decoration. (The chiseled variant does set
#     User=1654 itself.)
#   * It ships /bin/sh and busybox wget, which is what makes the HEALTHCHECK
#     below possible. The smaller `-noble-chiseled` variant has neither a shell
#     nor any HTTP client, so a container-level healthcheck there would require
#     adding a health-probe entrypoint to the application code.
#   * musl is safe for this dependency set: Npgsql and MailKit are fully managed
#     (no native assets), and Directory.Build.props already sets
#     InvariantGlobalization=true, so the missing ICU is not a factor.
#   Trade-off a reviewer may raise: alpine keeps a package manager (apk) in the
#   image, which chiseled does not. If you would rather have the smaller attack
#   surface than the built-in healthcheck, switch DOTNET_RUNTIME_TAG to
#   10.0.10-noble-chiseled and drop the HEALTHCHECK stanza.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG} AS final

# A global ARG declared before the first FROM is NOT visible inside a stage body
# unless it is re-declared here — otherwise ${DOTNET_RUNTIME_TAG} below would
# silently expand to an empty string. TARGETARCH/TARGETPLATFORM are supplied by
# BuildKit and recorded so an image can be traced to what it was built for.
ARG DOTNET_RUNTIME_TAG
ARG TARGETARCH
ARG TARGETPLATFORM

LABEL org.opencontainers.image.title="AppTemplate.Api" \
      org.opencontainers.image.description="Clean Architecture .NET template — HTTP API" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.base.name="mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG}" \
      io.apptemplate.target-platform="${TARGETPLATFORM}" \
      io.apptemplate.target-arch="${TARGETARCH}"

# HTTP only, on 8080.
#
# The previous Dockerfile also declared EXPOSE 8081 for HTTPS, but nothing ever
# provisioned a certificate, ASPNETCORE_HTTPS_PORTS was never set, and no
# listener was bound — compose then published a port that could not answer.
# The explicit decision for this template is: TLS is terminated UPSTREAM
# (ingress controller, reverse proxy, or cloud load balancer) and the container
# speaks plain HTTP on a private network. If you need in-container TLS, mount a
# certificate and set ASPNETCORE_HTTPS_PORTS/Kestrel__Certificates__Default__*
# yourself — do not re-add a bare EXPOSE.
#
# Only ASPNETCORE_HTTP_PORTS is set — restating the base image's own default, so
# the port is visible here rather than inherited invisibly. ASPNETCORE_URLS is
# deliberately NOT set: it takes precedence over ASPNETCORE_HTTP_PORTS and would
# silently ignore a port override from compose or an orchestrator.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8080

WORKDIR /app
COPY --from=build /app/publish .

# Run unprivileged. The old scaffold ran as root, which is both unnecessary for a
# process that only listens on 8080 and a container-escape amplifier.
USER $APP_UID

# Liveness/readiness for `docker compose` and for orchestrators that honour the
# image's own healthcheck. Targets the /health endpoint mapped by the API.
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=5 \
    CMD wget --spider --quiet http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "AppTemplate.Api.dll"]
