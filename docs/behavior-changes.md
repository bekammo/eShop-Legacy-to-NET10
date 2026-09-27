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

No entries yet. Nothing has changed: the new API does not exist until Stage 2, and the first delta lands with the first ported endpoint in Stage 7.

## Known upcoming deltas

The Stage 1 audit and characterization already show where the new API will differ. The list below records them so that none is forgotten. Each becomes an entry, with a number and a test, in the commit that implements it. The list is not an entry itself, and it does not pre-empt the decisions of later stages.

| Legacy behaviour (evidence) | Expected change | Forced by | Stage |
|---|---|---|---|
| XML for `Accept: application/xml`, `text/xml` or a browser `Accept` ([`brands-list.json`](legacy/contract/brands-list.json): `brands-get-all--accept-xml` and the related exchanges) | JSON regardless of `Accept`. | Platform (Minimal APIs) | 7.1, 7.2 |
| Web API 2 error bodies `{"Message", "MessageDetail"}`, empty 404 bodies, IIS/ASP.NET HTML error pages | The error format chosen in 7.1 (ProblemDetails is planned). | Platform | 7.1 |
| `GET /api/files` returns 200 with a BinaryFormatter stream labelled `text/html` ([`files.json`](legacy/contract/files.json): `files-get`) | `410 Gone` with a ProblemDetails body that points to `/api/brands`. | Security | 7.3 |
| `OPTIONS /api/brands` answered by IIS with 200 ([`brands-other-verbs.json`](legacy/contract/brands-other-verbs.json): `brands-options`) | Whatever Kestrel routing answers (no CORS is configured). | Platform | 7.2 |
| `GET /api/brands/1.5` rejected by the IIS static file handler with 404 ([`brands-get-by-id.json`](legacy/contract/brands-get-by-id.json): `brands-get-by-id--dot-in-segment`) | Handled by the app like any other non-integer ID. | Platform | 7.2 |
| `GET /api/brands?id=2` returns one brand ([`brands-get-by-id.json`](legacy/contract/brands-get-by-id.json): `brands-get-by-id--query-string`) | To be decided in 7.2 (ADR-0002, rule 5). | Platform | 7.2 |
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
