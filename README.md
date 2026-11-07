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

## Docker

```bash
podman compose up --build      # or: docker compose up --build
```

Starts postgres, the API (<http://localhost:8080/scalar>) and the web UI (<http://localhost:8081>, nginx, which also forwards `/api/` to the API).
The images (`src/FuelFlow.Api/Dockerfile`, `src/FuelFlow.Web/Dockerfile`, built from the repository root) run as non-root. The API image is distroless-style (no shell) and does not contain the development settings file; compose mounts it for local use.

## Web UI

Blazor WebAssembly (standalone), plain CSS. Customers register, log in, manage delivery addresses, get a price quote, place orders, see and cancel their orders.
Dispatchers and admins see all orders, filter them by status, region and delivery date, and move them along (confirm, out for delivery, delivered with the delivered litres).
Prices follow the customer type: private customers see totals including VAT, business customers excluding VAT; the full breakdown is always shown.

The UI talks to the API on its own address (nginx forwards `/api/`), so it runs through compose; there is no separate dev server setup.

### Where the login token lives (security trade-off)

The JWT is kept in memory and copied to **sessionStorage**, so a page reload keeps you logged in; closing the tab logs you out.
The catch: any JavaScript running on the page can read sessionStorage, so a cross-site scripting (XSS) bug could steal the token. Blazor escapes everything it renders and the app never writes raw HTML, which is the main defence, and tokens expire after 60 minutes (there are no refresh tokens).
Alternatives and why they were not chosen: an httpOnly cookie hides the token from scripts but needs CSRF protection and a proxy that holds the session (the "backend for frontend" pattern), which is more moving parts than this project wants; keeping the token only in memory is safer but logs you out on every reload.
Possible hardening later: a Content-Security-Policy header (Blazor's import map is an inline script, so it needs a hash or nonce first) and a backend-for-frontend.
The UI reads the role from the token to decide which pages to show. That is convenience, not security: the API checks the signed token on every request.

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
