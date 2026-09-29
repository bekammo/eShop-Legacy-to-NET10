# Legacy characterization

This folder records what the legacy app (`src/eShopLegacyMVC`) actually does when it runs. The data was captured from the running app, not derived from the code, and later stages test the new API against it:

- Stage 4 checks the EF Core schema and seed data against `schema.json` and `seed-data.json`.
- Stage 7 replays the golden exchanges in `contract/` against the new endpoints.

The code-level analysis is in [`../legacy-audit.md`](../legacy-audit.md).

The capture ran against the legacy code at the commit in [`capture-info.json`](capture-info.json), hosted in IIS Express 10 (integrated pipeline, CLR v4.0) with a fresh SQL Server 2025 LocalDB database. The app ran, so the code-derived fallback in the migration plan was not needed.

## Contents

| Path | What it is |
|---|---|
| [`capture/capture.cs`](capture/capture.cs) | The capture tool (a .NET 10 file-based app). It produces everything else in this folder. |
| [`capture-info.json`](capture-info.json) | Capture date, legacy commit, IIS Express and SQL Server versions. |
| [`schema.sql`](schema.sql) | The schema the legacy app creates, as one executable T-SQL batch. |
| [`schema.json`](schema.json) | The same schema, machine-readable: columns, keys, indexes, foreign keys with delete actions, sequences. |
| [`seed-data.json`](seed-data.json) | All rows and sequence values right after the first start. |
| [`ef6-model.edmx`](ef6-model.edmx) | The EF6 model that `CreateDatabaseIfNotExists` stored (gzip-compressed) in `__MigrationHistory`. |
| [`contract/`](contract) | 61 golden HTTP exchanges for the endpoints the new API keeps or retires, in 7 files (one per endpoint group). |
| [`evidence/`](evidence) | 12 scenarios run through the MVC UI, including the defects, plus a log4net output sample. |

## Re-running the capture

Prerequisites (Windows): IIS Express, SQL Server LocalDB with the `MSSQLLocalDB` instance, the .NET 10 SDK, and a Debug build of `eShopLegacyMVC.sln` (see the root README).

```bash
dotnet run docs/legacy/capture/capture.cs
```

Options:

- `--build` builds the legacy solution first, with the MSBuild that `vswhere` finds.
- `--reset` drops the legacy database if it already exists. The tool refuses to run against an existing database, because a capture must start from a fresh one.
- `--keep-db` keeps the database afterwards. By default the tool drops it, so the next run starts fresh and the machine is left as it was.
- `--port <n>` changes the IIS Express port (default 52429, the `IISUrl` port in the csproj).

The tool uses `Web.config` exactly as committed: `(localdb)\MSSQLLocalDB`, database `Microsoft.eShopOnContainers.Services.CatalogDb`. The database name cannot be changed, because the legacy sequence scripts hard-code `USE [Microsoft.eShopOnContainers.Services.CatalogDb]` (see the audit).

What the tool does, in order:

1. Starts IIS Express on the legacy project folder.
2. Sends `GET /api/brands`. This is the first request that uses the `DbContext`, so EF6 creates and seeds the database.
3. Captures the schema, the seed data and the EF6 model.
4. Replays the contract requests in the order they appear in `capture.cs`.
5. Runs the evidence scenarios. They create, edit and delete items through the MVC forms, using the anti-forgery token flow.
6. Restarts IIS Express and creates one more item, to show the HiLo gap after a restart.
7. Stops IIS Express, writes a log4net sample, and drops the database.

Two runs produce identical files, except for these values:

- `capturedAtUtc`
- the timestamp in the EF6 `MigrationId`
- the timestamps and thread IDs in `log4net-sample.log`

Before anything is stored:

- The `Date` and `X-SourceFiles` headers are dropped.
- `Set-Cookie` values are replaced with `<redacted>`, and so is the anti-forgery token in form bodies.
- Machine paths in error titles are replaced with `<repo>`, `<user-profile>` and `<machine>`.

The requests came from localhost, so error responses carry the detail that ASP.NET and Web API show only to local clients:

- Web API `MessageDetail`
- exception pages with the exception type

