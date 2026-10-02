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

### BC-006: `GET /api/files` is gone

- **Kind:** Contract delta
- **Forced by:** Security
- **Stage / commit:** 7.3, "Retire GET /api/files with 410 Gone (Stage 7.3)"
- **Legacy behaviour:** 200, with every brand in a 721-byte BinaryFormatter payload labelled `text/html`, whatever the `Accept` header, at `/api/files` and at `/api/files/{id}` for any ID ([`files.json`](legacy/contract/files.json): `files-get`, `files-get--accept-json`, `files-get-by-id`).
- **New behaviour:** `410 Gone`, with a problem whose `detail` says that the endpoint has been retired and points to `GET /api/brands`. The route is not in the OpenAPI document.
- **Client impact:** A client that read the payload has to call `GET /api/brands` and read JSON, with the same IDs and names: `[{"Id":1,"Brand":"Azure"}, ...]`. A client that deserialized the payload no longer runs BinaryFormatter on it, which was the risk (audit D4).
- **Test:** `LegacyContractTests` replays the three exchanges, in database mode and in mock mode, and expects 410. `FileEndpointsTests` checks the problem.
- **Decision:** [ADR-0022](../DECISIONS.md#adr-0022-get-apifiles-retired-with-410-gone), and [ADR-0001](../DECISIONS.md#adr-0001-migration-scope).

### BC-007: A missing picture file is a 404

- **Kind:** Contract delta
- **Forced by:** Security
- **Stage / commit:** 7.4, "Serve the item pictures from a confined folder (Stage 7.4)"
- **Legacy behaviour:** An item whose picture file did not exist got a 500, an error page with the `FileNotFoundException` and its path ([`pic-missing-file`](legacy/evidence/pic-missing-file.json), audit D7).
- **New behaviour:** 404, a problem without the path. The API logs `PictureNotFound` at Warning, with the item's ID and the picture's name.
- **Client impact:** 404 instead of 500 for such an item.
- **Test:** `PictureEndpointsTests.Missing_picture_file_is_a_404_and_a_warning`, `CatalogPicturesTests.Name_without_a_file_finds_nothing`.
- **Decision:** [ADR-0023](../DECISIONS.md#adr-0023-security-fixes-made-during-the-migration).

### BC-008: Picture names outside the pictures folder are not served

- **Kind:** Contract delta
- **Forced by:** Security
- **Stage / commit:** 7.4, "Serve the item pictures from a confined folder (Stage 7.4)"
- **Legacy behaviour:** The endpoint served whatever file the item's picture name pointed at: `..\Global.asax` served the app's `Global.asax`, and `C:\Windows\win.ini` served that file ([`pic-path-traversal-relative`](legacy/evidence/pic-path-traversal-relative.json), [`pic-path-traversal-absolute`](legacy/evidence/pic-path-traversal-absolute.json), audit D1).
- **New behaviour:** Only files inside the pictures folder are served. A name that leaves the folder, or a rooted one, gets a 404 and the `PictureNotFound` warning. Clients can no longer write a picture name at all ([ADR-0015](../DECISIONS.md#adr-0015-async-first-catalog-service)), but a database adopted from the legacy app may hold such names.
- **Client impact:** None for the pictures of the folder.
- **Test:** `PictureEndpointsTests.Picture_name_that_leaves_the_folder_is_a_404`, `CatalogPicturesTests.Name_that_leaves_the_folder_finds_nothing` and `Rooted_name_finds_nothing`.
- **Decision:** [ADR-0023](../DECISIONS.md#adr-0023-security-fixes-made-during-the-migration).

### BC-009: The content type of a picture ignores the case of its extension

- **Kind:** Contract delta
- **Forced by:** Security
- **Stage / commit:** 7.4, "Serve the item pictures from a confined folder (Stage 7.4)"
- **Legacy behaviour:** A case-sensitive switch on the extension chose the content type, so `1.PNG` was sent as `application/octet-stream` ([`pic-extension-case`](legacy/evidence/pic-extension-case.json), audit D8). It sent `.wmf` as `image/wmf` and `.jp2` as `image/jp2`.
- **New behaviour:** ASP.NET Core's table of extensions gives the type, whatever the case: `1.PNG` is `image/png`. It sends `.wmf` as `application/x-msmetafile`, and `.jp2`, which it does not know, as `application/octet-stream`, the type for any unknown extension, as before.
- **Client impact:** None for the pictures of the folder, which are all `.png`.
- **Test:** `CatalogPicturesTests.Extension_case_does_not_change_the_content_type`.
- **Decision:** [ADR-0023](../DECISIONS.md#adr-0023-security-fixes-made-during-the-migration).

### BC-010: Range requests are honoured

- **Kind:** Contract delta
- **Forced by:** Platform, in the sense of [ADR-0002](../DECISIONS.md#adr-0002-wire-contract-policy), decision 5: the legacy file result could not serve ranges, and ASP.NET Core's can. Turning it on is the plan's decision (Stage 7.4), not a necessity.
- **Stage / commit:** 7.4, "Serve the item pictures from a confined folder (Stage 7.4)"
- **Legacy behaviour:** `Range: bytes=0-99` was ignored: 200 with the whole picture ([`pictures.json`](legacy/contract/pictures.json): `pic-get--range`). No picture had `Last-Modified`.
- **New behaviour:** 206, with `Content-Range: bytes 0-99/151640` and those 100 bytes. Every picture has `Accept-Ranges: bytes` and `Last-Modified`, and `If-Modified-Since` gets 304 when the file has not changed.
- **Client impact:** A client that sends `Range` gets the part that it asks for. A client that sends `If-Modified-Since` can get a 304 without a body.
- **Test:** `LegacyContractTests` replays `pic-get--range`, in database mode and in mock mode, and compares the 100 bytes with the start of the recorded picture. `PictureEndpointsTests.Picture_has_its_last_modified_time_and_a_conditional_request_gets_304`.
- **Decision:** [ADR-0023](../DECISIONS.md#adr-0023-security-fixes-made-during-the-migration).

### BC-011: Methods other than GET on the picture route are a 405

- **Kind:** Contract delta
- **Forced by:** Platform
- **Stage / commit:** 7.4, "Serve the item pictures from a confined folder (Stage 7.4)"
- **Legacy behaviour:** MVC answered `HEAD` and `POST` on the picture route with 404 ([`pictures.json`](legacy/contract/pictures.json): `pic-head`, `pic-post`).
- **New behaviour:** ASP.NET Core's routing answers a method that the route does not have with 405 and `Allow: GET`, as Web API 2 answered on the brand routes.
- **Client impact:** 405 instead of 404. A client that probed pictures with `HEAD` has to use `GET`.
- **Test:** `LegacyContractTests` replays `pic-head` and `pic-post`, and compares the responses with those of `brands-head` and `brands-post`: 405 with `Allow: GET`.
- **Decision:** [ADR-0023](../DECISIONS.md#adr-0023-security-fixes-made-during-the-migration), which leaves `HEAD` support to a version 2.

### BC-012: Paging is validated

- **Kind:** Business-rule change
- **Forced by:** Security
- **Stage / commit:** 7.5, "Expose the catalog reads: items, one item and types (Stage 7.5)"
- **Legacy behaviour:** The item list accepted any page size and index. A page size of 0 divided by zero, a negative size or index, and a size times index that overflowed an `int`, each gave a 500. A page size of 100000 read the whole table, and `pageSize=abc` fell back to the default ([`catalog-reads`](legacy/evidence/catalog-reads.json), audit D6).
- **New behaviour:** `GET /api/items` takes `pageSize` from 1 to 100, default 10, and `pageIndex` from 0, default 0. A value out of its range is a 400 problem that names the parameter in `errors`, and a value that is not an integer, such as `abc`, is a 400 problem too. A page after the last one is empty, with the totals.
- **Client impact:** A client that asks for more than 100 items pages through them. A malformed value is a 400 instead of the default page.
- **Test:** `ItemEndpointsTests.Paging_outside_its_bounds_is_a_400_problem`, `Page_of_100_items_is_allowed` and `Page_holds_the_items_of_its_index_and_a_page_after_the_last_is_empty`.
- **Decision:** [ADR-0024](../DECISIONS.md#adr-0024-item-and-type-reads), and [ADR-0015](../DECISIONS.md#adr-0015-async-first-catalog-service).

### BC-013: Clients do not write an item's ID or picture

- **Kind:** Business-rule change
- **Forced by:** Security
- **Stage / commit:** 7.6, "Create items with the legacy rules and a 4 MB body limit (Stage 7.6)"
- **Legacy behaviour:** The create form bound a posted `Id`, which HiLo then replaced, and a posted `PictureFileName`, which was stored and served, even when it pointed outside the pictures folder ([`create-ignores-posted-id`](legacy/evidence/create-ignores-posted-id.json), [`pic-path-traversal-relative`](legacy/evidence/pic-path-traversal-relative.json), audit D1 and D2).
- **New behaviour:** `POST /api/items` has neither field. A posted `Id` or `PictureFileName` is ignored, and the item gets a new ID and `dummy.png`, as from the legacy form.
- **Client impact:** A client can no longer set a picture name. Nothing in the legacy UI offered it.
- **Test:** `ItemWriteEndpointsTests.Posted_ID_and_picture_name_are_ignored` and `Created_item_gets_a_new_ID_the_default_picture_and_its_location`.
- **Decision:** [ADR-0025](../DECISIONS.md#adr-0025-creating-items), and [ADR-0015](../DECISIONS.md#adr-0015-async-first-catalog-service).

### BC-014: Invalid items are a 400

- **Kind:** Business-rule change
- **Forced by:** Security
- **Stage / commit:** 7.6, "Create items with the legacy rules and a 4 MB body limit (Stage 7.6)"
- **Legacy behaviour:** `[Range(0, 1000000)]` rounded the price to an integer first, so 1000000.01 to 1000000.50 were accepted, and a price above `Int32.MaxValue` gave a 500 (D9). A name over 50 characters, and an unknown brand or type, passed the form's validation and gave a 500 when saved (D10) ([`create-item-validation`](legacy/evidence/create-item-validation.json)). `OnReorder`, which the form did not have, was `false` unless posted.
- **New behaviour:** The price must be from 0 to 1,000,000 exactly, with at most two decimal places. A longer name, and an unknown brand or type, are a 400 problem that names the field. `OnReorder` is required, like the other fields.
- **Client impact:** A price above 1,000,000 is refused, and a create without `OnReorder` is a 400. The other cases were 500s.
- **Test:** `ItemWriteEndpointsTests.Field_that_breaks_its_rule_is_a_400_problem_that_names_it`, `Unknown_brand_or_type_is_a_400_problem_that_names_it` and `Value_at_the_edge_of_its_rule_is_accepted`.
- **Decision:** [ADR-0025](../DECISIONS.md#adr-0025-creating-items).

## Known upcoming deltas

The Stage 1 audit and characterization already show where the new API will differ. The list below records them so that none is forgotten. Each becomes an entry, with a number and a test, in the commit that implements it. The list is not an entry itself, and it does not pre-empt the decisions of later stages.

| Legacy behaviour (evidence) | Expected change | Forced by | Stage |
|---|---|---|---|
| `PictureFileName` and `Id` are client-writable on create and edit; edit overwrites fields the client did not send ([`edit-overwrites-unposted-fields`](legacy/evidence/edit-overwrites-unposted-fields.json)) | `PictureFileName` is not client-writable, and updates apply explicit fields. | Security | 7.7 (create: BC-013) |
| Editing or deleting an unknown item gives 500 ([`unknown-item-writes`](legacy/evidence/unknown-item-writes.json)) | To be decided in 7.7 (a 404 is expected). | Security | 7.7 |

Some response differences are not deltas, because the [comparison rules](legacy/README.md#comparison-rules) treat them as informational:

- the `Server`, `X-Powered-By`, `X-AspNet-Version` and `X-AspNetMvc-Version` headers
- the Web API no-cache headers
- the MVC session cookie
