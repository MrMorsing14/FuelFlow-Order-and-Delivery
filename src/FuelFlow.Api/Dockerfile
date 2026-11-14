# Build from the repository root:  docker build -f src/FuelFlow.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Restore first, from project files only, so this slow layer is cached until a package changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FuelFlow.Domain/FuelFlow.Domain.csproj src/FuelFlow.Domain/
COPY src/FuelFlow.Application/FuelFlow.Application.csproj src/FuelFlow.Application/
COPY src/FuelFlow.Infrastructure/FuelFlow.Infrastructure.csproj src/FuelFlow.Infrastructure/
COPY src/FuelFlow.Api/FuelFlow.Api.csproj src/FuelFlow.Api/
RUN dotnet restore src/FuelFlow.Api/FuelFlow.Api.csproj

COPY src src
RUN dotnet publish src/FuelFlow.Api/FuelFlow.Api.csproj -c Release --no-restore -p:UseAppHost=false -o /app \
    # Development credentials (seed passwords, JWT dev key) must never ship in the image.
    && rm -f /app/appsettings.Development.json

# Chiseled = no shell, no package manager, minimal attack surface. Listens on port 8080 (not 80), so non-root works.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FuelFlow.Api.dll"]
