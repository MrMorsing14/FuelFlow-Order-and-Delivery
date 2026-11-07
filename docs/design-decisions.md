# Design decisions and trade-offs

Each entry says what was decided, why, and what it costs. Entries marked *(Spring)* compare with how a Java/Spring Boot project would do it, because this project is also a way to learn .NET.

## Structure

### 1. Four layers with the dependency pointing inward
- **Decision:** Domain, Application, Infrastructure, Api. The Domain has no dependencies. Application defines interfaces; Infrastructure implements them.
- **Why:** Business rules (pricing, state machine, scheduling) can be tested without a database or a web server. 172 Domain tests run in about a second.
- **Cost:** More projects and some mapping code. For a project this size a single project would also work, but then the rules would not be forced to stay clean.

### 2. No MediatR, no AutoMapper
- **Decision:** Controllers call application services directly. Objects are mapped by hand (`ToDto()` methods).
- **Why:** MediatR and AutoMapper moved to commercial licenses, and a direct call is easier to read and debug than a pipeline. Hand mapping is a few lines per type and the compiler checks it.
- **Cost:** A little repetition. Cross-cutting behaviour (validation, transactions) is explicit in each service instead of automatic.

### 3. Rich domain model with real behaviour
- **Decision:** `Order` changes state only through methods (`Confirm`, `Cancel`, ...). `PriceCalculator` is the one place for money maths. Setters are private.
- **Why:** A rule cannot be skipped by forgetting to call it. The state machine has a test for every status and every action, with the expected legal moves written out in the test, independent of the code.
- **Cost:** EF Core needs private parameterless constructors and some mapping configuration.

### 4. Controllers, not minimal APIs
- **Decision:** `[ApiController]` classes with attributes.
- **Why:** Familiar from Spring, and the attributes (`[Authorize(Roles = ...)]`, `[EnableRateLimiting]`) keep the access rules visible next to each action.

## Data

### 5. PostgreSQL `xmin` for optimistic concurrency
- **Decision:** `orders` and `delivery_windows` use PostgreSQL's built-in `xmin` column as a concurrency token. A lost race becomes a 409.
- **Why:** No extra column to maintain, and no database locks held while a user thinks. It protects exactly the dangerous cases: two replicas scheduling the same order, a customer cancelling while a dispatcher confirms, two bookings into the last free litres of a window. *(Spring: like `@Version`.)*
- **Cost:** The loser must retry. The scheduler simply picks the order up at the next run. The token is a hidden "shadow property", so the Domain classes do not know about it.
- **Alternative rejected:** row locks (`SELECT ... FOR UPDATE`) would hold connections and need care about lock order.

### 6. Price snapshot on the order, price history for products
- **Decision:** An order stores its full price breakdown at order time. Product prices are an append-only history with "valid from". Final totals after delivery are recalculated from the snapshot, keeping the order-time discount rate and fee.
- **Why:** An old order must never change when prices or rules change. Keeping the discount and fee on delivery means a truck that delivers 4 percent less does not cost the customer a lost discount.
- **Cost:** The same money fields exist twice on an order (price and final price). They are stored as columns on the `orders` row, so it is one read.

### 7. Regions as postal code ranges in configuration
- **Decision:** Regions are config, not a table. The region of an address is calculated, not stored.
- **Why:** Changing a range needs no data migration. The app validates the setup at startup (no overlaps, names unique) and refuses to run on bad config.
- **Cost:** Windows store the region *name*. Renaming a region in config leaves old windows unmatched until they are updated.

### 8. Migrations and demo data only in Development
- **Decision:** `ASPNETCORE_ENVIRONMENT=Development` migrates and seeds. Anything else expects the database to be ready.
- **Why:** Several replicas starting at once must not all run migrations. Demo accounts must never appear in production. Seed credentials are read from `appsettings.Development.json`, which is deleted from the Docker image.

## Behaviour

