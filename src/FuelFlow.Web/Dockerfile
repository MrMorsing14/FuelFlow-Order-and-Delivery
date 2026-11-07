# Build from the repository root:  docker build -f src/FuelFlow.Web/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FuelFlow.Web/FuelFlow.Web.csproj src/FuelFlow.Web/
RUN dotnet restore src/FuelFlow.Web/FuelFlow.Web.csproj
COPY src/FuelFlow.Web src/FuelFlow.Web
RUN dotnet publish src/FuelFlow.Web/FuelFlow.Web.csproj -c Release --no-restore -o /out

# The "unprivileged" nginx image runs as a non-root user and listens on 8080.
FROM docker.io/nginxinc/nginx-unprivileged:1.31-alpine AS final
# Pull in security fixes that are newer than the base image (the Trivy scan in CD fails the build on CRITICAL findings).
USER root
RUN apk upgrade --no-cache
USER 101

# nginx fills in ${API_UPSTREAM} from the environment when the container starts.
COPY src/FuelFlow.Web/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /out/wwwroot /usr/share/nginx/html
ENV API_UPSTREAM=http://api:8080
EXPOSE 8080
