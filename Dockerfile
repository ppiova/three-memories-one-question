# syntax=docker/dockerfile:1

# The build stage runs on the machine's own architecture and cross-compiles for the target one,
# so a multi-platform build (amd64 + arm64) needs no emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source

# Restore first, on its own layer: it is only redone when the project file changes.
COPY src/ThreeMemories/ThreeMemories.csproj src/ThreeMemories/
RUN dotnet restore src/ThreeMemories -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/ThreeMemories -a $TARGETARCH -c Release --no-restore -o /app \
    && mkdir /results

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
COPY scenarios/ scenarios/

# The run writes its report to /app/results; mount a folder there to keep it.
# 1654 is the non-root "app" user that the .NET images define.
COPY --from=build --chown=1654:1654 /results results/
USER 1654

ENTRYPOINT ["dotnet", "ThreeMemories.dll"]
