# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Src/Presentation/CA.Presentation.WebApi/CA.Presentation.WebApi.csproj", "Src/Presentation/CA.Presentation.WebApi/"]
COPY ["Src/Domain/CA.Domain/CA.Domain.csproj", "Src/Domain/CA.Domain/"]
COPY ["Src/Application/CA.Application/CA.Application.csproj", "Src/Application/CA.Application/"]
COPY ["Src/Infrastructure/CA.Infrastructure.Identity/CA.Infrastructure.Identity.csproj", "Src/Infrastructure/CA.Infrastructure.Identity/"]
COPY ["Src/Infrastructure/CA.Infrastructure/CA.Infrastructure.csproj", "Src/Infrastructure/CA.Infrastructure.Persistence/"]
RUN dotnet restore "Src/Presentation/CA.Presentation.WebApi/CA.Presentation.WebApi.csproj"
COPY . .
WORKDIR "/src/Src/Presentation/CA.Presentation.WebApi"
RUN dotnet build "CA.Presentation.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CA.Presentation.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CA.Presentation.WebApi.dll"]