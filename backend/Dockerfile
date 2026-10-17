FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OfficeAI.slnx ./
COPY src/OfficeAI.Domain/OfficeAI.Domain.csproj src/OfficeAI.Domain/
COPY src/OfficeAI.Application/OfficeAI.Application.csproj src/OfficeAI.Application/
COPY src/OfficeAI.Infrastructure/OfficeAI.Infrastructure.csproj src/OfficeAI.Infrastructure/
COPY src/OfficeAI.Api/OfficeAI.Api.csproj src/OfficeAI.Api/
COPY src/OfficeAI.SandboxRunner/OfficeAI.SandboxRunner.csproj src/OfficeAI.SandboxRunner/
RUN dotnet restore src/OfficeAI.Api/OfficeAI.Api.csproj && dotnet restore src/OfficeAI.SandboxRunner/OfficeAI.SandboxRunner.csproj
COPY src/ src/
RUN dotnet publish src/OfficeAI.Api/OfficeAI.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/OfficeAI.SandboxRunner/OfficeAI.SandboxRunner.csproj -c Release -o /out/runner --no-restore

# API: needs git for repositories; never gets the Docker socket.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data/repos && chown $APP_UID /data/repos
WORKDIR /app
COPY --from=build /out/api .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OfficeAI.Api.dll"]

# Sandbox runner: the only component with the Docker socket; it only runs allow-listed images with fixed limits.
FROM docker:27-cli AS dockercli
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runner
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
COPY --from=build /out/runner .
EXPOSE 8080
ENTRYPOINT ["dotnet", "OfficeAI.SandboxRunner.dll"]
