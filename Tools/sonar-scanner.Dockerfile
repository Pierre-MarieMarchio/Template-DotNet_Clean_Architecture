# syntax=docker/dockerfile:1

# The image the local Sonar analysis runs in, and the reason CONTRIBUTING.md can still say the
# prerequisites are the SDK from global.json and Docker.
#
# SonarScanner for .NET is a .NET tool that shells out to a JRE, so an analysis needs both
# toolchains present at once. Installing a JDK on the developer's machine would add a second
# language runtime to a repository that deliberately has none; putting it here means Docker
# supplies the Java, which the prerequisite list already asks for.
#
# Build context is the REPOSITORY ROOT, like the other two Dockerfiles:
#
#   docker build -f Tools/sonar-scanner.Dockerfile -t app-template-sonar-scanner:local .
#
# The SDK tag is pinned to the same patch version as Src/Presentation/AppTemplate.Api/Dockerfile.
# Keep the two in step: an analysis that builds with a different SDK than the image ships is a
# difference nothing here would report.
ARG DOTNET_SDK_TAG=10.0.302-noble

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG}

# Java 21, not 17: analyses running on a Java runtime below 21 stopped being supported on
# 20 July 2026. The package comes from the standard archive of the SDK image's own Ubuntu 24.04
# base, so no third-party apt source is added to reach it.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends openjdk-21-jre-headless \
    && rm -rf /var/lib/apt/lists/*

# The scanner itself is deliberately NOT installed here. It is pinned in
# .config/dotnet-tools.json next to dotnet-ef and restored at run time from the mounted checkout,
# so the version this image runs is the version CI runs; baking it in would create a second pin
# that could drift from the first without anything failing.
WORKDIR /repo
