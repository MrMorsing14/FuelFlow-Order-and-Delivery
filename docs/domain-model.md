# Domain model

Three views of the same system: the **domain classes**, the **layer classes** (who talks to whom), and the **database tables**.

## 1. Domain classes

```mermaid
classDiagram
    direction LR

    class User {
        +Guid Id
        +string Email
        +string PasswordHash
        +UserRole Role
        +Guid? CustomerId
        +Create(...)$ User
    }
    class Customer {
        +Guid Id
        +CustomerType Type
        +string Phone
        +string Email
        +string? FullName
        +string? CompanyName
        +CvrNumber? Cvr
        +CreatePrivate(...)$ Customer
        +CreateBusiness(...)$ Customer
        +Update(...)
    }
    class DeliveryAddress {
        +Guid Id
        +Guid CustomerId
        +string Street
        +PostalCode PostalCode
        +string City
        +int TankCapacityLitres
        +string? AccessNotes
        +Create(...)$ DeliveryAddress
        +Update(...)
    }
    class Product {
        +Guid Id
        +ProductType Type
        +PriceAt(time) decimal
        +ChangePrice(price, validFrom, now)
    }
    class PriceHistory {
        +Guid Id
        +decimal PricePerLitreExVat
        +DateTimeOffset ValidFrom
    }
    class Order {
        +Guid Id
        +int OrderedLitres
        +OrderStatus Status
        +PriceBreakdown Price
        +PriceBreakdown? FinalPrice
        +Guid? DeliveryWindowId
        +int? DeliveredLitres
        +string? CancellationReason
        +Place(...)$ Order
        +Confirm(now)
        +Schedule(windowId, now)
        +StartDelivery(now)
        +Deliver(litres, now)
        +Cancel(reason, now)
    }
    class PriceBreakdown {
        <<record>>
        +int Litres
        +decimal UnitPriceExVat
        +decimal NetAmount
        +decimal DiscountRate
        +decimal DiscountAmount
        +decimal SmallOrderFee
        +decimal TotalExVat
        +decimal VatRate
        +decimal VatAmount
        +decimal TotalIncVat
        +bool DisplayIncVat
    }
    class DeliveryWindow {
        +Guid Id
        +DateOnly Date
        +string Region
        +int CapacityLitres
        +int BookedLitres
        +int RemainingLitres
        +Book(litres)
        +Release(litres)
    }
    class PriceCalculator {
        +EnsureValidQuantity(litres, tankCapacity)
        +Calculate(unitPrice, litres, customerType) PriceBreakdown
        +AddVat(amount) decimal
        +Recalculate(snapshot, litres)$ PriceBreakdown
    }
    class DeliveryScheduler {
        <<static, pure>>
        +Plan(orders, windows, now)$ Assignment[]
    }
    class RegionMap {
        +RegionOf(postalCode) string?
        +Find(name) RegionRange?
    }
    class CvrNumber {
        <<value object>>
        +Create(text)$ CvrNumber
    }
    class PostalCode {
        <<value object>>
        +Create(text)$ PostalCode
    }

    Customer "1" --> "0..*" DeliveryAddress : owns
    User "0..1" --> "1" Customer : is the login of
    Product "1" *-- "1..*" PriceHistory : price history
    Customer "1" --> "0..*" Order : places
    DeliveryAddress "1" --> "0..*" Order : delivered to
    Product "1" --> "0..*" Order : ordered
    DeliveryWindow "0..1" --> "0..*" Order : holds
    Order *-- PriceBreakdown : price snapshot
    Order ..> PriceCalculator : Place and Deliver use it
    DeliveryScheduler ..> DeliveryWindow : reads
    DeliveryScheduler ..> RegionMap : region per order is decided before planning
    Customer o-- CvrNumber
    DeliveryAddress o-- PostalCode
```

Reading notes:

- `Order` and `DeliveryWindow` are the two classes with real behaviour. Everything that changes an order goes through its methods, never by setting `Status` directly.
- `PriceBreakdown` is an immutable record stored on the order at order time. It never changes. The final price after delivery is a second copy (`FinalPrice`).
- Enums: `UserRole` (Customer, Dispatcher, Admin), `CustomerType` (Private, Business), `ProductType` (HeatingOil, Diesel, Hvo), `OrderStatus` (Pending, Confirmed, Scheduled, OutForDelivery, Delivered, Cancelled).

## 2. Layer classes: use cases, ports and adapters

