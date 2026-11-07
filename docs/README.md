# FuelFlow documentation

| Page | Read it to learn |
|---|---|
| [Architecture](architecture.md) | How the system is built: runtime picture, layers, the path of a request, configuration |
| [Domain model](domain-model.md) | Class diagrams (domain and layers) and the database tables |
| [Business rules](business-rules.md) | Every pricing, order, scheduling and validation rule, with worked examples |
| [Flows](flows.md) | Swimlane of a full order, plus sequence diagrams for placing an order, concurrent scheduling and login |
| [API reference](api-reference.md) | Endpoints, roles, examples and the error table |
| [Design decisions](design-decisions.md) | Why things are the way they are, trade-offs, known shortcuts and limitations |
| [Testing and delivery](testing-and-delivery.md) | Test strategy, CI, CD, Docker images |

**Suggested reading order:** Architecture, Business rules, Flows, then Design decisions. Domain model and API reference are for looking things up.

The diagrams are written in [Mermaid](https://mermaid.js.org/), so they are plain text, live in version control, and are drawn by GitHub when you open the pages.
