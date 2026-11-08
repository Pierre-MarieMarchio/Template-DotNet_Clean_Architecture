# syntax=docker/dockerfile:1.27.1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e

# Build from the repository root: docker build -f src/Presentation/AT.Worker/Dockerfile -t app-template-worker:local .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build

ARG BUILD_CONFIGURATION=Release

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src

# Restore inputs first, so this layer stays cached until a project file or a version pin changes.
COPY ["global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "./"]

# Every project the worker references, at its real path.
COPY ["src/Domain/AT.Domain.Core/AppTemplate.Domain.Core.csproj",                                       "src/Domain/AT.Domain.Core/"]
COPY ["src/Domain/AT.Domain/AppTemplate.Domain.csproj",                                                 "src/Domain/AT.Domain/"]
COPY ["src/Application/AT.Application.Core/AppTemplate.Application.Core.csproj",                        "src/Application/AT.Application.Core/"]
#if (localAuth)
COPY ["src/Application/AT.Application.Auth/AppTemplate.Application.Auth.csproj",                        "src/Application/AT.Application.Auth/"]
#endif
COPY ["src/Application/AT.Application/AppTemplate.Application.csproj",                                  "src/Application/AT.Application/"]
COPY ["src/Presentation/AT.Presentation.Core/AppTemplate.Presentation.Core.csproj",                     "src/Presentation/AT.Presentation.Core/"]
COPY ["src/Infrastructure/AT.Infrastructure.Core/AppTemplate.Infrastructure.Core.csproj",             "src/Infrastructure/AT.Infrastructure.Core/"]
COPY ["src/Infrastructure/AT.Infrastructure.Persistence/AppTemplate.Infrastructure.Persistence.csproj", "src/Infrastructure/AT.Infrastructure.Persistence/"]
#if (localAuth)
COPY ["src/Infrastructure/AT.Infrastructure.Auth/AppTemplate.Infrastructure.Auth.csproj",       "src/Infrastructure/AT.Infrastructure.Auth/"]
#endif
COPY ["src/Infrastructure/AT.Infrastructure.Email/AppTemplate.Infrastructure.Email.csproj",             "src/Infrastructure/AT.Infrastructure.Email/"]
#if (files)
COPY ["src/Infrastructure/AT.Infrastructure.Storage/AppTemplate.Infrastructure.Storage.csproj",         "src/Infrastructure/AT.Infrastructure.Storage/"]
#endif
COPY ["src/Presentation/AT.Worker/AppTemplate.Worker.csproj",                         "src/Presentation/AT.Worker/"]

RUN dotnet restore "src/Presentation/AT.Worker/AppTemplate.Worker.csproj"

COPY . .

RUN dotnet publish "src/Presentation/AT.Worker/AppTemplate.Worker.csproj" \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        -p:UseAppHost=false \
        -o /app/publish

# The API's runtime image: the identity module needs ASP.NET Core, and the two images share its layers.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-alpine3.24@sha256:486297ae006e95ee2ee3112a2c03e3d26e41fbbc64b7a29e28e7a18cc36b12bb AS final

ARG TARGETARCH
ARG TARGETPLATFORM

LABEL org.opencontainers.image.title="AppTemplate.Worker" \
      org.opencontainers.image.description="Clean Architecture .NET template — worker" \
      org.opencontainers.image.licenses="MIT" \
      io.apptemplate.target-platform="${TARGETPLATFORM}" \
      io.apptemplate.target-arch="${TARGETARCH}"

# No port: the worker serves no HTTP.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /app
COPY --from=build /app/publish .
#if (sqlServer)

# SqlClient needs ICU, which the alpine image leaves out and turns off.
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
#endif

# The alpine image defines this user without switching to it.
USER $APP_UID

ENTRYPOINT ["dotnet", "AppTemplate.Worker.dll"]
