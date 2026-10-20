# syntax=docker/dockerfile:1.27.1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

# Build from the repository root: docker build -f Src/Presentation/AppTemplate.Api/Dockerfile -t app-template-api:local .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302-noble@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build

ARG BUILD_CONFIGURATION=Release

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src

# Restore inputs first, so this layer stays cached until a project file or a version pin changes.
COPY ["global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "./"]

# Every project the API references, at its real path.
COPY ["Src/Domain/AppTemplate.Domain.Core/AppTemplate.Domain.Core.csproj",                                       "Src/Domain/AppTemplate.Domain.Core/"]
COPY ["Src/Domain/AppTemplate.Domain/AppTemplate.Domain.csproj",                                                 "Src/Domain/AppTemplate.Domain/"]
COPY ["Src/Application/AppTemplate.Application.Core/AppTemplate.Application.Core.csproj",                        "Src/Application/AppTemplate.Application.Core/"]
#if (localAuth)
COPY ["Src/Application/AppTemplate.Application.Auth/AppTemplate.Application.Auth.csproj",                        "Src/Application/AppTemplate.Application.Auth/"]
#endif
COPY ["Src/Application/AppTemplate.Application/AppTemplate.Application.csproj",                                  "Src/Application/AppTemplate.Application/"]
COPY ["Src/Presentation/AppTemplate.Presentation.Core/AppTemplate.Presentation.Core.csproj",                     "Src/Presentation/AppTemplate.Presentation.Core/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Core/AppTemplate.Infrastructure.Core.csproj",             "Src/Infrastructure/AppTemplate.Infrastructure.Core/"]
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence/AppTemplate.Infrastructure.Persistence.csproj", "Src/Infrastructure/AppTemplate.Infrastructure.Persistence/"]
#if (localAuth)
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Auth/AppTemplate.Infrastructure.Auth.csproj",       "Src/Infrastructure/AppTemplate.Infrastructure.Auth/"]
#endif
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Email/AppTemplate.Infrastructure.Email.csproj",             "Src/Infrastructure/AppTemplate.Infrastructure.Email/"]
#if (files)
COPY ["Src/Infrastructure/AppTemplate.Infrastructure.Storage/AppTemplate.Infrastructure.Storage.csproj",         "Src/Infrastructure/AppTemplate.Infrastructure.Storage/"]
#endif
COPY ["Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj",                                                 "Src/Presentation/AppTemplate.Api/"]

RUN dotnet restore "Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj"

COPY . .

RUN dotnet publish "Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj" \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        -p:UseAppHost=false \
        -o /app/publish

# For a smaller surface, use aspnet:10.0.13-noble-chiseled and drop the HEALTHCHECK, which needs alpine's wget.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24@sha256:819bd3649d8bf53f35527c68d62e17b348ef6f24ae73073f497f00a878348062 AS final

ARG TARGETARCH
ARG TARGETPLATFORM

LABEL org.opencontainers.image.title="AppTemplate.Api" \
      org.opencontainers.image.description="Clean Architecture .NET template — HTTP API" \
      org.opencontainers.image.licenses="MIT" \
      io.apptemplate.target-platform="${TARGETPLATFORM}" \
      io.apptemplate.target-arch="${TARGETARCH}"

# HTTP only, TLS terminating upstream; ASPNETCORE_URLS stays unset, as it would override a port set from outside.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8080

WORKDIR /app
COPY --from=build /app/publish .

# The alpine image defines this user without switching to it.
USER $APP_UID

HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=5 \
    CMD wget --spider --quiet http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "AppTemplate.Api.dll"]