```mermaid
classDiagram
    direction TB

    class AuthService
    class CustomerService
    class ProductService
    class OrderService
    class DispatchService
    class SchedulingService

    class IUserRepository { <<interface>> }
    class ICustomerRepository { <<interface>> }
    class IAddressRepository { <<interface>> }
    class IProductRepository { <<interface>> }
    class IOrderRepository { <<interface>> }
    class IDeliveryWindowRepository { <<interface>> }
    class IUnitOfWork { <<interface>> }
    class IPasswordService { <<interface>> }
    class ITokenService { <<interface>> }

    class FuelFlowDbContext {
        SaveChangesAsync()
        DiscardChanges()
    }
    class UserRepository
    class CustomerRepository
    class AddressRepository
    class ProductRepository
    class OrderRepository
    class DeliveryWindowRepository
    class PasswordService
    class JwtTokenService

    AuthService --> IUserRepository
    AuthService --> ICustomerRepository
    AuthService --> IPasswordService
    AuthService --> ITokenService
    CustomerService --> ICustomerRepository
    CustomerService --> IAddressRepository
    ProductService --> IProductRepository
    OrderService --> IOrderRepository
    OrderService --> IAddressRepository
    OrderService --> IProductRepository
    OrderService --> IDeliveryWindowRepository
    DispatchService --> IOrderRepository
    DispatchService --> IDeliveryWindowRepository
    SchedulingService --> IOrderRepository
    SchedulingService --> IDeliveryWindowRepository

    AuthService --> IUnitOfWork
    OrderService --> IUnitOfWork
    DispatchService --> IUnitOfWork
    SchedulingService --> IUnitOfWork

    UserRepository ..|> IUserRepository
    CustomerRepository ..|> ICustomerRepository
    AddressRepository ..|> IAddressRepository
    ProductRepository ..|> IProductRepository
    OrderRepository ..|> IOrderRepository
    DeliveryWindowRepository ..|> IDeliveryWindowRepository
    FuelFlowDbContext ..|> IUnitOfWork
    PasswordService ..|> IPasswordService
    JwtTokenService ..|> ITokenService

    UserRepository --> FuelFlowDbContext
    OrderRepository --> FuelFlowDbContext
```

The top row is the Application layer, the interfaces are the ports it defines, and the bottom rows are the Infrastructure adapters. (Only some adapters are drawn pointing at the DbContext, to keep the picture readable: all repositories use it.)

`FuelFlowDbContext` plays the "unit of work" role: repositories only *add* or *find*; `SaveChangesAsync` commits everything in one transaction. It also translates database errors into application exceptions (concurrency conflict, duplicate email, duplicate window).

## 3. Database tables

```mermaid
erDiagram
    users {
        uuid id PK
        text email UK "lower case"
        text password_hash
        text role
        uuid customer_id FK "only for Customer users"
    }
    customers {
        uuid id PK
        text type
        text phone
        text email
        text full_name "private"
        text company_name "business"
        text cvr "business, 8 digits"
    }
    delivery_addresses {
        uuid id PK
        uuid customer_id FK
        text street
        text postal_code "4 digits"
        text city
        int tank_capacity_litres
        text access_notes
    }
    products {
        uuid id PK
        text type UK
    }
    price_history {
        uuid id PK
        uuid product_id FK
        numeric price_per_litre_ex_vat "18,4"
        timestamptz valid_from "unique with product"
    }
    orders {
        uuid id PK
        uuid customer_id FK
        uuid delivery_address_id FK
        uuid product_id FK
        uuid delivery_window_id FK "null until scheduled"
        int ordered_litres
        text status
        columns price_snapshot "Price_* columns"
        columns final_price "FinalPrice_* columns, null until delivered"
        int delivered_litres
        timestamptz created_at "plus one timestamp per status change"
        xid xmin "concurrency token"
    }
    delivery_windows {
        uuid id PK
        date date "unique with region"
        text region
        int capacity_litres
        int booked_litres
        xid xmin "concurrency token"
    }

    customers ||--o{ delivery_addresses : has
    customers |o--o| users : "login for"
    customers ||--o{ orders : places
    delivery_addresses ||--o{ orders : "delivered to"
    products ||--|{ price_history : "priced by"
    products ||--o{ orders : ordered
    delivery_windows |o--o{ orders : holds
```

Choices behind the tables:

- **Price snapshot as columns on `orders`.** EF Core "complex types" store the `PriceBreakdown` as `Price_NetAmount`, `Price_VatAmount` and so on, instead of a separate table. One row read gives the whole order.
- **`price_history` is append-only** (apart from replacing prices that are scheduled but not yet in force). Old orders do not depend on it, because they carry their own snapshot.
- **Foreign keys from orders are `RESTRICT`.** A customer, address, product or window with orders cannot be deleted by accident.
- **`xmin` is a system column in PostgreSQL** that changes on every update of a row. EF Core adds `WHERE xmin = @old` to updates, so a second writer on the same row gets "0 rows updated" and the app returns 409.
- Region is **not** stored on addresses. It is worked out from the postal code and the configured ranges, so changing the region setup needs no data migration. Windows store the region name, so renaming a region in configuration also means updating existing windows.
