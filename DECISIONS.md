# Architecture decision records

These are the decisions behind the .NET 10 migration described in [MIGRATION_PLAN.md](MIGRATION_PLAN.md). Each ADR is added in the same commit as the change it describes.

- ADRs are numbered in order, and numbers are never reused.
- An accepted ADR is not rewritten. When a decision changes, a new ADR supersedes it, and the old one stays in place with its status updated.
- Facts cited in an ADR link to their evidence: the audit ([docs/legacy-audit.md](docs/legacy-audit.md)), the characterization data ([docs/legacy/](docs/legacy/README.md)) or the code.

## Index

| ADR | Title | Status | Plan stage |
|---|---|---|---|
| [ADR-0001](#adr-0001-migration-scope) | Migration scope | Accepted | 1.3 |
| [ADR-0002](#adr-0002-wire-contract-policy) | Wire-contract policy | Accepted | 1.3 |
| [ADR-0003](#adr-0003-non-goals) | Non-goals | Accepted | 1.3 |
| [ADR-0004](#adr-0004-write-endpoints-stay-anonymous-until-after-cutover) | Write endpoints stay anonymous until after cutover | Accepted | 1.3 |

## Template

```markdown
## ADR-NNNN: <decision as a short noun phrase>

- **Status:** Proposed | Accepted | Superseded by ADR-NNNN
- **Date:** YYYY-MM-DD
- **Plan stage:** <sub-task number>

### Context
The forces behind the decision, with links to the evidence.

### Decision
What we do, stated so that it can be checked.

### Alternatives considered
Each option that lost, and why.

### Consequences
What gets easier, what gets harder, the follow-up work, and the risks we accept.
```

---

## ADR-0001: Migration scope

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

The legacy app has two HTTP surfaces ([audit: endpoint inventory](docs/legacy-audit.md#4-endpoint-inventory)):

- **Web API 2**, which is small:
  - `BrandsController`: `GET /api/brands`, `GET /api/brands/{id}`, and a `DELETE /api/brands/{id}` that does nothing.
  - `FilesController`: `GET /api/files`, which returns a BinaryFormatter stream.
- **MVC 5 with Razor views**, which holds everything else:
  - item list, details, create, edit and delete, which exist only as HTML forms
  - brand and type dropdowns
  - the picture route `GET /items/{catalogItemId:int}/pic`

The characterization run ([docs/legacy](docs/legacy/README.md)) shows how each of these behaves today. It also shows that several capabilities are reachable only through anti-forgery-protected HTML forms.

### Decision

The target is one ASP.NET Core Web API on .NET 10, covering the full catalog:

| Legacy capability | In the new API |
|---|---|
| `GET /api/brands`, `GET /api/brands/{id}`, `DELETE /api/brands/{id}` | Migrated drop-in ([ADR-0002](#adr-0002-wire-contract-policy)). The delete stays a no-op. |
| `GET /items/{catalogItemId:int}/pic` | Migrated with the same route and route name (`GetPicRouteTemplate`). |
| Item list, details, create, edit, delete (Razor only) | Re-exposed as REST: `GET /api/items` (paged), `GET /api/items/{id}`, `POST /api/items`, `PUT /api/items/{id}`, `DELETE /api/items/{id}`. |
| Type dropdown (Razor only) | Re-exposed as `GET /api/types`. |
| `GET /api/files` | Retired. It answers `410 Gone` and points to `/api/brands`. |
| Razor UI, bundles, client libraries, `CatalogController2` | Dropped. `CatalogController2` is unreachable today. |
| `eShopLegacy.Utilities` | Removed at cutover (Stage 11). Its only consumer is `/api/files`. |

Runtime features:

- **Kept:**
  - in-memory mode (`UseMockData`, which becomes `Catalog:UseMockData`)
  - a size-rolled log file with the log4net limits: 10 MB per file, 5 backups plus the active file
- **Dropped:**
  - the CSV customization seed (`UseCustomizationData`, `Setup/*.csv`, `Setup/CatalogItems.zip`)
  - Application Insights: no instrumentation key exists anywhere in the repository, so it sends nothing today
  - session state: only the Razor layout reads it

The legacy projects stay in the repository, unchanged, as the reference implementation until Stage 11. The `legacy-final` tag marks the rollback point.

### Alternatives considered

- **Port only the Web API 2 surface.** This loses every item capability, because they exist only behind Razor.
- **Port the UI as well (Razor Pages or MVC on ASP.NET Core).** The migration is API-focused. A UI can be built later on top of the new endpoints without touching them.

### Consequences

- The re-exposed endpoints have no legacy wire contract to match. They follow the conventions of the migrated endpoints ([ADR-0002](#adr-0002-wire-contract-policy)) and the business rules recorded in [`docs/legacy/evidence`](docs/legacy/README.md#evidence). Their shape is designed in Stages 7.5–7.7.
- UI-only mechanics disappear with the UI: anti-forgery tokens, redirects after posts, HTML validation messages, and the session cookie.
- Dropping `/api/files` removes the last BinaryFormatter use, which is what allows `eShopLegacy.Utilities` to go.

---

## ADR-0002: Wire-contract policy

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

Clients of the Web API 2 routes should not have to change when the backend moves to .NET 10. The characterization ([contract coverage](docs/legacy/README.md#contract-coverage)) shows what "the same" means in practice:

- JSON property names are PascalCase (the Newtonsoft default in Web API 2).
- Content negotiation returns XML for `application/xml`, `text/xml` and typical browser `Accept` headers.
- Error bodies are framework output: Web API `{"Message", "MessageDetail"}` or empty for the API, and IIS or ASP.NET HTML pages elsewhere.
- Some responses come from IIS, not the app: `OPTIONS`, and a path segment that contains a dot.
- Accidental features exist, such as `GET /api/brands?id=2` returning one brand.

### Decision

1. **Drop-in compatibility for migrated endpoints.** Routes (case-insensitive, trailing slash tolerated), verbs, status codes, JSON property names, JSON shapes and ordering stay the same. `DELETE /api/brands/{id}` stays a no-op: 200 for an existing brand, 404 for an unknown one.
2. **PascalCase JSON on every endpoint**, including the re-exposed ones, so that the API is consistent.
3. **Only forced deltas.** A behaviour may differ from the golden exchanges only when one of these forces it:
   - **Security.** Examples: retiring `/api/files`, and the picture-endpoint fixes.
   - **The platform.** Examples: no XML content negotiation, and IIS-level behaviour that Kestrel does not have.

   Every delta gets an entry in [docs/behavior-changes.md](docs/behavior-changes.md) in the commit that introduces it. The entry names the golden exchange it departs from, and a test asserts the new behaviour.
4. **Status codes are contract; error payloads are not.** The legacy error bodies are framework output. The new error format is chosen in Stage 7.1 and recorded as a delta.
5. **Accidental behaviours are decided one at a time.** Behaviour that comes from the Web API 2 action selector or IIS rather than from the code (for example, the `?id=` query-string binding) is decided in the stage that ports the endpoint. If it is not reproduced, it is recorded as a delta.
6. **No idiomatic changes during the migration.** camelCase, real brand deletion, `201`/`204` semantics and similar improvements are deferred to a future v2 of the API.
7. **Verification.** The golden exchanges in [docs/legacy/contract](docs/legacy/contract) are replayed against the new API using the [comparison rules](docs/legacy/README.md#comparison-rules).

### Alternatives considered

- **Idiomatic now** (camelCase, ProblemDetails everywhere, proper REST semantics). This breaks every existing client during a platform change, and mixes two kinds of risk in one release.
- **Byte-for-byte parity**, headers and XML included. This would need controllers with XML formatters and emulation of IIS behaviour. The headers it would preserve (`Server`, `X-Powered-By`, `X-AspNet-Version`, `X-AspNetMvc-Version`) disclose the stack, so keeping them has negative value.

### Consequences

- Some legacy oddities are kept on purpose: the no-op delete, and PascalCase on new endpoints.
- Every departure is visible in one register, and each one has a test.
- The error-format change is a known, recorded delta, not an accident discovered by a client.
- v2 has a ready list of idiomatic changes to start from.

---

## ADR-0003: Non-goals

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

A migration with an open-ended scope does not finish. Several legacy features are unused, UI-only or unsafe, and some improvements are tempting but belong in a separate change. Each exclusion below is explicit, and each one names what would bring it back, so that it can be revisited on evidence rather than by default.

### Decision

These are out of scope for the migration:

| Non-goal | Why it is out | What would bring it back |
|---|---|---|
| Porting the Razor/MVC UI | The migration is API-focused ([ADR-0001](#adr-0001-migration-scope)). | A product need for a server-rendered admin UI. It would be built as a separate client of the API. |
| XML content negotiation | Minimal APIs return JSON only, and no known client needs XML. | Evidence of a real XML consumer, such as access logs showing `Accept: application/xml` traffic. |
| Any BinaryFormatter payload | BinaryFormatter is unsafe and removed from .NET 9+. `/api/files` is retired. | Nothing brings BinaryFormatter back. A need for a bulk export would be met with a new JSON or CSV endpoint. |
| The CSV customization seed (`UseCustomizationData`) | It is off in the committed config. When on, it deletes and re-extracts the `Pics` folder at startup. | A requirement to seed a different catalog per deployment. It would be built as an import tool or a seeding option with its own tests. |
| Application Insights | No instrumentation key exists, so the legacy telemetry is inert. | An operational requirement for APM. It would use OpenTelemetry with an exporter, not the classic SDK. |
| Session state | Only the Razor layout reads it: it shows the machine name and the session start time. | None expected. The API is stateless. |
| The log4net line format | Serilog replaces log4net (Stage 6). Only the file location and rolling limits are kept. | A downstream tool that parses the old format. |
| Reproducing IIS host behaviour (`OPTIONS` answered by IIS, dotted segments served by the static file handler, stack-disclosure headers) | It is host behaviour, not app behaviour, and the new host is Kestrel. | A client that demonstrably depends on one of these. |
| Idiomatic contract changes (camelCase, real brand delete, `201`/`204`) | Deferred to v2 ([ADR-0002](#adr-0002-wire-contract-policy)). | A decision to version the API. |
| A YARP or `SystemWebAdapters` strangler setup | About ten endpoints do not justify the proxy machinery. The Stage 2.1 ADR covers the migration strategy. | A larger surface, or a need to migrate endpoint by endpoint in production. |

### Alternatives considered

- **Leave non-goals implicit.** Excluded features then come back as surprise work in later stages, or are silently lost with no record.

### Consequences

- The dropped features are documented, with evidence, in the audit and here.
- Each row names a trigger, so the list can be revisited when circumstances change.

---

## ADR-0004: Write endpoints stay anonymous until after cutover

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

The legacy app has no authentication or authorization anywhere ([audit: defects and risks](docs/legacy-audit.md#7-defects-and-risks)):

- Anyone who can reach the MVC forms can create, edit and delete items.
- The anti-forgery tokens protect only against cross-site request forgery. They do not identify anyone.
- The Web API `DELETE /api/brands/{id}` needs no identity, and it does nothing anyway.

The new API exposes the item writes directly (`POST`, `PUT` and `DELETE` on `/api/items`). Adding authentication during the migration would change the contract of the migrated endpoints and mix a security feature into a platform change.

### Decision

- The migration keeps the legacy posture: no authentication on any endpoint. This is an **accepted risk** until Stage 12.
- Stage 12, after cutover, adds JWT bearer authentication with a `catalog:write` scope policy on item `POST`, `PUT` and `DELETE`. Local tokens come from `dotnet user-jwts`. Reads and `/api/brands` stay anonymous.
- Until Stage 12, the new API must not be deployed where untrusted clients can reach it.

### Alternatives considered

- **Add authentication in Stage 7 with the write endpoints.** This mixes two changes and makes the contract comparison against legacy harder. It also needs a token story for every test from the start.
- **Keep the writes out of the API until Stage 12.** Stages 7–11 would then be unable to prove that the re-exposed capabilities work.

### Consequences

- Until Stage 12, anyone who can reach the new API can change the catalog, as with the legacy UI today.
- Browser-based cross-site writes remain hard: the new API uses no cookies (no ambient credentials to abuse), it accepts JSON bodies (which need a CORS preflight), and it enables no CORS.
- Stage 12 has a defined scope, and its tests (401, 403, 2xx) are planned.
