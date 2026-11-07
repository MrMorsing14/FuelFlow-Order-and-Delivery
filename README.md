# FuelFlow

Order and delivery backend for a Danish heating oil and diesel supplier. Built with .NET 10, ASP.NET Core Web API, EF Core + PostgreSQL and a Blazor WebAssembly UI. A portfolio project, and a way to learn C#/.NET coming from Java/Spring Boot.

> Work in progress. The full README (architecture, business rules, CI/CD, design decisions) arrives in Phase 8.

## Run locally

```bash
podman compose up -d postgres      # or: docker compose up -d postgres
dotnet run --project src/FuelFlow.Api
```

In Development the API applies migrations and seeds demo data on startup (users, customers, products, delivery windows; credentials are in `src/FuelFlow.Api/appsettings.Development.json`).
Outside Development, `Jwt__SigningKey` (at least 32 bytes) and `ConnectionStrings__Default` must be set as environment variables; the app refuses to start without them.
Delivery regions (postal code ranges) are configured under `Delivery:Regions`. A background worker assigns confirmed orders to delivery windows every `Scheduling:IntervalMinutes` minutes (0 turns it off).
Scalar API docs: <http://localhost:5044/scalar>. Probes: `/health/live`, `/health/ready`.

## Tests

```bash
dotnet test
```

Integration tests start PostgreSQL with Testcontainers, so they need a running container engine.
With Podman, enable its Docker-compatible socket once and point Testcontainers at it:

```bash
systemctl --user enable --now podman.socket
export DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock
```

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/FuelFlow.Infrastructure --output-dir Persistence/Migrations
```
