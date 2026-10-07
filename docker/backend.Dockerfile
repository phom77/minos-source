FROM docker:28.5.2-cli AS docker-cli

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Minos.sln ./
COPY src/Minos.Domain/*.csproj src/Minos.Domain/packages.lock.json src/Minos.Domain/
COPY src/Minos.Application/*.csproj src/Minos.Application/packages.lock.json src/Minos.Application/
COPY src/Minos.Infrastructure/*.csproj src/Minos.Infrastructure/packages.lock.json src/Minos.Infrastructure/
COPY src/Minos.Api/*.csproj src/Minos.Api/packages.lock.json src/Minos.Api/
COPY src/Minos.JudgeWorker/*.csproj src/Minos.JudgeWorker/packages.lock.json src/Minos.JudgeWorker/
COPY tests/Minos.Domain.Tests/*.csproj tests/Minos.Domain.Tests/packages.lock.json tests/Minos.Domain.Tests/
COPY tests/Minos.IntegrationTests/*.csproj tests/Minos.IntegrationTests/packages.lock.json tests/Minos.IntegrationTests/
RUN dotnet restore Minos.sln --locked-mode --artifacts-path /build
COPY . .
RUN dotnet build Minos.sln --no-restore -c Release --artifacts-path /build \
    && dotnet publish src/Minos.Api --no-restore -c Release --artifacts-path /build -o /out/api \
    && dotnet publish src/Minos.JudgeWorker --no-restore -c Release --artifacts-path /build -o /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl openssl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

FROM runtime AS api
COPY --from=build /out/api/ ./
COPY docker/init-volumes.sh /minos/init-volumes.sh
USER 1654:1654
EXPOSE 8443
ENTRYPOINT ["dotnet", "Minos.Api.dll"]

FROM runtime AS worker
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=build /out/worker/ ./
COPY docker/worker-entrypoint.sh /minos/worker-entrypoint.sh
COPY docker/worker-healthcheck.sh /minos/worker-healthcheck.sh
ENV DOCKER_HOST=unix:///var/run/docker.sock DOCKER_CONFIG=/tmp/docker-config
USER 1654:1654
ENTRYPOINT ["/bin/sh", "/minos/worker-entrypoint.sh"]

# Existing tests can exercise the real runner from a non-root Linux container.
FROM build AS checks
USER root
RUN apt-get update && apt-get install -y --no-install-recommends python3 openssl curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
ENV DOCKER_HOST=unix:///var/run/docker.sock DOCKER_CONFIG=/tmp/docker-config
USER 1654:1654
ENTRYPOINT ["dotnet", "test", "Minos.sln", "--no-build", "-c", "Release", "--artifacts-path", "/build"]