Remote clients of the legacy app got less. See [audit section 2.5](../legacy-audit.md#25-error-handling).

## Golden exchange format

Each file in `contract/` holds the exchanges of one endpoint group. `exchanges` maps each exchange name to one request and its response, in capture order:

```json
{
  "group": "brands-get-by-id",
  "description": "GET /api/brands/{id}: valid, unknown, zero, negative, ...",
  "exchanges": {
    "brands-get-by-id--json": {
      "description": "Get one brand, JSON requested.",
      "request":  { "method": "GET", "path": "/api/brands/1", "headers": { "Accept": "application/json" }, "body": null },
      "response": {
        "status": 200,
        "reasonPhrase": "OK",
        "headers": { "Content-Type": "application/json; charset=utf-8", "...": "..." },
        "body": { "kind": "json", "json": { "Id": 1, "Brand": "Azure" } }
      }
    }
  }
}
```

Exchange names are unique across all files, so a name alone identifies an exchange. The rest of this documentation refers to exchanges by name.

`request.headers` lists every header the tool sent besides `Host`. There is no `User-Agent` or `Accept-Encoding`, and an exchange without `Accept` sent none. `response.headers` lists every header received, except the dropped ones.

`response.body.kind` is one of:

| kind | Fields | Used for |
|---|---|---|
| `empty` | none | No body. For `HEAD`, the headers still describe the `GET` body. |
| `json` | `json`: the parsed body | JSON responses. |
| `xml` | `text`: the raw body | XML responses (Web API content negotiation). |
| `html` | `title`, plus `exception` or `validationErrors` when present | IIS and ASP.NET error pages, and MVC pages. The markup is not stored: it is machine-specific. |
| `binary` | `length`, `sha256`, and `matchesFile` when the bytes equal a repo file | Pictures and the BinaryFormatter payload. |
| `text` | `text` | Any other text. None in the current capture. |

An evidence file has the same exchange shape inside `steps[]`, each step with a `label`. Steps that change data also carry `createdId` and `databaseRow` (the row read back from the database after the step). Form bodies are recorded as `{ "kind": "form", "fields": { ... } }`.

## Contract coverage

| File | Exchanges | What they pin down |
|---|---|---|
| [`brands-list.json`](contract/brands-list.json) | `brands-get-all--*` (10) | 200 with PascalCase JSON ordered by `Id`, for every `Accept`: none, JSON, `*/*`, `text/html`, `image/png`. XML for `application/xml`, `text/xml` and a browser `Accept`. Case-insensitive path, trailing slash accepted. |
| [`brands-get-by-id.json`](contract/brands-get-by-id.json) | `brands-get-by-id--*` (12) | 200 for 1, 5 and `01`, and for `?id=2` in the query string. 404 with an empty body for 0, -1 and 6. 400 with a Web API error body for `abc` and `2147483648`. `1.5` is a 404 from IIS itself, because IIS treats `.5` as a file extension. |
| [`brands-other-verbs.json`](contract/brands-other-verbs.json) | `brands-post`, `-put`, `-patch`, `-head`, `-options` (5) | 405 with an `Allow` header from Web API; `HEAD` is not mapped to `GET`. `OPTIONS` is answered by IIS (200, `Allow: OPTIONS, TRACE, GET, HEAD, POST`) before the request reaches the app. |
| [`brands-delete.json`](contract/brands-delete.json) | `brands-delete*` (5) | 200 with an empty body, and the brand still exists afterwards: the delete is a no-op. 404 for an unknown ID, 400 for `abc`, 405 without an ID. |
| [`files.json`](contract/files.json) | `files-get*` (3) | 200 with a 721-byte BinaryFormatter stream labelled `Content-Type: text/html`. `Accept` is ignored, and `/api/files/1` returns the same payload. |
| [`api-root.json`](contract/api-root.json) | `api-root*`, `api-unknown-controller` (3) | `/api` and `/api/` are ASP.NET 404 pages: `CatalogController2` is unreachable. An unknown `api/{controller}` gets a Web API 404 JSON body. |
| [`pictures.json`](contract/pictures.json) | `pic-get--item-01` … `-12`, `pic-get--*`, `pic-head`, `pic-post` (23) | Seeded items 1–12: 200 `image/png`, byte-identical to `Pics/1.png` … `Pics/12.png`. A request without a session cookie gets an `ASP.NET_SessionId` cookie. Edge cases: 400 for 0 and -1. 404 for an unknown item, and for `abc`, `2147483648`, `HEAD` and `POST` (no route matches). Case-insensitive path, trailing slash accepted. `Range` is ignored (200 with the full body). `Accept` is ignored. |

## Evidence

The files in `evidence/` describe legacy behaviour that is reachable only through the MVC UI. They are reference material, not contract.

| File | Shows |
|---|---|
| [`create-item-validation.json`](evidence/create-item-validation.json) | 25 create attempts, one rule or boundary each. Required fields. Price: two decimals, no sign, no comma. `Range(0, 1000000)` rounds the decimal to an integer before comparing, so 1000000.50 is accepted and 1000000.51 is not. A 51-character name gives a 500 (EF6 `DbEntityValidationException`), an unknown brand or type gives a 500 (FK violation), and a missing anti-forgery token gives a 500. |
| [`create-ignores-posted-id.json`](evidence/create-ignores-posted-id.json) | A posted `Id` is bound, then overwritten by HiLo. Item 1 is untouched. |
| [`create-default-picture.json`](evidence/create-default-picture.json) | Items created through the form get `dummy.png`. |
| [`pic-missing-file.json`](evidence/pic-missing-file.json) | A picture file that does not exist gives a 500 (`FileNotFoundException`). |
| [`pic-extension-case.json`](evidence/pic-extension-case.json) | `1.PNG` is served as `application/octet-stream`: the MIME switch is case-sensitive, and the Windows file lookup is not. |
| [`pic-path-traversal-relative.json`](evidence/pic-path-traversal-relative.json) | `PictureFileName = ..\Global.asax` serves `Global.asax`. `PictureFileName` is not on the Create form but is in `[Bind]`. |
| [`pic-path-traversal-absolute.json`](evidence/pic-path-traversal-absolute.json) | `PictureFileName = C:\Windows\win.ini` serves that file. The body is not stored. |
| [`edit-overwrites-unposted-fields.json`](evidence/edit-overwrites-unposted-fields.json) | `EntityState.Modified` writes every column. A normal form edit resets `OnReorder` to false, because the form has no such field. A partial post nulls `Description`, zeroes the stock fields and resets the picture to `dummy.png`. |
| [`unknown-item-writes.json`](evidence/unknown-item-writes.json) | Editing item 999 gives a 500 (`DbUpdateConcurrencyException`), and deleting it gives a 500 (`ArgumentNullException`). |
| [`delete-item.json`](evidence/delete-item.json) | Delete is a hard delete. The picture route returns 404 afterwards. |
| [`catalog-reads.json`](evidence/catalog-reads.json) | Paging and details. `pageSize=0` gives a 500 (divide by zero), and so does a negative `pageSize` or `pageIndex` (EF6 `ArgumentException`), and so does `pageSize*pageIndex` overflowing. `pageSize=100000` is accepted, and `pageSize=abc` falls back to the default. Details: 400 without an ID or with `abc`, 404 for an unknown ID. |
| [`hilo-restart-gap.json`](evidence/hilo-restart-gap.json) | Item IDs within one process, and the jump to the next block of 10 after a restart. Failed inserts also consume IDs. |
| [`log4net-sample.log`](evidence/log4net-sample.log) | Real log output. `%property{activity}` always prints `(null)`, because the code sets `activityid`. Controller disposal is logged twice per request. |

When a create attempt breaks more than one rule, which message the form shows is not stable between app starts. In the first capture run, a price of -1 showed the Range message; in later runs it showed the regular-expression message. The matrix therefore breaks one rule per step, and only the status code and field of a multi-rule failure should be relied on.

## Comparison rules

### Contract (`contract/*.json`), used from Stage 7

1. Only `contract/` is the wire contract. `evidence/` is not. Each entry of `exchanges` in each file is one test case.
2. Replay each exchange as recorded: method, path verbatim (case and trailing slash included), exactly the recorded request headers, and the recorded body. The exchanges do not depend on each other's side effects (the only write, `brands-delete`, is a no-op), so they can run in any order against a freshly seeded database.
3. The status code must be equal. The only exceptions are deltas recorded in [`../behavior-changes.md`](../behavior-changes.md). A test for an exchange with a delta asserts the new behaviour explicitly and names the delta.
4. For responses with a body, the media type must be equal (case-insensitive). When the legacy value carries `charset=utf-8`, so must the new one.
5. Success JSON bodies are compared as JSON trees:
   - Property names are case-sensitive: PascalCase is part of the contract.
   - Value types and values must be equal. Numbers are compared by value, so `19.5` equals `19.50`.
   - Arrays must have the same order.
   - Object member order does not matter.
   - No property may be missing or extra.
6. Success responses recorded as `empty` must have no body.
7. Error bodies are not contract. The legacy error bodies are framework output: Web API `{"Message", "MessageDetail"}`, IIS and ASP.NET HTML pages, or nothing. For 4xx and 5xx responses only the status code is compared, plus the `Allow` header on 405 (compared as a set of methods). The new error format is decided in Stage 7.1.
8. XML is not ported. For an exchange whose legacy body is `xml`, the test asserts that the new API answers the same request with the JSON body of the matching JSON exchange: `brands-get-all--accept-xml` is checked against `brands-get-all--accept-json`, and so on. This delta is recorded in the register.
9. Binary bodies are compared by length and SHA-256. `matchesFile` names the source file, so the check still holds after Stage 11.2 moves the pictures, as long as the bytes do not change.
10. These headers are informational and not compared:
    - `Server`, `X-Powered-By`, `X-AspNet-Version`, `X-AspNetMvc-Version`: they disclose the stack, and the new API does not send them.
    - `Cache-Control`, `Pragma`, `Expires`: Web API no-cache defaults.
    - `Set-Cookie`: the MVC session cookie. The new API is stateless.
    - `Content-Length`: implied by the body.
    - `Public`: the IIS `OPTIONS` handler.
11. Some exchanges record host behaviour, not app behaviour:
    - `brands-options`: IIS `OPTIONSVerbHandler`.
    - `brands-get-by-id--dot-in-segment`: IIS static file handler.
    - `api-root*`, `pic-get--non-integer`, `pic-get--overflow`, `pic-post`: ASP.NET "resource cannot be found" pages.

    Their status code is still the contract. A difference forced by the platform (Kestrel instead of IIS) is recorded as a delta.
12. `files-*` exchanges describe the retired endpoint. The new API answers `410 Gone` (Stage 7.3), and the tests assert that against this delta.

### Schema (`schema.json`), used from Stage 4.1

Stage 4.1 compares the EF Core model with this file, and Stage 4.2 compares the database that the migrations create. Both state the schema as one line per column, key, index, foreign key, check constraint or sequence ("schema facts", in `tests/Shared/Legacy`).

- Compared for `Catalog`, `CatalogBrand` and `CatalogType`:
  - columns: name, `storeType`, `nullable`, `identity`, and whether a default or a computed expression exists. The expressions themselves are not compared, because SQL Server rewrites them.
  - a column `collation`, only where it differs from the database collation. In the legacy schema every string column uses the server default (`SQL_Latin1_General_CP1_CI_AS`), which is not a model setting.
  - the primary key: name, clustered or not, and columns with their sort order
  - indexes and unique constraints: name, uniqueness, clustered or not, key columns with their sort order, included columns and filter
  - foreign keys: name, columns, principal table and columns, `onDelete`, `onUpdate`, and whether the key is enabled and trusted
  - check constraints. The legacy schema has none: `objectCounts` lists no `CHECK_CONSTRAINT`.
- Compared for the `catalog_hilo` sequence: type, start, increment, minimum, maximum, cycle, and the cache setting.
- `objectCounts`, from Stage 4.2 against a live database, so that objects in `sys.objects` without facts of their own (table triggers, views, procedures, functions, synonyms and so on) cannot slip in. Schemas, user-defined types and database-level DDL triggers are not in `sys.objects`; the tests require that there are none. Users, permissions and statistics are not compared. The count leaves out the two unused sequences below. EF6's `__MigrationHistory` table and its primary key give way to EF Core's `__EFMigrationsHistory` and its primary key, so those two counts stay the same.
- Not compared:
  - Column `ordinal`: EF Core orders columns its own way.
  - `__MigrationHistory`: it is EF6's table. EF Core keeps its own `__EFMigrationsHistory`.
  - `catalog_brand_hilo` and `catalog_type_hilo`: the legacy seeding reads them once, and the values it takes are discarded because the brand and type IDs are `IDENTITY` columns. The new schema does not have them ([ADR-0010](../../DECISIONS.md#adr-0010-data-model)). The baseline for existing databases (Stage 4.3) has to tolerate them.
- `objectCounts` shows that there are no views, procedures, triggers or default constraints.

### Seed data (`seed-data.json`), used from Stage 4.1

- Brand and type IDs and names must match exactly: the new app seeds them with `HasData` using the legacy IDs. Stage 4.1 checks the model's seed data, and Stage 4.2 the rows in the database.
- From Stage 4.4, the 12 items must match column by column. In a fresh database HiLo hands out IDs 1–12, so the IDs match too.
- `catalog_hilo` is 11 after seeding. The seeding took two blocks of 10: IDs 1–10, then 11–20, of which items 11 and 12 used two. An EF Core `UseHiLo` seeder with the same sequence takes the same blocks.
- The `__MigrationHistory` row is informational.

### Evidence (`evidence/`)

Not replayed as contract. Stage 7 uses it this way:

- The security and robustness fixes assert the fixed behaviour and cite the evidence file that shows the old one: path traversal, a missing file returning 404 instead of 500, case-insensitive MIME types, and bounded paging.
- The item validation rules for Stage 7.6 are derived from `create-item-validation.json`. Where the new API deliberately differs, for example by applying the price range without rounding, the difference is recorded in the delta register.
