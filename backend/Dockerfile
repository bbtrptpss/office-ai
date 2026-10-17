FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OfficeAI.slnx ./
COPY src/OfficeAI.Domain/OfficeAI.Domain.csproj src/OfficeAI.Domain/
COPY src/OfficeAI.Application/OfficeAI.Application.csproj src/OfficeAI.Application/
COPY src/OfficeAI.Infrastructure/OfficeAI.Infrastructure.csproj src/OfficeAI.Infrastructure/
COPY src/OfficeAI.Api/OfficeAI.Api.csproj src/OfficeAI.Api/
RUN dotnet restore src/OfficeAI.Api/OfficeAI.Api.csproj
COPY src/ src/
RUN dotnet publish src/OfficeAI.Api/OfficeAI.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "OfficeAI.Api.dll"]
