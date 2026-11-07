# Flows

How work moves between people and parts of the system. The first diagram is a swimlane view of a whole order. The others zoom in on the three trickiest moments.

## 1. An order from start to finish (swimlanes)

Each vertical line is a **lane**: a person or a part of the system. Time runs from top to bottom, and the colours group the phases.

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant API as API
    actor D as Dispatcher
    participant W as Scheduling worker
    participant DB as PostgreSQL

    rect rgb(235, 245, 255)
    Note over C,DB: 1. Getting started
    C->>API: register and log in
    API->>DB: save customer and user (password stored hashed)
    API-->>C: token, valid for 60 minutes
    C->>API: add a delivery address
    API->>DB: save address
    end

    rect rgb(240, 255, 240)
    Note over C,DB: 2. Ordering
    C->>API: ask for a quote
    API-->>C: price breakdown (nothing is saved)
    C->>API: place the order
    Note right of API: checks address ownership, minimum order,<br/>tank capacity, takes the price in force now
    API->>DB: save order as Pending with its price snapshot
    API-->>C: 201 Created
    end

    rect rgb(255, 250, 235)
    Note over D,DB: 3. Dispatch prepares
    D->>API: create delivery windows (region, day, capacity)
    API->>DB: save windows
    D->>API: list orders, filter by status, region, date
    D->>API: confirm the order
    API->>DB: Pending to Confirmed
    end

    rect rgb(245, 240, 255)
    Note over W,DB: 4. Scheduling (every N minutes, on every API copy)
    W->>DB: load confirmed orders and open windows
    W->>W: DeliveryScheduler plans: oldest order first,<br/>earliest window in its region with room, tomorrow or later
    W->>DB: for each assignment: book the window and mark the order Scheduled
    Note right of DB: xmin check: a lost race is skipped<br/>and retried at the next run
    end

    rect rgb(255, 240, 240)
    Note over C,DB: 5. Delivery
    D->>API: out for delivery
    API->>DB: Scheduled to OutForDelivery
    D->>API: delivered litres
    Note right of API: within plus or minus 10 percent of ordered,<br/>final price recalculated from the snapshot
    API->>DB: Delivered, with the final price
    C->>API: view the order
    API-->>C: order with price and final price
    end

    opt The customer cancels (Pending, Confirmed or Scheduled)
        C->>API: cancel with a reason
        API->>DB: Cancelled, and the window gets its litres back (same transaction)
    end
```

## 2. Placing an order

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant UI as Blazor UI
    participant N as nginx
    participant API as OrdersController
    participant S as OrderService
    participant D as Domain (Order, PriceCalculator)
    participant DB as PostgreSQL

    C->>UI: pick product, address, litres, press Get price
    UI->>N: POST /api/quotes with Bearer token
    N->>API: forward, adds X-Forwarded-For
    API->>S: QuoteAsync(customerId from the token, request)
    S->>DB: load customer, address of this customer, product
    S->>D: EnsureValidQuantity, Calculate
    D-->>S: PriceBreakdown
    S-->>UI: 200 price (nothing saved)
    C->>UI: press Place order
    UI->>API: POST /api/orders
    API->>S: PlaceAsync
    S->>D: Order.Place(customer, address, product, litres)
    Note over D: checks the address belongs to the customer<br/>checks quantity, takes the price in force now,<br/>snapshots it on the order
    D-->>S: new Order (Pending)
    S->>DB: INSERT order and SaveChanges
    S-->>UI: 201 Created with Location header
    Note over UI: if the price differs from the quote,<br/>the UI says so before moving on
```

## 3. Two replicas schedule at the same time

Every API copy runs the worker, so two copies can plan the same order in the same moment. The database decides who wins.

```mermaid
sequenceDiagram
    autonumber
    participant A as Replica A (worker)
    participant B as Replica B (worker)
    participant DB as PostgreSQL

    A->>DB: read confirmed orders and open windows
    B->>DB: read confirmed orders and open windows
    Note over A,B: both compute the same plan:<br/>order 42 goes into window W
    A->>DB: load order 42 and window W (remember xmin)
    B->>DB: load order 42 and window W (remember xmin)
    Note over A: window.Book(600), order.Schedule(W)
    Note over B: window.Book(600), order.Schedule(W)
    A->>DB: UPDATE window ... WHERE xmin = old, UPDATE order ... WHERE xmin = old
    DB-->>A: 1 row each, commit
    B->>DB: UPDATE window ... WHERE xmin = old
    DB-->>B: 0 rows, the row changed
    Note over B: ConcurrencyConflictException<br/>DiscardChanges, skip this order, carry on with the next
    Note over A,DB: Result: booked once, order scheduled once, nothing overbooked
```

The same check protects the dispatcher screen: if a customer cancels while a dispatcher confirms the same order, the second save fails and the user sees a **409** "changed by someone else".

## 4. Log in and call the API

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant UI as Blazor UI
    participant API as API
    participant DB as PostgreSQL

    U->>UI: email and password
    UI->>API: POST /api/auth/login (rate limited)
    API->>DB: find user by lower-case email
    API->>API: verify the password hash
    alt wrong password or unknown email
        API-->>UI: 401 "Invalid email or password" (same answer for both)
    else correct
        API-->>UI: 200 JWT (sub, email, role, customer_id), expires in 60 minutes
        UI->>UI: keep the token in memory and sessionStorage
    end
    U->>UI: open My orders
    UI->>API: GET /api/orders with Authorization: Bearer token
    API->>API: validate signature, issuer, audience, expiry, role
    API->>DB: orders where customer_id = the id in the token
    API-->>UI: 200 page of orders
    Note over UI,API: when the token expires the API answers 401,<br/>the UI signs out and sends the user to the login page
```

## 5. Price changes

```mermaid
flowchart LR
    admin(["Admin: PUT /api/products/id/price"]) --> v{"valid from<br/>not in the past?"}
    v -- "no" --> e400["400"]
    v -- "yes" --> r["remove prices that are scheduled<br/>and start at or after this time"]
    r --> chk{"new time is after the latest<br/>price already in force?"}
    chk -- "no" --> e400b["400"]
    chk -- "yes" --> add["add the new price row"]
    add --> save[("price_history")]
    save --> use["orders placed after the valid-from time<br/>use the new price<br/>existing orders keep their own snapshot"]
```