### 9. Domain exceptions mapped by one global handler
- **Decision:** The Domain throws plain exceptions. `ConflictDomainException` is the base for everything that means 409; `DomainException` means 400. One `IExceptionHandler` turns them into ProblemDetails. *(Spring: `@ControllerAdvice`.)*
- **Why:** Controllers stay free of try/catch, and a new conflict type becomes a 409 just by deriving from the base class.

### 10. Validation in two places
- **Decision:** FluentValidation checks the *shape* of input and gives field-by-field errors. Domain methods enforce the *rules* again.
- **Why:** The UI gets good error lists, and the rules still hold when something other than the API calls the Domain (a test, the scheduler, a future import).
- **Cost:** A few checks appear twice (for example the CVR check). Business-rule errors (minimum order, tank capacity) come back as a message, not per field.

### 11. 404 instead of 403 for other people's data
- **Decision:** Records are looked up by `(id, customerId)`. If it is not yours, it "does not exist".
- **Why:** A 403 would confirm the id exists. Tests check that the body does not leak the id.

### 12. A pure scheduler, applied one assignment at a time
- **Decision:** `DeliveryScheduler.Plan` is a static pure function (no database, no clock, no mutation). `SchedulingService` loads data, calls it, then applies each assignment in its own save. `SchedulingWorker` only calls the service on a timer.
- **Why:** The hard logic is testable with simple in-memory objects. Applying assignments one by one means one lost race does not undo the rest.
- **Cost:** The plan can be slightly out of date by the time it is applied. That is fine: stale assignments are skipped and retried.
- **Not done:** no leader election. Every replica runs the worker and the database settles conflicts. That wastes a little work but needs no extra infrastructure.

### 13. "At least one day ahead" means the next calendar day (UTC)
- **Decision:** A window is usable if its date is tomorrow or later by the UTC date.
- **Why:** Simple and testable. All times are UTC by rule.
- **Cost:** For a Danish business, "tomorrow" starts one or two hours earlier than UTC midnight. A window for the Danish date could be planned a couple of hours "too soon". Switching to a Danish time zone would be a one-place change in the scheduler.

## Security

### 14. ASP.NET `PasswordHasher`, not full ASP.NET Identity
- **Decision:** Use only the hasher (PBKDF2 with salt and versioning) behind a small `IPasswordService`.
- **Why:** Identity brings its own tables, user model and cookie logic that this project does not need.

### 15. Short-lived JWT, no refresh token
- **Decision:** HS256, 60 minutes, claims `sub`, `email`, `role`, `customer_id`. No `nbf` claim. The signing key must be at least 32 bytes or the app will not start.
- **Why:** Simple and stateless. The `nbf` ("not before") claim was removed on purpose: with several replicas, a clock a few milliseconds behind would reject a token that was just issued.
- **Cost:** A role change or a stolen token stays valid until it expires. Expiry is checked against the injected `TimeProvider`.

### 16. Login throttling and forwarded headers
- **Decision:** The built-in rate limiter allows 10 login or register attempts per minute per address. Behind nginx, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes it use the real client address.
- **Why:** Each attempt costs a slow password hash and invites guessing.
- **Cost:** Only enable forwarded headers when the API cannot be reached directly, otherwise clients can fake their address.

### 17. Same answer for wrong password and unknown email
- **Decision:** Both return the same 401 text.
- **Why:** The API does not reveal which emails are registered. (Registering an existing email does return 409, so this is hardening, not secrecy.)

## Web UI

### 18. Blazor WebAssembly served from nginx, one address for UI and API
- **Decision:** Standalone Blazor WASM. nginx serves the files and forwards `/api/` to the API.
- **Why:** No CORS to configure. The UI is just static files, so it is cheap to host and scale.
- **Cost:** The first load downloads the .NET runtime with the app: about 9 MB of files, about 3.5 MB over the wire because nginx serves the ready-made gzip copies. (Brotli copies would be smaller, about 2.7 MB, but the nginx image has no Brotli module.) Later visits use the browser cache. The UI needs compose (or an equivalent proxy) to run.

