# API reference

A short map of every endpoint. In Development the full, interactive reference (generated from the code) is at `/scalar` on the API, for example <http://localhost:8080/scalar> when running with compose.

All bodies are JSON. Property names are camelCase. Enums are strings (`"Private"`, `"Pending"`). Money is a decimal number. Times are UTC ISO 8601.

Authentication: send `Authorization: Bearer <token>` (get the token from `POST /api/auth/login`).

## Endpoints

### Auth

| Method and path | Who | What |
|---|---|---|
| `POST /api/auth/register` | anyone, rate limited | Create a customer account and profile. Returns 201 with `userId` and `customerId`. |
| `POST /api/auth/login` | anyone, rate limited | Returns `accessToken`, `tokenType`, `expiresAt` (60 minutes). |
| `GET /api/auth/me` | any logged-in user | Who the token says you are: `userId`, `email`, `role`, `customerId`. |

### Customer (role Customer)

| Method and path | What |
|---|---|
| `GET /api/customers/me` | Own profile |
| `PUT /api/customers/me` | Change phone and name (private) or company name and CVR (business) |
| `GET /api/customers/me/addresses` | List own addresses |
| `GET /api/customers/me/addresses/{id}` | One address (404 if it is not yours) |
| `POST /api/customers/me/addresses` | Add an address. 201 with `Location`. |
| `PUT /api/customers/me/addresses/{id}` | Replace an address. Street, postal code and city are frozen once the address has orders (409). |
| `DELETE /api/customers/me/addresses/{id}` | 204. 409 if the address has orders. |
| `POST /api/quotes` | Price an order without creating it |
| `POST /api/orders` | Place an order. 201 with `Location`. |
| `GET /api/orders?page=&pageSize=` | Own orders, newest first |
| `GET /api/orders/{id}` | One own order (404 for anyone else's) |
| `POST /api/orders/{id}/cancel` | Body `{ "reason": "..." }` |

### Products

| Method and path | Who | What |
|---|---|---|
| `GET /api/products` | anyone | Current price per litre excluding and including VAT |
| `PUT /api/products/{id}/price` | Admin | Body `{ "pricePerLitreExVat": 12.5, "validFrom": null }`. Leave `validFrom` out to change the price now, or give a future time. |

### Dispatch (role Dispatcher or Admin)

| Method and path | What |
|---|---|
| `GET /api/dispatch/orders?status=&region=&date=&page=&pageSize=` | All orders with customer, address and region. `date` is the planned delivery date. |
| `GET /api/dispatch/regions` | The configured regions and their postal code ranges |
| `POST /api/dispatch/orders/{id}/confirm` | Pending to Confirmed |
| `POST /api/dispatch/orders/{id}/out-for-delivery` | Scheduled to OutForDelivery |
| `POST /api/dispatch/orders/{id}/deliver` | Body `{ "deliveredLitres": 950 }`. Within plus or minus 10 percent of ordered. |
| `GET /api/dispatch/delivery-windows?from=&to=&region=` | Windows from today by default |
| `POST /api/dispatch/delivery-windows` | Body `{ "date": "2026-03-05", "region": "Copenhagen", "capacityLitres": 10000 }` |

### Health

| Path | What |
|---|---|
| `GET /health/live` | Process is up |
| `GET /health/ready` | Process is up and the database answers (503 otherwise) |

## Examples

Quote for a private customer (`POST /api/quotes`):

```json
{ "productId": "0195...", "deliveryAddressId": "0195...", "litres": 500 }
```

```json
{
  "litres": 500, "unitPriceExVat": 10.0000, "netAmount": 5000.00,
  "discountRate": 0, "discountAmount": 0.00, "smallOrderFee": 250,
  "totalExVat": 5250.00, "vatRate": 0.25, "vatAmount": 1312.50,
  "totalIncVat": 6562.50, "displayIncVat": true
}
```

A validation error (`400`, `application/problem+json`):

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "postalCode": ["Postal code must be 4 digits (1000-9999)."] }
}
```

A paged list:

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 3 }
```

## Error responses

Every error is an RFC 7807 ProblemDetails (`application/problem+json`). Unexpected errors return a generic 500 without internals and are logged on the server.

| Status | When | Raised by |
|---|---|---|
| 400 | Input is invalid: field errors keyed by field name | FluentValidation (`errors` object) |
| 400 | A business rule is broken (minimum order, tank capacity, delivered litres outside 10 percent, price in the past) | `DomainException` and `DomainValidationException` |
| 401 | No token, bad or expired token, wrong email or password | JWT middleware, `AuthenticationFailedException` |
| 403 | Logged in, but the role is not allowed | Authorization middleware |
| 404 | Not found, **or not yours** | `NotFoundException` |
| 409 | Invalid status change, window full, duplicate email, duplicate window, address in use, lost a concurrency race | `ConflictDomainException` and its subclasses |
| 429 | Too many login or register attempts | Rate limiter |
| 500 | A bug. Logged with the exception, answered with a generic message. | Fallback in `GlobalExceptionHandler` |
| 503 | `/health/ready` while the database is unreachable | Health check |
