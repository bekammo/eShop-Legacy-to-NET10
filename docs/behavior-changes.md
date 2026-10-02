# Behavior-change register

This register lists every deliberate difference between the new API and the legacy app. The policy is [ADR-0002](../DECISIONS.md#adr-0002-wire-contract-policy):

- The migrated endpoints are drop-in compatible with the golden exchanges in [legacy/contract](legacy/contract).
- A difference is allowed only when security or the platform forces it.

An entry is added in the same commit as the change it describes, and that commit also adds a test that asserts the new behaviour.

The register has two parts:

- **Contract deltas:** differences from a golden exchange of a migrated endpoint (`/api/brands`, `/api/files`, `/items/{id}/pic`).
- **Business-rule changes:** differences from legacy behaviour behind re-exposed capabilities (items, types). The legacy behaviour is recorded in [legacy/evidence](legacy/evidence). These endpoints had no HTTP contract before, but their rules come from the legacy UI, so each departure from those rules is recorded too.

## Entry format

```markdown
### BC-NNN: <short title>

- **Kind:** Contract delta | Business-rule change
- **Forced by:** Security | Platform
- **Stage / commit:** <plan sub-task>, <commit subject>
- **Legacy behaviour:** what the legacy app does, with a link to the golden exchange or evidence file.
- **New behaviour:** what the new API does instead.
- **Client impact:** who notices, and what they have to change (or "none").
- **Test:** the test that asserts the new behaviour.
- **Decision:** the ADR that justifies it.
```

## Entries

### BC-001: Error responses are problem details

- **Kind:** Contract delta
- **Forced by:** Platform
- **Stage / commit:** 7.1, "Add Minimal API conventions, problem details and OpenAPI (Stage 7.1)"
- **Legacy behaviour:** The error body came from the framework that failed. Web API 2 wrote `{"Message", "MessageDetail"}` ([`brands-get-by-id--non-integer`](legacy/contract/brands-get-by-id.json)), or XML for an XML `Accept` header (`brands-get-by-id--non-integer-xml`). The 404s that the code returned had no body (`brands-get-by-id--not-found`). IIS and ASP.NET answered the rest with HTML pages ([`api-root`](legacy/contract/api-root.json), `brands-get-by-id--dot-in-segment`). Local clients got an exception's message, type and stack trace.
- **New behaviour:** Every error is a problem details object (RFC 9457), `application/problem+json`, whatever the `Accept` header says. It has `type`, `title`, `status` and `traceId`, plus `detail` when the endpoint gives one and `errors` for a validation failure. No response shows an exception, in any environment. The status codes do not change.
- **Client impact:** A client that reads `Message` or `MessageDetail` reads `title`, `detail` or `errors` instead. A client that expects an empty 404 body gets a body. A client that looks only at the status sees no change.
- **Test:** `HttpConventionsTests` (a problem from each source of errors, for each `Accept` header of the golden exchanges) and `ErrorHandlingTests` (the app that `Program.cs` builds). From Stage 7.2 the golden exchanges check the statuses.
- **Decision:** [ADR-0021](../DECISIONS.md#adr-0021-error-contract-problem-details), and [ADR-0002](../DECISIONS.md#adr-0002-wire-contract-policy), decision 4.

### BC-002: No XML

- **Kind:** Contract delta
- **Forced by:** Platform
- **Stage / commit:** 7.2, "Port /api/brands drop-in and replay the golden exchanges (Stage 7.2)"
- **Legacy behaviour:** Web API 2 answered `Accept: application/xml`, `text/xml` and a browser's `Accept` header, which lists `application/xml`, with XML from `DataContractSerializer` ([`brands-list.json`](legacy/contract/brands-list.json): `brands-get-all--accept-xml`, `brands-get-all--accept-text-xml`, `brands-get-all--accept-browser`; [`brands-get-by-id.json`](legacy/contract/brands-get-by-id.json): `brands-get-by-id--xml`, and the error of `brands-get-by-id--non-integer-xml`).
- **New behaviour:** JSON, whatever the `Accept` header says, with the same status: the list and the brand as for `Accept: application/json`, and the 400 for a non-integer ID as a problem ([BC-001](#bc-001-error-responses-are-problem-details)).
- **Client impact:** A client that reads XML has to read JSON. No such client is known ([ADR-0003](../DECISIONS.md#adr-0003-non-goals) names the evidence that would bring XML back). A browser shows the brands as JSON.
- **Test:** `LegacyContractTests` replays each of the five exchanges, in database mode and in mock mode, and compares the response with that of the matching JSON exchange.
- **Decision:** [ADR-0020](../DECISIONS.md#adr-0020-minimal-api-endpoints-and-the-openapi-document) (Minimal APIs, no content negotiation), and [ADR-0003](../DECISIONS.md#adr-0003-non-goals).

### BC-003: OPTIONS is a 405

- **Kind:** Contract delta
- **Forced by:** Platform
- **Stage / commit:** 7.2, "Port /api/brands drop-in and replay the golden exchanges (Stage 7.2)"
- **Legacy behaviour:** IIS answered `OPTIONS /api/brands` itself, before the request reached the app: 200, with `Allow: OPTIONS, TRACE, GET, HEAD, POST`, which names four methods that the route does not accept ([`brands-other-verbs.json`](legacy/contract/brands-other-verbs.json): `brands-options`).
- **New behaviour:** The request reaches the API, whose routing answers it as any other method that the route does not have: 405, with `Allow: GET`, as for `POST`. No CORS is configured, so no preflight is answered either.
- **Client impact:** A client that asks which methods the route has gets a 405 with the right answer in `Allow`. Browsers send `OPTIONS` only as a CORS preflight, which the API does not allow ([ADR-0004](../DECISIONS.md#adr-0004-write-endpoints-stay-anonymous-until-after-cutover)).
- **Test:** `LegacyContractTests` replays `brands-options` and compares the response with that of `brands-post`: 405, with the methods of `Allow` compared as a set.
- **Decision:** [ADR-0003](../DECISIONS.md#adr-0003-non-goals) (IIS host behaviour is not reproduced).

### BC-004: A dotted brand ID is a 400

- **Kind:** Contract delta
- **Forced by:** Platform
- **Stage / commit:** 7.2, "Port /api/brands drop-in and replay the golden exchanges (Stage 7.2)"
- **Legacy behaviour:** `GET /api/brands/1.5` never reached the app. The IIS static file handler took `.5` for a file extension and answered 404 with an HTML page ([`brands-get-by-id.json`](legacy/contract/brands-get-by-id.json): `brands-get-by-id--dot-in-segment`).
- **New behaviour:** The request reaches the API, which cannot bind `1.5` to an integer ID: 400, as for `abc` (`brands-get-by-id--non-integer`).
- **Client impact:** 400 instead of 404, for an ID that was never valid.
- **Test:** `LegacyContractTests` replays `brands-get-by-id--dot-in-segment` and compares the response with that of `brands-get-by-id--non-integer`.
- **Decision:** [ADR-0003](../DECISIONS.md#adr-0003-non-goals) (IIS host behaviour is not reproduced), and [ADR-0020](../DECISIONS.md#adr-0020-minimal-api-endpoints-and-the-openapi-document), decision 2.

### BC-005: The query string does not select a brand

- **Kind:** Contract delta
- **Forced by:** Platform, in the sense of [ADR-0002](../DECISIONS.md#adr-0002-wire-contract-policy), decision 5: the behaviour came from Web API 2, not from the app's code, and the new platform does not have it. The API could reproduce it, so this is a decision, not a necessity.
- **Stage / commit:** 7.2, "Port /api/brands drop-in and replay the golden exchanges (Stage 7.2)"
- **Legacy behaviour:** `GET /api/brands?id=2` returned brand 2 ([`brands-get-by-id.json`](legacy/contract/brands-get-by-id.json): `brands-get-by-id--query-string`). The code did not ask for this: Web API 2's action selector also matches action parameters to query-string values, so the request selected `Get(int id)` instead of `Get()`.
- **New behaviour:** Routing selects an endpoint by its path and method only, so `GET /api/brands?id=2` is `GET /api/brands`: 200, with every brand. It is not reproduced, for three reasons:
  - It is accidental. Nothing in the legacy app asks for it, and no view, script or controller of the legacy app calls `/api/brands` at all, with or without the query string.
  - Reproducing it would give the list an `id` parameter that turns its response from an array into one brand, or into a 404. The list would then have two shapes. The OpenAPI generator shows one response type per status, so the document would show only one of them.
  - The same answer has a route of its own, `GET /api/brands/{id}`, which the new API keeps.
- **Client impact:** A client that used `?id=` gets the list, an array, instead of one brand, and has to use `GET /api/brands/{id}`. An unknown ID in the query string, such as `?id=6`, gets the list with a 200 instead of a 404, which the legacy app would presumably have answered; that request was not captured.
- **Test:** `LegacyContractTests` replays `brands-get-by-id--query-string` and compares the response with that of `brands-get-all--accept-json`.
- **Decision:** [ADR-0002](../DECISIONS.md#adr-0002-wire-contract-policy), decision 5.

## Known upcoming deltas

The Stage 1 audit and characterization already show where the new API will differ. The list below records them so that none is forgotten. Each becomes an entry, with a number and a test, in the commit that implements it. The list is not an entry itself, and it does not pre-empt the decisions of later stages.

| Legacy behaviour (evidence) | Expected change | Forced by | Stage |
|---|---|---|---|
| `GET /api/files` returns 200 with a BinaryFormatter stream labelled `text/html` ([`files.json`](legacy/contract/files.json): `files-get`) | `410 Gone` with a ProblemDetails body that points to `/api/brands`. | Security | 7.3 |
| A missing picture file returns 500 ([`pic-missing-file`](legacy/evidence/pic-missing-file.json)) | 404. | Security | 7.4 |
| `PictureFileName` can point outside `Pics` ([`pic-path-traversal-relative`](legacy/evidence/pic-path-traversal-relative.json), [`-absolute`](legacy/evidence/pic-path-traversal-absolute.json)) | Only files inside the pictures root are served. | Security | 7.4 |
| An upper-case extension is served as `application/octet-stream` ([`pic-extension-case`](legacy/evidence/pic-extension-case.json)) | The content type comes from a case-insensitive lookup. | Security | 7.4 |
| `Range` is ignored: 200 with the full body ([`pictures.json`](legacy/contract/pictures.json): `pic-get--range`) | Range requests are honoured (206). | Platform | 7.4 |
| `HEAD /items/1/pic` returns 404 ([`pictures.json`](legacy/contract/pictures.json): `pic-head`) | To be decided in 7.4. | Platform | 7.4 |
| Paging accepts `pageSize=0`, negative values and unbounded sizes, and the bad ones give 500 ([`catalog-reads`](legacy/evidence/catalog-reads.json)) | `pageSize` must be 1–100 and `pageIndex` must be 0 or more; anything else is a 400. | Security (unbounded reads) | 7.5 |
| `PictureFileName` and `Id` are client-writable on create and edit; edit overwrites fields the client did not send ([`edit-overwrites-unposted-fields`](legacy/evidence/edit-overwrites-unposted-fields.json)) | `PictureFileName` is not client-writable, and updates apply explicit fields. | Security | 7.6, 7.7 |
| `Range(0, 1000000)` rounds the price before comparing, so 1000000.50 is accepted ([`create-item-validation`](legacy/evidence/create-item-validation.json)) | To be decided in 7.6: keep it or apply the range exactly. | Security (input validation) | 7.6 |
| A name over 50 characters, an unknown brand or an unknown type gives 500 ([`create-item-validation`](legacy/evidence/create-item-validation.json)) | To be decided in 7.6 (a 400 is expected). | Security | 7.6 |
| Editing or deleting an unknown item gives 500 ([`unknown-item-writes`](legacy/evidence/unknown-item-writes.json)) | To be decided in 7.7 (a 404 is expected). | Security | 7.7 |

Some response differences are not deltas, because the [comparison rules](legacy/README.md#comparison-rules) treat them as informational:

- the `Server`, `X-Powered-By`, `X-AspNet-Version` and `X-AspNetMvc-Version` headers
- the Web API no-cache headers
- the MVC session cookie