### 19. JWT in sessionStorage
- **Decision:** The token is kept in memory and in `sessionStorage`.
- **Why:** A reload keeps you logged in, and closing the tab logs you out.
- **Cost:** Any script on the page can read it, so an XSS bug could steal it. Blazor escapes all output and the app never writes raw HTML. See the README for the options considered (httpOnly cookie with a backend-for-frontend, memory only).

### 20. Plain CSS and explicit Danish formatting
- **Decision:** No CSS framework. Numbers are formatted with an explicit Danish pattern (`12.500,00 kr.`) instead of the "da-DK" culture.
- **Why:** The browser build does not ship Danish culture data and silently falls back to English. An explicit format is the same everywhere.

## Testing and delivery

### 21. Real PostgreSQL in tests
- **Decision:** Integration tests use Testcontainers with PostgreSQL 17 and the real app through `WebApplicationFactory`. One container is shared and tables are emptied between tests.
- **Why:** The important behaviour (xmin concurrency, unique indexes, `ANY(...)` queries, migrations) only exists in the real database. In-memory fakes would have hidden bugs that these tests found.
- **Cost:** Tests need a container engine. With Podman, enable its Docker-compatible socket (see the README).

### 22. Controllable time in tests
- **Decision:** All code uses `TimeProvider`. Unit tests use `FakeTimeProvider`. Integration tests use a small settable clock.
- **Why:** `FakeTimeProvider` cannot move backwards, so it cannot be reset between tests. Tokens, price validity, scheduling and expiry are all tested without waiting.

### 23. Hardened, scanned images
- **Decision:** The API runs on the chiseled .NET image (no shell) as a non-root user. The web image uses non-root nginx and upgrades OS packages during the build. CD scans both with Trivy and fails on CRITICAL findings before anything is pushed.
- **Why:** Less to attack, and known critical problems cannot reach the registry. The scan did catch real findings during development (nginx and OpenSSL) and the build was fixed.
- **Cost:** A new CVE can fail a build on a day when nothing in the code changed. Dependabot proposes base image updates weekly.

### 24. Quality gates in the build
- **Decision:** Warnings are errors, nullable reference types are on, package versions are central (`Directory.Packages.props`), and `dotnet format --verify-no-changes` runs in CI.
- **Why:** Problems show up in the pull request, not in review comments.

### 25. xUnit v3 on Microsoft Testing Platform
- **Decision:** `coverlet.MTP` for coverage and TRX files for results.
- **Why:** xUnit v3 uses the new platform, and the .NET 10 SDK requires an opt-in (`global.json`) for it. The older VSTest packages do not apply.

## Known shortcuts

These are marked in the code with a `shortcut:` comment that says when to upgrade.

| Shortcut | Where | Upgrade when |
|---|---|---|
| Order responses expose the Domain `PriceBreakdown` record directly | `OrderService.cs` | The API price shape must differ from the Domain |
| Delivery window list is not paged | `DispatchService.cs` | Date ranges get wide |
| The scheduler loads all confirmed orders at once | `Repositories.cs` | The backlog reaches many thousands |
| Rate limit is per connection address | `Program.cs` | Several proxies sit in front of the API |

## Known limitations and next steps

| Limitation | Effect | Possible next step |
|---|---|---|
| No UI to create delivery windows or change prices | Dispatchers and admins use the API for these | Add two small pages |
| An order bigger than any window never schedules | Stays Confirmed; only a warning in the log | Reject at order time, or let dispatch split it |
| No Content-Security-Policy | Weaker defence against XSS | Add a CSP with a hash for Blazor's import map |
| The worker runs on every replica | Some duplicated work | Leader election, or a single worker deployment |
| No end-to-end browser test in CI | A boot failure in the browser is caught late | Add a Playwright smoke test against compose |
| No Kubernetes manifests yet | Deployment is compose only | Manifests with the existing health endpoints and environment variables |
| Windows must be created one by one | Busy work for dispatchers | A "create for the next 14 days" action |
