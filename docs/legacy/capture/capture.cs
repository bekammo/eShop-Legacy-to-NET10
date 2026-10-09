#:package Microsoft.Data.SqlClient@6.1.1
#:property PublishAot=false

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

var options = Options.Parse(args);
var repo = Tools.FindRepoRoot(Directory.GetCurrentDirectory());
var appDir = Path.Combine(repo, "src", "eShopLegacyMVC");
var outDir = Path.Combine(repo, "docs", "legacy");
var contractDir = Path.Combine(outDir, "contract");
var evidenceDir = Path.Combine(outDir, "evidence");

if (options.Build)
{
    Tools.BuildLegacy(repo);
}

Tools.Require(File.Exists(Path.Combine(appDir, "bin", "eShopLegacyMVC.dll")),
    "The legacy app is not built. Build eShopLegacyMVC.sln with MSBuild first, or pass --build.");

var webConfig = XDocument.Load(Path.Combine(appDir, "Web.config"));
var appSettings = webConfig.Root!.Element("appSettings")!.Elements("add")
    .ToDictionary(e => (string)e.Attribute("key")!, e => (string)e.Attribute("value")!);
Tools.Require(!bool.Parse(appSettings["UseMockData"]), "Web.config has UseMockData=true. The capture needs the database mode.");
Tools.Require(!bool.Parse(appSettings["UseCustomizationData"]), "Web.config has UseCustomizationData=true. The capture expects the preconfigured seed data.");

var legacyConnectionString = (string)webConfig.Root!.Element("connectionStrings")!.Elements("add")
    .Single(e => (string?)e.Attribute("name") == "CatalogDBContext").Attribute("connectionString")!;
var dbBuilder = new SqlConnectionStringBuilder(legacyConnectionString)
{
    Encrypt = SqlConnectionEncryptOption.Optional,
    MultipleActiveResultSets = false,
    Pooling = false,
};
var dbName = dbBuilder.InitialCatalog;
var db = new Db(dbBuilder.ConnectionString);
var master = new Db(new SqlConnectionStringBuilder(dbBuilder.ConnectionString) { InitialCatalog = "master" }.ConnectionString);

if (await DatabaseExistsAsync())
{
    Tools.Require(options.Reset, $"Database [{dbName}] already exists on {dbBuilder.DataSource}. The capture needs a fresh database; pass --reset to drop it.");
    await DropDatabaseAsync();
}

foreach (var dir in new[] { contractDir, evidenceDir })
{
    if (Directory.Exists(dir))
    {
        foreach (var file in Directory.GetFiles(dir).Where(f => f.EndsWith(".json") || f.EndsWith(".log")))
        {
            File.Delete(file);
        }
    }
}

var knownFiles = new Dictionary<string, string>();
foreach (var file in Directory.GetFiles(Path.Combine(appDir, "Pics")).Order().Append(Path.Combine(appDir, "Global.asax")))
{
    knownFiles.TryAdd(Tools.Sha256File(file), Path.GetRelativePath(repo, file).Replace('\\', '/'));
}

Html.Redactions.Add((repo, "<repo>"));
Html.Redactions.Add((Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user-profile>"));
Html.Redactions.Add((Environment.MachineName, "<machine>"));

var logPath = Path.Combine(appDir, "logFiles", "myapp.log");
var logStart = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

var api = Http.CreateClient(options.Port, cookies: false);
var browser = Http.CreateClient(options.Port, cookies: true);
var antiForgeryToken = "";
var createdInFirstProcess = new List<int>();

var iis = await IisExpress.StartAsync(appDir, options.Port);
try
{
    await Contract("brands-get-all--accept-json", "List all brands, JSON requested. The first request creates and seeds the database.",
        "GET", "/api/brands", [("Accept", "application/json")]);

    Console.WriteLine("Capturing schema and seed data...");
    var (schemaJson, schemaSql) = await Schema.CaptureAsync(db);
    Json.Write(Path.Combine(outDir, "schema.json"), schemaJson);
    Tools.WriteText(Path.Combine(outDir, "schema.sql"), schemaSql);
    Json.Write(Path.Combine(outDir, "seed-data.json"), await Schema.CaptureSeedDataAsync(db, Path.Combine(outDir, "ef6-model.edmx"), repo));

    await Contract("brands-get-all--no-accept", "List all brands without an Accept header.",
        "GET", "/api/brands");
    await Contract("brands-get-all--accept-xml", "List all brands, XML requested.",
        "GET", "/api/brands", [("Accept", "application/xml")]);
    await Contract("brands-get-all--accept-text-xml", "List all brands, text/xml requested.",
        "GET", "/api/brands", [("Accept", "text/xml")]);
    await Contract("brands-get-all--accept-html", "List all brands, only text/html acceptable (no formatter matches).",
        "GET", "/api/brands", [("Accept", "text/html")]);
    await Contract("brands-get-all--accept-browser", "List all brands with a typical browser Accept header (XML is acceptable at q=0.9).",
        "GET", "/api/brands", [("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")]);
    await Contract("brands-get-all--accept-any", "List all brands, */* requested.",
        "GET", "/api/brands", [("Accept", "*/*")]);
    await Contract("brands-get-all--accept-unsupported", "List all brands, only image/png acceptable (no formatter matches).",
        "GET", "/api/brands", [("Accept", "image/png")]);
    await Contract("brands-get-all--path-upper-case", "Route matching is case-insensitive.",
        "GET", "/API/BRANDS", [("Accept", "application/json")]);
    await Contract("brands-get-all--trailing-slash", "Trailing slash on the collection route.",
        "GET", "/api/brands/", [("Accept", "application/json")]);

    await Contract("brands-get-by-id--json", "Get one brand, JSON requested.",
        "GET", "/api/brands/1", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--xml", "Get one brand, XML requested.",
        "GET", "/api/brands/1", [("Accept", "application/xml")]);
    await Contract("brands-get-by-id--last", "Get the last seeded brand.",
        "GET", "/api/brands/5", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--not-found", "Get a brand that does not exist.",
        "GET", "/api/brands/6", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--zero", "Get brand 0.",
        "GET", "/api/brands/0", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--negative", "Get brand -1.",
        "GET", "/api/brands/-1", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--non-integer", "Non-integer id, JSON requested.",
        "GET", "/api/brands/abc", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--non-integer-xml", "Non-integer id, XML requested.",
        "GET", "/api/brands/abc", [("Accept", "application/xml")]);
    await Contract("brands-get-by-id--overflow", "Id larger than Int32.MaxValue.",
        "GET", "/api/brands/2147483648", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--leading-zero", "Id with a leading zero.",
        "GET", "/api/brands/01", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--dot-in-segment", "Id that contains a dot. The last segment looks like a file extension to IIS.",
        "GET", "/api/brands/1.5", [("Accept", "application/json")]);
    await Contract("brands-get-by-id--query-string", "Id passed in the query string instead of the path.",
        "GET", "/api/brands?id=2", [("Accept", "application/json")]);

    await Contract("brands-post", "POST to the brands collection (no such action).",
        "POST", "/api/brands", [("Accept", "application/json")], new JsonObject { ["Brand"] = "Posted brand" });
    await Contract("brands-put", "PUT a brand (no such action).",
        "PUT", "/api/brands/1", [("Accept", "application/json")], new JsonObject { ["Id"] = 1, ["Brand"] = "Changed" });
    await Contract("brands-patch", "PATCH a brand (no such action).",
        "PATCH", "/api/brands/1", [("Accept", "application/json")], new JsonObject { ["Brand"] = "Changed" });
    await Contract("brands-head", "HEAD on the brands collection.",
        "HEAD", "/api/brands", [("Accept", "application/json")]);
    await Contract("brands-options", "OPTIONS on the brands collection (no CORS configured).",
        "OPTIONS", "/api/brands", [("Accept", "application/json")]);

    await Contract("brands-delete", "Delete an existing brand. The legacy action is a no-op that returns 200.",
        "DELETE", "/api/brands/1", [("Accept", "application/json")]);
    await Contract("brands-delete--then-get", "The brand deleted in brands-delete still exists.",
        "GET", "/api/brands/1", [("Accept", "application/json")]);
    await Contract("brands-delete--not-found", "Delete a brand that does not exist.",
        "DELETE", "/api/brands/6", [("Accept", "application/json")]);
    await Contract("brands-delete--non-integer", "Delete with a non-integer id.",
        "DELETE", "/api/brands/abc", [("Accept", "application/json")]);
    await Contract("brands-delete--no-id", "DELETE on the collection route.",
        "DELETE", "/api/brands", [("Accept", "application/json")]);

    await Contract("files-get", "BinaryFormatter payload of the brand list (retired in the new API).",
        "GET", "/api/files");
    await Contract("files-get--accept-json", "JSON requested; the action ignores content negotiation.",
        "GET", "/api/files", [("Accept", "application/json")]);
    await Contract("files-get-by-id", "Id on a controller that only has a parameterless Get.",
        "GET", "/api/files/1");

    await Contract("api-root", "GET /api, the route of the unreachable CatalogController2.",
        "GET", "/api", [("Accept", "application/json")]);
    await Contract("api-root--trailing-slash", "GET /api/.",
        "GET", "/api/", [("Accept", "application/json")]);
    await Contract("api-unknown-controller", "An api/{controller} route for a controller that does not exist.",
        "GET", "/api/catalog", [("Accept", "application/json")]);

    for (var id = 1; id <= 12; id++)
    {
        await Contract($"pic-get--item-{id:00}", $"Picture of seeded item {id}.",
            "GET", $"/items/{id}/pic");
    }

    await Contract("pic-get--accept-json", "Picture with Accept: application/json; the action ignores Accept.",
        "GET", "/items/1/pic", [("Accept", "application/json")]);
    await Contract("pic-get--not-found", "Picture of an item that does not exist.",
        "GET", "/items/13/pic");
    await Contract("pic-get--zero", "Picture of item 0.",
        "GET", "/items/0/pic");
    await Contract("pic-get--negative", "Picture of item -1.",
        "GET", "/items/-1/pic");
    await Contract("pic-get--non-integer", "Non-integer id; the :int route constraint does not match.",
        "GET", "/items/abc/pic");
    await Contract("pic-get--overflow", "Id larger than Int32.MaxValue; the :int route constraint does not match.",
        "GET", "/items/2147483648/pic");
    await Contract("pic-get--range", "Range request for the first 100 bytes.",
        "GET", "/items/1/pic", [("Range", "bytes=0-99")]);
    await Contract("pic-get--path-upper-case", "Route matching is case-insensitive.",
        "GET", "/ITEMS/1/PIC");
    await Contract("pic-get--trailing-slash", "Trailing slash after /pic.",
        "GET", "/items/1/pic/");
    await Contract("pic-head", "HEAD on a picture.",
        "HEAD", "/items/1/pic");
    await Contract("pic-post", "POST to a picture route.",
        "POST", "/items/1/pic");
    WriteContract();

    var createPage = await Http.SendAsync(browser, "GET", "/Catalog/Create");
    antiForgeryToken = Html.AntiForgeryToken(createPage.Text)
        ?? throw new InvalidOperationException("No anti-forgery token on /Catalog/Create.");

    await EvidenceValidationAsync();
    await EvidenceCreateIgnoresIdAsync();
    await EvidenceCreateDefaultPictureAsync();
    await EvidencePictureAsync("pic-missing-file",
        "A picture file that does not exist. PicController reads the file without checking that it exists.",
        "missing.png");
    await EvidencePictureAsync("pic-extension-case",
        "An upper-case extension. The MIME switch is case-sensitive, while the Windows file lookup is not.",
        "1.PNG");
    await EvidencePictureAsync("pic-path-traversal-relative",
        "A relative path that leaves the Pics folder. Path.Combine does not stop '..', so any file the app pool can read is served.",
        @"..\Global.asax");
    await EvidencePictureAsync("pic-path-traversal-absolute",
        "An absolute path. Path.Combine returns the second argument when it is rooted. The response body is not stored.",
        @"C:\Windows\win.ini", storeBody: false);
    await EvidenceEditOverwritesUnpostedFieldsAsync();
    await EvidenceUnknownItemWritesAsync();
    await EvidenceDeleteAsync();
    await EvidenceReadsAsync();
}
finally
{
    await iis.DisposeAsync();
}

var idsBeforeRestart = createdInFirstProcess.ToList();
var sequenceBeforeRestart = await db.ScalarAsync<long>("SELECT CAST(current_value AS bigint) FROM sys.sequences WHERE name = 'catalog_hilo'");
iis = await IisExpress.StartAsync(appDir, options.Port);
try
{
    browser = Http.CreateClient(options.Port, cookies: true);
    var createPage = await Http.SendAsync(browser, "GET", "/Catalog/Create");
    antiForgeryToken = Html.AntiForgeryToken(createPage.Text)
        ?? throw new InvalidOperationException("No anti-forgery token on /Catalog/Create.");

    var step = await PostFormAsync("/Catalog/Create", Forms.Item("HiLo after restart"));
    var id = await ItemIdAsync("HiLo after restart");
    var sequenceAfterRestart = await db.ScalarAsync<long>("SELECT CAST(current_value AS bigint) FROM sys.sequences WHERE name = 'catalog_hilo'");
    Evidence("hilo-restart-gap",
        "CatalogItemHiLoGenerator keeps its block in memory. After an app restart it takes a new block from catalog_hilo, " +
        "so the unused IDs of the previous block are skipped.",
        [Step("Create an item after an IIS Express restart", step, new JsonObject { ["createdId"] = id })],
        new JsonObject
        {
            ["itemIdsCreatedBeforeRestart"] = new JsonArray(idsBeforeRestart.Select(i => (JsonNode?)i).ToArray()),
            ["catalogHiloBeforeRestart"] = sequenceBeforeRestart,
            ["itemIdCreatedAfterRestart"] = id,
            ["catalogHiloAfterRestart"] = sequenceAfterRestart,
        });
}
finally
{
    await iis.DisposeAsync();
}

await WriteLogSampleAsync();
Json.Write(Path.Combine(outDir, "capture-info.json"), await CaptureInfoAsync());

if (!options.KeepDb)
{
    await DropDatabaseAsync();
}

Console.WriteLine($"Done. Output written to {Path.GetRelativePath(repo, outDir)}.");
return 0;

async Task<Exchange> Contract(string name, string description, string method, string path,
    (string Name, string Value)[]? headers = null, JsonNode? jsonBody = null)
{
    HttpContent? content = jsonBody is null ? null : new StringContent(jsonBody.ToJsonString(), Encoding.UTF8, "application/json");
    var recordedBody = jsonBody is null ? null : new JsonObject { ["kind"] = "json", ["json"] = jsonBody.DeepClone() };
    var exchange = await Http.SendAsync(api, method, path, headers, content, recordedBody, knownFiles);
    var group = ContractGroups.For(name);
    group.Exchanges[name] = new JsonObject
    {
        ["description"] = description,
        ["request"] = exchange.Request,
        ["response"] = exchange.Response,
    };
    Console.WriteLine($"{exchange.Status} {method} {path} -> contract/{group.File}.json");
    return exchange;
}

void WriteContract()
{
    foreach (var group in ContractGroups.All)
    {
        Json.Write(Path.Combine(contractDir, group.File + ".json"), new JsonObject
        {
            ["group"] = group.File,
            ["description"] = group.Description,
            ["exchanges"] = group.Exchanges,
        });
    }
}

void Evidence(string name, string summary, JsonObject[] steps, JsonObject? database = null)
{
    var record = new JsonObject
    {
        ["name"] = name,
        ["summary"] = summary,
        ["steps"] = new JsonArray(steps.Cast<JsonNode?>().ToArray()),
    };
    if (database is not null)
    {
        record["database"] = database;
    }
    Json.Write(Path.Combine(evidenceDir, name + ".json"), record);
    Console.WriteLine($"evidence/{name}.json ({steps.Length} steps)");
}

JsonObject Step(string label, Exchange exchange, JsonObject? extra = null)
{
    var step = new JsonObject { ["label"] = label, ["request"] = exchange.Request.DeepClone(), ["response"] = exchange.Response.DeepClone() };
    foreach (var (key, value) in extra ?? new JsonObject())
    {
        step[key] = value?.DeepClone();
    }
    return step;
}

async Task<Exchange> PostFormAsync(string path, IEnumerable<(string Name, string Value)> fields, bool withToken = true)
{
    List<(string Name, string Value)> all = [.. withToken ? fields.Prepend(("__RequestVerificationToken", antiForgeryToken)) : fields];
    var content = new FormUrlEncodedContent(all.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
    var recorded = new JsonObject
    {
        ["kind"] = "form",
        ["fields"] = new JsonObject(all.Select(f => KeyValuePair.Create(f.Name,
            (JsonNode?)(f.Name == "__RequestVerificationToken" ? "<redacted>" : f.Value)))),
    };
    var exchange = await Http.SendAsync(browser, "POST", path, null, content, recorded, knownFiles);
    Console.WriteLine($"{exchange.Status} POST {path}");
    return exchange;
}

async Task<Exchange> GetAsync(HttpClient client, string path)
{
    var exchange = await Http.SendAsync(client, "GET", path, null, null, null, knownFiles);
    Console.WriteLine($"{exchange.Status} GET {path}");
    return exchange;
}

async Task<int?> ItemIdAsync(string name)
{
    var id = await db.ScalarAsync<int?>("SELECT Id FROM dbo.Catalog WHERE Name = @p0", name);
    if (id is int created)
    {
        createdInFirstProcess.Add(created);
    }
    return id;
}

async Task<JsonNode?> ItemRowAsync(int? id)
{
    if (id is null)
    {
        return null;
    }
    var rows = await db.QueryAsync("SELECT * FROM dbo.Catalog WHERE Id = @p0", id.Value);
    return rows.Count == 0 ? null : rows[0].ToJson();
}

async Task<JsonObject> CreatedStepAsync(string label, string itemName, IEnumerable<(string, string)> fields)
{
    var exchange = await PostFormAsync("/Catalog/Create", fields);
    var id = exchange.Status == 302 ? await ItemIdAsync(itemName) : null;
    return Step(label, exchange, new JsonObject { ["createdId"] = id, ["databaseRow"] = await ItemRowAsync(id) });
}

async Task EvidenceValidationAsync()
{
    var steps = new List<JsonObject>
    {
        await CreatedStepAsync("Valid item", "Validation 01 valid", Forms.Item("Validation 01 valid")),
        await CreatedStepAsync("Name missing", "", Forms.Item("")),
        await CreatedStepAsync("Name with exactly 50 characters", new string('M', 50), Forms.Item(new string('M', 50))),
        await CreatedStepAsync("Name longer than the 50-character column (EF6 validates MaxLength on SaveChanges)", new string('N', 51), Forms.Item(new string('N', 51))),
        await CreatedStepAsync("Price with three decimals", "Validation 03 price 1.234", Forms.Item("Validation 03 price 1.234", price: "1.234")),
        await CreatedStepAsync("Negative price", "Validation 04 price -1", Forms.Item("Validation 04 price -1", price: "-1")),
        await CreatedStepAsync("Small negative price", "Validation 04b price -0.01", Forms.Item("Validation 04b price -0.01", price: "-0.01")),
        await CreatedStepAsync("Price zero", "Validation 05 price 0", Forms.Item("Validation 05 price 0", price: "0")),
        await CreatedStepAsync("Price at the Range maximum", "Validation 06 price 1000000", Forms.Item("Validation 06 price 1000000", price: "1000000")),
        await CreatedStepAsync("Price just above the Range maximum", "Validation 07 price 1000000.01", Forms.Item("Validation 07 price 1000000.01", price: "1000000.01")),
        await CreatedStepAsync("Price half a unit above the Range maximum", "Validation 07b price 1000000.50", Forms.Item("Validation 07b price 1000000.50", price: "1000000.50")),
        await CreatedStepAsync("Price more than half a unit above the Range maximum", "Validation 07c price 1000000.51", Forms.Item("Validation 07c price 1000000.51", price: "1000000.51")),
        await CreatedStepAsync("Price one unit above the Range maximum", "Validation 07d price 1000001", Forms.Item("Validation 07d price 1000001", price: "1000001")),
        await CreatedStepAsync("Non-numeric price", "Validation 08 price abc", Forms.Item("Validation 08 price abc", price: "abc")),
        await CreatedStepAsync("Price with a decimal comma", "Validation 09 price 12,5", Forms.Item("Validation 09 price 12,5", price: "12,5")),
        await CreatedStepAsync("Price with a group separator", "Validation 10 price 1,000.50", Forms.Item("Validation 10 price 1,000.50", price: "1,000.50")),
        await CreatedStepAsync("Price with one decimal", "Validation 11 price 7.5", Forms.Item("Validation 11 price 7.5", price: "7.5")),
        await CreatedStepAsync("Price missing", "Validation 12 price missing", Forms.Item("Validation 12 price missing", price: "")),
        await CreatedStepAsync("Negative stock", "Validation 13 stock -1", Forms.Item("Validation 13 stock -1", stock: "-1")),
        await CreatedStepAsync("Max stock above 10 million", "Validation 14 max stock 10000001", Forms.Item("Validation 14 max stock 10000001", maxStock: "10000001")),
        await CreatedStepAsync("Stock missing (non-nullable int)", "Validation 15 stock missing", Forms.Item("Validation 15 stock missing", stock: "")),
        await CreatedStepAsync("Description missing", "Validation 16 description missing", Forms.Item("Validation 16 description missing", description: "")),
        await CreatedStepAsync("Unknown brand (FK violation on SaveChanges)", "Validation 17 brand 99", Forms.Item("Validation 17 brand 99", brandId: "99")),
        await CreatedStepAsync("Unknown type (FK violation on SaveChanges)", "Validation 18 type 99", Forms.Item("Validation 18 type 99", typeId: "99")),
    };

    var noToken = await PostFormAsync("/Catalog/Create", Forms.Item("Validation 19 no token"), withToken: false);
    steps.Add(Step("Anti-forgery token missing", noToken, new JsonObject { ["createdId"] = await db.ScalarAsync<int?>("SELECT Id FROM dbo.Catalog WHERE Name = @p0", "Validation 19 no token") }));

    Evidence("create-item-validation",
        "POST /Catalog/Create with the fields of the Create form, one rule or boundary exercised per step. 302 means the item was created; " +
        "200 means the form came back with the validation messages listed under response.body.validationErrors. " +
        "The rules come from the data annotations on CatalogItem and the en-US culture pinned in Web.config.",
        steps.ToArray());
}

async Task EvidenceCreateIgnoresIdAsync()
{
    var before = await ItemRowAsync(1);
    var step = await CreatedStepAsync("Create with Id=1 posted (Id is in the [Bind] include list)", "Posted Id 1",
        Forms.Item("Posted Id 1").Prepend(("Id", "1")));
    Evidence("create-ignores-posted-id",
        "Id is in the [Bind] include list of Create, but CatalogService.CreateCatalogItem overwrites it with the next HiLo value.",
        [step],
        new JsonObject { ["item1Before"] = before, ["item1After"] = await ItemRowAsync(1) });
}

async Task EvidenceCreateDefaultPictureAsync()
{
    var create = await CreatedStepAsync("Create without PictureFileName (the Create form has no such field)", "Default picture",
        Forms.Item("Default picture"));
    var id = create["createdId"]!.GetValue<int>();
    var picture = await GetAsync(browser, $"/items/{id}/pic");
    Evidence("create-default-picture",
        "The CatalogItem constructor sets PictureFileName to dummy.png, so an item created through the form serves Pics/dummy.png.",
        [create, Step("Get the picture of the new item", picture)]);
}

async Task EvidencePictureAsync(string name, string summary, string pictureFileName, bool storeBody = true)
{
    var itemName = "Picture " + name;
    var create = await CreatedStepAsync($"Create an item with PictureFileName '{pictureFileName}' (not a form field, but in the [Bind] include list)",
        itemName, Forms.Item(itemName).Append(("PictureFileName", pictureFileName)));
    var id = create["createdId"]!.GetValue<int>();
    var picture = await GetAsync(browser, $"/items/{id}/pic");
    var step = Step("Get the picture of the new item", picture);
    if (!storeBody && step["response"]!["body"] is JsonObject body)
    {
        body.Remove("sha256");
        body.Remove("length");
        body["note"] = "Body not stored: it is a file from the capture machine.";
    }
    Evidence(name, summary, [create, step]);
}

async Task EvidenceEditOverwritesUnpostedFieldsAsync()
{
    var before = await ItemRowAsync(2);
    var steps = new List<JsonObject>();

    var full = await PostFormAsync("/Catalog/Edit/2", Forms.EditFields(before!).Append(("OnReorder", "true")));
    steps.Add(Step("Post every [Bind] field, including OnReorder=true (the Edit form has no OnReorder field)", full,
        new JsonObject { ["databaseRow"] = await ItemRowAsync(2) }));

    var formShaped = await PostFormAsync("/Catalog/Edit/2", Forms.EditFields((await ItemRowAsync(2))!));
    steps.Add(Step("Post exactly the fields the Edit form renders", formShaped,
        new JsonObject { ["databaseRow"] = await ItemRowAsync(2) }));

    var partial = await PostFormAsync("/Catalog/Edit/2",
        [("Id", "2"), ("Name", ".NET Black & White Mug"), ("CatalogBrandId", "2"), ("CatalogTypeId", "1"), ("Price", "8.50")]);
    steps.Add(Step("Post only Id, Name, brand, type and price", partial,
        new JsonObject { ["databaseRow"] = await ItemRowAsync(2) }));

    Evidence("edit-overwrites-unposted-fields",
        "CatalogService.UpdateCatalogItem attaches the bound object with EntityState.Modified, so every column is written. " +
        "Fields the client does not post are saved as their defaults: OnReorder (not on the Edit form) is reset to false by every " +
        "normal edit, and a partial post clears Description, zeroes the stock fields and resets PictureFileName to dummy.png " +
        "(the CatalogItem constructor default). PictureFileName is read-only on the form but still bound, so it can be changed too.",
        steps.ToArray(),
        new JsonObject { ["item2Before"] = before });
}

async Task EvidenceUnknownItemWritesAsync()
{
    var edit = await PostFormAsync("/Catalog/Edit/999", Forms.Item("Unknown item").Prepend(("Id", "999")).Append(("PictureFileName", "dummy.png")));
    var delete = await PostFormAsync("/Catalog/Delete/999", []);
    Evidence("unknown-item-writes",
        "Edit and delete of an item that does not exist. UpdateCatalogItem updates zero rows and DeleteConfirmed passes null to DbSet.Remove; " +
        "both surface as unhandled exceptions.",
        [Step("Edit item 999", edit), Step("Delete item 999", delete)]);
}

async Task EvidenceDeleteAsync()
{
    var create = await CreatedStepAsync("Create an item to delete", "Delete me", Forms.Item("Delete me"));
    var id = create["createdId"]!.GetValue<int>();
    var delete = await PostFormAsync($"/Catalog/Delete/{id}", []);
    var picture = await GetAsync(browser, $"/items/{id}/pic");
    Evidence("delete-item",
        "DeleteConfirmed removes the item (hard delete) and redirects to the list. Its picture route returns 404 afterwards.",
        [create, Step($"Delete item {id}", delete, new JsonObject { ["databaseRow"] = await ItemRowAsync(id) }), Step("Get its picture", picture)]);
}

async Task EvidenceReadsAsync()
{
    var paths = new (string Label, string Path)[]
    {
        ("List with the defaults (pageSize=10, pageIndex=0)", "/"),
        ("Second page of five", "/?pageSize=5&pageIndex=1"),
        ("Page past the end", "/?pageSize=5&pageIndex=100"),
        ("pageSize=0 (TotalPages divides by the page size)", "/?pageSize=0"),
        ("Negative pageSize", "/?pageSize=-1"),
        ("Negative pageIndex", "/?pageIndex=-1"),
        ("pageSize*pageIndex overflows Int32", "/?pageSize=2147483647&pageIndex=2"),
        ("Very large pageSize (no upper bound)", "/?pageSize=100000"),
        ("Non-integer pageSize", "/?pageSize=abc"),
        ("Details of item 1", "/Catalog/Details/1"),
        ("Details without an id", "/Catalog/Details"),
        ("Details of an item that does not exist", "/Catalog/Details/999"),
        ("Details with a non-integer id", "/Catalog/Details/abc"),
    };
    var steps = new List<JsonObject>();
    foreach (var (label, path) in paths)
    {
        steps.Add(Step(label, await GetAsync(browser, path)));
    }
    Evidence("catalog-reads",
        "Paged list and details pages of the MVC UI. Page contents are HTML and not stored; the status codes and error titles are.",
        steps.ToArray());
}

async Task WriteLogSampleAsync()
{
    if (!File.Exists(logPath))
    {
        Console.WriteLine("No log file was written.");
        return;
    }
    await using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    stream.Seek(logStart, SeekOrigin.Begin);
    using var reader = new StreamReader(stream);
    var entries = Regex.Split((await reader.ReadToEndAsync()).Replace("\r\n", "\n"), @"\n\n(?=\d{4}-\d{2}-\d{2} )")
        .Where(e => e.Trim().Length > 0)
        .ToList();
    var sample = entries
        .GroupBy(e =>
        {
            var lines = e.Split('\n', 2);
            var header = Regex.Match(lines[0], @"\] \S+ (\S+ \S+) - ").Groups[1].Value;
            var message = lines.Length > 1 ? Regex.Replace(lines[1].Split('?')[0], @"\d+", "#") : "";
            return header + " | " + message;
        })
        .SelectMany(g => g.Take(2))
        .OrderBy(entries.IndexOf);
    Tools.WriteText(Path.Combine(evidenceDir, "log4net-sample.log"), string.Join("\n\n", sample.Select(e => e.TrimEnd())) + "\n");
}

async Task<JsonObject> CaptureInfoAsync()
{
    var server = (await master.QueryAsync(
        "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion, " +
        "CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Edition, " +
        "(SELECT compatibility_level FROM sys.databases WHERE name = @p0) AS CompatibilityLevel", dbName))[0];
    return new JsonObject
    {
        ["capturedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["tool"] = "docs/legacy/capture/capture.cs",
        ["legacySourceCommit"] = Tools.Run("git", ["-C", repo, "rev-parse", "HEAD"], quiet: true).Trim(),
        ["legacySourceModified"] = Tools.Run("git", ["-C", repo, "status", "--porcelain", "--", "src"], quiet: true).Trim().Length > 0,
        ["baseUrl"] = $"http://localhost:{options.Port}",
        ["host"] = new JsonObject
        {
            ["server"] = $"IIS Express {FileVersionInfo.GetVersionInfo(IisExpress.FindExecutable()).ProductVersion}",
            ["pipeline"] = "integrated, CLR v4.0",
            ["os"] = RuntimeInformation.OSDescription,
        },
        ["database"] = new JsonObject
        {
            ["dataSource"] = dbBuilder.DataSource,
            ["name"] = dbName,
            ["sqlServerVersion"] = (string?)server["ProductVersion"],
            ["sqlServerEdition"] = (string?)server["Edition"],
            ["compatibilityLevel"] = Convert.ToInt32(server["CompatibilityLevel"]),
        },
    };
}

async Task<bool> DatabaseExistsAsync() =>
    await master.ScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name = @p0", dbName) > 0;

async Task DropDatabaseAsync()
{
    if (!await DatabaseExistsAsync())
    {
        return;
    }
    var quoted = "[" + dbName.Replace("]", "]]") + "]";
    await master.ExecuteAsync($"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};");
    Console.WriteLine($"Dropped database {quoted}.");
}

sealed record Options(bool Build, bool Reset, bool KeepDb, int Port)
{
    public static Options Parse(string[] args)
    {
        var options = new Options(false, false, false, 52429);
        for (var i = 0; i < args.Length; i++)
        {
            options = args[i] switch
            {
                "--build" => options with { Build = true },
                "--reset" => options with { Reset = true },
                "--keep-db" => options with { KeepDb = true },
                "--port" when i + 1 < args.Length => options with { Port = int.Parse(args[++i]) },
                _ => throw new ArgumentException($"Unknown argument '{args[i]}'. Usage: dotnet run docs/legacy/capture/capture.cs -- [--build] [--reset] [--keep-db] [--port <n>]"),
            };
        }
        return options;
    }
}

static class Forms
{
    public static IEnumerable<(string Name, string Value)> Item(string name, string price = "19.50", string brandId = "2", string typeId = "1",
        string description = "Characterization item", string stock = "10", string restock = "5", string maxStock = "100") =>
    [
        ("Name", name),
        ("Description", description),
        ("CatalogBrandId", brandId),
        ("CatalogTypeId", typeId),
        ("Price", price),
        ("AvailableStock", stock),
        ("RestockThreshold", restock),
        ("MaxStockThreshold", maxStock),
    ];

    public static IEnumerable<(string Name, string Value)> EditFields(JsonNode row) =>
    [
        ("Id", row["Id"]!.ToString()),
        ("Name", row["Name"]!.ToString()),
        ("Description", row["Description"]?.ToString() ?? ""),
        ("CatalogBrandId", row["CatalogBrandId"]!.ToString()),
        ("CatalogTypeId", row["CatalogTypeId"]!.ToString()),
        ("Price", row["Price"]!.ToString()),
        ("PictureFileName", row["PictureFileName"]!.ToString()),
        ("AvailableStock", row["AvailableStock"]!.ToString()),
        ("RestockThreshold", row["RestockThreshold"]!.ToString()),
        ("MaxStockThreshold", row["MaxStockThreshold"]!.ToString()),
    ];
}

sealed record Exchange(int Status, JsonObject Request, JsonObject Response, string Text);

sealed record ContractGroup(string File, string Prefix, string Description)
{
    public JsonObject Exchanges { get; } = new();
}

static class ContractGroups
{
    public static readonly ContractGroup[] All =
    [
        new("brands-list", "brands-get-all", "GET /api/brands: every Accept header variant, path case and trailing slash."),
        new("brands-get-by-id", "brands-get-by-id", "GET /api/brands/{id}: valid, unknown, zero, negative, non-integer, overflowing and dotted IDs, and the id in the query string."),
        new("brands-delete", "brands-delete", "DELETE /api/brands/{id}: the no-op delete, the brand still there afterwards, unknown and non-integer IDs, and no ID."),
        new("brands-other-verbs", "brands-", "POST, PUT, PATCH, HEAD and OPTIONS on the brand routes."),
        new("files", "files-", "GET /api/files: the BinaryFormatter endpoint that the new API retires."),
        new("api-root", "api-", "GET /api (the unreachable CatalogController2 route) and an unknown api/{controller}."),
        new("pictures", "pic-", "GET /items/{catalogItemId}/pic: every seeded picture and the edge cases of the route."),
    ];

    public static ContractGroup For(string name) =>
        All.FirstOrDefault(g => name.StartsWith(g.Prefix, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No contract group for exchange '{name}'.");
}

static class Http
{
    static readonly string[] VolatileHeaders = ["Date", "X-SourceFiles"];

    public static HttpClient CreateClient(int port, bool cookies) =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = cookies,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.None,
        })
        {
            BaseAddress = new Uri($"http://localhost:{port}"),
            Timeout = TimeSpan.FromMinutes(3),
        };

    public static Task<Exchange> SendAsync(HttpClient client, string method, string path) =>
        SendAsync(client, method, path, null, null, null, new Dictionary<string, string>());

    public static async Task<Exchange> SendAsync(HttpClient client, string method, string path,
        (string Name, string Value)[]? headers, HttpContent? content, JsonNode? recordedBody, IReadOnlyDictionary<string, string> knownFiles)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = content };
        var requestHeaders = new JsonObject();
        foreach (var (name, value) in headers ?? [])
        {
            request.Headers.TryAddWithoutValidation(name, value);
            requestHeaders[name] = value;
        }
        if (content?.Headers.ContentType is { } contentType)
        {
            requestHeaders["Content-Type"] = contentType.ToString();
        }

        using var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var body = Body.Describe(bytes, mediaType, knownFiles, out var text);

        var responseHeaders = new JsonObject();
        foreach (var (name, values) in response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated)
                     .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (VolatileHeaders.Contains(name, StringComparer.OrdinalIgnoreCase)
                || ((string?)body["kind"] == "html" && name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                responseHeaders[name] = new JsonArray(values.Select(v => (JsonNode?)Regex.Replace(v, "^([^=]+)=[^;]*", "$1=<redacted>")).ToArray());
            }
            else
            {
                responseHeaders[name] = values.ToString();
            }
        }

        return new Exchange(
            (int)response.StatusCode,
            new JsonObject
            {
                ["method"] = method,
                ["path"] = path,
                ["headers"] = requestHeaders,
                ["body"] = recordedBody?.DeepClone(),
            },
            new JsonObject
            {
                ["status"] = (int)response.StatusCode,
                ["reasonPhrase"] = response.ReasonPhrase,
                ["headers"] = responseHeaders,
                ["body"] = body,
            },
            text);
    }
}

static class Body
{
    public static JsonObject Describe(byte[] bytes, string? mediaType, IReadOnlyDictionary<string, string> knownFiles, out string text)
    {
        text = "";
        if (bytes.Length == 0)
        {
            return new JsonObject { ["kind"] = "empty" };
        }

        // The media type is not enough: /api/files sends a BinaryFormatter stream labelled text/html.
        var isText = mediaType is not null
            && (mediaType.StartsWith("text/") || mediaType.EndsWith("/json") || mediaType.EndsWith("/xml") || mediaType.EndsWith("+json") || mediaType.EndsWith("+xml"))
            && IsUtf8Text(bytes);
        if (!isText)
        {
            var sha = Tools.Sha256(bytes);
            var binary = new JsonObject { ["kind"] = "binary", ["length"] = bytes.Length, ["sha256"] = sha };
            if (knownFiles.TryGetValue(sha, out var file))
            {
                binary["matchesFile"] = file;
            }
            return binary;
        }

        text = new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
        if (mediaType!.Contains("json"))
        {
            try
            {
                return new JsonObject { ["kind"] = "json", ["json"] = JsonNode.Parse(text) };
            }
            catch (JsonException)
            {
                return new JsonObject { ["kind"] = "text", ["text"] = text };
            }
        }
        if (mediaType.Contains("xml"))
        {
            return new JsonObject { ["kind"] = "xml", ["text"] = text };
        }
        if (mediaType == "text/html")
        {
            var html = new JsonObject { ["kind"] = "html", ["title"] = Html.Title(text) };
            if (Html.ExceptionDetails(text) is { } exception)
            {
                html["exception"] = exception;
            }
            var errors = Html.ValidationErrors(text);
            if (errors.Count > 0)
            {
                html["validationErrors"] = errors;
            }
            return html;
        }
        return new JsonObject { ["kind"] = "text", ["text"] = text };
    }

    static bool IsUtf8Text(byte[] bytes)
    {
        try
        {
            var decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return !decoded.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

static class Html
{
    // Every text that Html returns for the capture files goes through Clean, which removes the machine name and user
    // paths. Not AntiForgeryToken, which is posted back exactly as the page gave it.
    public static List<(string Value, string Replacement)> Redactions { get; } = [];
    public static string? Title(string html) =>
        Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase) is { Success: true } m ? Clean(m.Groups[1].Value) : null;

    public static string? ExceptionDetails(string html) =>
        Regex.Match(html, @"Exception Details:\s*</b>(.*?)<br", RegexOptions.Singleline | RegexOptions.IgnoreCase) is { Success: true } m ? Clean(m.Groups[1].Value) : null;

    public static JsonArray ValidationErrors(string html)
    {
        var errors = new JsonArray();
        foreach (Match m in Regex.Matches(html, "<span(?<attrs>[^>]*class=\"[^\"]*field-validation-error[^\"]*\"[^>]*)>(?<message>.*?)</span>", RegexOptions.Singleline))
        {
            var field = Regex.Match(m.Groups["attrs"].Value, "data-valmsg-for=\"([^\"]*)\"").Groups[1].Value;
            errors.Add(new JsonObject { ["field"] = field, ["message"] = Clean(m.Groups["message"].Value) });
        }
        return errors;
    }

    public static string? AntiForgeryToken(string html) =>
        Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"") is { Success: true } m ? m.Groups[1].Value : null;

    static string Clean(string value)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", ""));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        foreach (var (secret, replacement) in Redactions)
        {
            text = text.Replace(secret, replacement, StringComparison.OrdinalIgnoreCase);
        }
        return text.Length > 400 ? text[..400] + "..." : text;
    }
}

sealed class Row(List<(string Name, object? Value)> columns)
{
    public object? this[string name] => columns.First(c => c.Name == name).Value;

    public JsonObject ToJson() => new(columns.Select(c => KeyValuePair.Create(c.Name, Json.FromDb(c.Value))));
}

sealed class Db(string connectionString)
{
    public async Task<List<Row>> QueryAsync(string sql, params object[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await reader.ReadAsync())
        {
            var columns = new List<(string, object?)>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add((reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i)));
            }
            rows.Add(new Row(columns));
        }
        return rows;
    }

    public async Task<T> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)value;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, []);
        await command.ExecuteNonQueryAsync();
    }

    static SqlCommand Command(SqlConnection connection, string sql, object[] parameters)
    {
        var command = new SqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue("@p" + i, parameters[i]);
        }
        return command;
    }
}

static class Schema
{
    public static async Task<(JsonObject Json, string Sql)> CaptureAsync(Db db)
    {
        var collation = await db.ScalarAsync<string>("SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))");
        var tables = await db.QueryAsync(
            "SELECT t.object_id, s.name AS schema_name, t.name, t.is_ms_shipped FROM sys.tables t " +
            "JOIN sys.schemas s ON s.schema_id = t.schema_id ORDER BY s.name, t.name");
        var columns = await db.QueryAsync(
            "SELECT c.object_id, c.column_id, c.name, ty.name AS type_name, c.max_length, c.precision, c.scale, c.is_nullable, " +
            "c.is_identity, CAST(ic.seed_value AS bigint) AS seed_value, CAST(ic.increment_value AS bigint) AS increment_value, " +
            "c.collation_name, dc.name AS default_name, dc.definition AS default_definition, c.is_computed " +
            "FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id " +
            "LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id " +
            "LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id " +
            "WHERE c.object_id IN (SELECT object_id FROM sys.tables) ORDER BY c.object_id, c.column_id");
        var indexes = await db.QueryAsync(
            "SELECT i.object_id, i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition " +
            "FROM sys.indexes i WHERE i.object_id IN (SELECT object_id FROM sys.tables) AND i.type > 0 ORDER BY i.object_id, i.name");
        var indexColumns = await db.QueryAsync(
            "SELECT ic.object_id, ic.index_id, c.name, ic.is_descending_key, ic.is_included_column " +
            "FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "WHERE ic.object_id IN (SELECT object_id FROM sys.tables) " +
            "ORDER BY ic.object_id, ic.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id");
        var foreignKeys = await db.QueryAsync(
            "SELECT fk.object_id, fk.name, fk.parent_object_id, OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ref_schema, " +
            "OBJECT_NAME(fk.referenced_object_id) AS ref_table, fk.delete_referential_action_desc, fk.update_referential_action_desc, " +
            "fk.is_disabled, fk.is_not_trusted FROM sys.foreign_keys fk ORDER BY fk.name");
        var foreignKeyColumns = await db.QueryAsync(
            "SELECT fkc.constraint_object_id, pc.name AS column_name, rc.name AS ref_column_name FROM sys.foreign_key_columns fkc " +
            "JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id " +
            "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id " +
            "ORDER BY fkc.constraint_object_id, fkc.constraint_column_id");
        var sequences = await db.QueryAsync(
            "SELECT s.name AS schema_name, seq.name, TYPE_NAME(seq.user_type_id) AS type_name, CAST(seq.start_value AS bigint) AS start_value, " +
            "CAST(seq.increment AS bigint) AS increment, CAST(seq.minimum_value AS bigint) AS minimum_value, " +
            "CAST(seq.maximum_value AS bigint) AS maximum_value, seq.is_cycling, seq.is_cached, seq.cache_size " +
            "FROM sys.sequences seq JOIN sys.schemas s ON s.schema_id = seq.schema_id ORDER BY s.name, seq.name");
        var objectCounts = await db.QueryAsync(
            "SELECT type_desc, COUNT(*) AS count FROM sys.objects WHERE is_ms_shipped = 0 OR object_id IN (SELECT object_id FROM sys.tables) " +
            "GROUP BY type_desc ORDER BY type_desc");

        var tableNames = tables.ToDictionary(t => Convert.ToInt32(t["object_id"]), t => $"[{t["schema_name"]}].[{t["name"]}]");
        var sql = new StringBuilder();
        sql.Append("-- Legacy catalog schema, captured from the database that eShopLegacyMVC creates on its first start\n");
        sql.Append("-- (EF6 CreateDatabaseIfNotExists plus Models/Infrastructure/*.Sequence.sql).\n");
        sql.Append("-- Generated by docs/legacy/capture/capture.cs. Do not edit by hand.\n");
        sql.Append($"-- Database collation: {collation}. Objects that differ from it carry an explicit COLLATE.\n");
        sql.Append("-- One batch without GO separators, so it can be executed as a single command.\n\n");

        var sequencesJson = new JsonArray();
        foreach (var s in sequences)
        {
            var cache = (bool)s["is_cached"]! ? (s["cache_size"] is null ? "CACHE" : $"CACHE {s["cache_size"]}") : "NO CACHE";
            sql.Append($"CREATE SEQUENCE [{s["schema_name"]}].[{s["name"]}] AS {s["type_name"]} START WITH {s["start_value"]} INCREMENT BY {s["increment"]} " +
                       $"MINVALUE {s["minimum_value"]} MAXVALUE {s["maximum_value"]} {((bool)s["is_cycling"]! ? "CYCLE" : "NO CYCLE")} {cache};\n");
            sequencesJson.Add(new JsonObject
            {
                ["schema"] = (string?)s["schema_name"],
                ["name"] = (string?)s["name"],
                ["type"] = (string?)s["type_name"],
                ["startValue"] = (long)s["start_value"]!,
                ["increment"] = (long)s["increment"]!,
                ["minValue"] = (long)s["minimum_value"]!,
                ["maxValue"] = (long)s["maximum_value"]!,
                ["cycle"] = (bool)s["is_cycling"]!,
                ["cached"] = (bool)s["is_cached"]!,
                ["cacheSize"] = s["cache_size"] is null ? null : Convert.ToInt32(s["cache_size"]),
            });
        }
        sql.Append('\n');

        var tablesJson = new JsonArray();
        var indexSql = new StringBuilder();
        var foreignKeySql = new StringBuilder();
        foreach (var t in tables)
        {
            var objectId = Convert.ToInt32(t["object_id"]);
            var tableName = tableNames[objectId];
            var columnsJson = new JsonArray();
            var columnLines = new List<string>();
            foreach (var c in columns.Where(c => Convert.ToInt32(c["object_id"]) == objectId))
            {
                var storeType = StoreType((string)c["type_name"]!, Convert.ToInt32(c["max_length"]), Convert.ToInt32(c["precision"]), Convert.ToInt32(c["scale"]));
                var identity = (bool)c["is_identity"]!;
                var columnCollation = (string?)c["collation_name"];
                var line = $"    [{c["name"]}] {storeType}";
                if (columnCollation is not null && columnCollation != collation)
                {
                    line += $" COLLATE {columnCollation}";
                }
                if (identity)
                {
                    line += $" IDENTITY({c["seed_value"]},{c["increment_value"]})";
                }
                line += (bool)c["is_nullable"]! ? " NULL" : " NOT NULL";
                if (c["default_definition"] is string def)
                {
                    line += $" CONSTRAINT [{c["default_name"]}] DEFAULT {def}";
                }
                columnLines.Add(line);
                columnsJson.Add(new JsonObject
                {
                    ["name"] = (string?)c["name"],
                    ["ordinal"] = Convert.ToInt32(c["column_id"]),
                    ["storeType"] = storeType,
                    ["nullable"] = (bool)c["is_nullable"]!,
                    ["identity"] = identity ? new JsonObject { ["seed"] = (long)c["seed_value"]!, ["increment"] = (long)c["increment_value"]! } : null,
                    ["collation"] = columnCollation,
                    ["default"] = (string?)c["default_definition"],
                    ["computed"] = (bool)c["is_computed"]!,
                });
            }

            JsonObject? primaryKeyJson = null;
            var indexesJson = new JsonArray();
            foreach (var i in indexes.Where(i => Convert.ToInt32(i["object_id"]) == objectId))
            {
                var indexId = Convert.ToInt32(i["index_id"]);
                var cols = indexColumns.Where(ic => Convert.ToInt32(ic["object_id"]) == objectId && Convert.ToInt32(ic["index_id"]) == indexId).ToList();
                var keyCols = cols.Where(ic => !(bool)ic["is_included_column"]!).ToList();
                var included = cols.Where(ic => (bool)ic["is_included_column"]!).Select(ic => (string)ic["name"]!).ToList();
                var clustered = (string)i["type_desc"]! == "CLUSTERED";
                var keyList = string.Join(", ", keyCols.Select(ic => $"[{ic["name"]}] {((bool)ic["is_descending_key"]! ? "DESC" : "ASC")}"));
                var keyJson = new JsonArray(keyCols.Select(ic => (JsonNode?)new JsonObject { ["name"] = (string?)ic["name"], ["descending"] = (bool)ic["is_descending_key"]! }).ToArray());
                if ((bool)i["is_primary_key"]!)
                {
                    columnLines.Add($"    CONSTRAINT [{i["name"]}] PRIMARY KEY {(clustered ? "CLUSTERED" : "NONCLUSTERED")} ({keyList})");
                    primaryKeyJson = new JsonObject { ["name"] = (string?)i["name"], ["clustered"] = clustered, ["columns"] = keyJson };
                    continue;
                }
                if ((bool)i["is_unique_constraint"]!)
                {
                    columnLines.Add($"    CONSTRAINT [{i["name"]}] UNIQUE {(clustered ? "CLUSTERED" : "NONCLUSTERED")} ({keyList})");
                }
                else
                {
                    indexSql.Append($"CREATE {((bool)i["is_unique"]! ? "UNIQUE " : "")}{(clustered ? "CLUSTERED" : "NONCLUSTERED")} INDEX [{i["name"]}] ON {tableName} ({keyList})" +
                                    (included.Count > 0 ? $" INCLUDE ({string.Join(", ", included.Select(n => $"[{n}]"))})" : "") +
                                    (i["filter_definition"] is string filter ? $" WHERE {filter}" : "") + ";\n");
                }
                indexesJson.Add(new JsonObject
                {
                    ["name"] = (string?)i["name"],
                    ["clustered"] = clustered,
                    ["unique"] = (bool)i["is_unique"]!,
                    ["uniqueConstraint"] = (bool)i["is_unique_constraint"]!,
                    ["columns"] = keyJson,
                    ["included"] = new JsonArray(included.Select(n => (JsonNode?)n).ToArray()),
                    ["filter"] = (string?)i["filter_definition"],
                });
            }

            var foreignKeysJson = new JsonArray();
            foreach (var fk in foreignKeys.Where(fk => Convert.ToInt32(fk["parent_object_id"]) == objectId))
            {
                var fkCols = foreignKeyColumns.Where(fc => Convert.ToInt32(fc["constraint_object_id"]) == Convert.ToInt32(fk["object_id"])).ToList();
                var onDelete = ((string)fk["delete_referential_action_desc"]!).Replace('_', ' ');
                var onUpdate = ((string)fk["update_referential_action_desc"]!).Replace('_', ' ');
                foreignKeySql.Append($"ALTER TABLE {tableName} ADD CONSTRAINT [{fk["name"]}] FOREIGN KEY ({string.Join(", ", fkCols.Select(fc => $"[{fc["column_name"]}]"))}) " +
                                     $"REFERENCES [{fk["ref_schema"]}].[{fk["ref_table"]}] ({string.Join(", ", fkCols.Select(fc => $"[{fc["ref_column_name"]}]"))})" +
                                     (onDelete == "NO ACTION" ? "" : $" ON DELETE {onDelete}") +
                                     (onUpdate == "NO ACTION" ? "" : $" ON UPDATE {onUpdate}") + ";\n");
                foreignKeysJson.Add(new JsonObject
                {
                    ["name"] = (string?)fk["name"],
                    ["columns"] = new JsonArray(fkCols.Select(fc => (JsonNode?)(string?)fc["column_name"]).ToArray()),
                    ["principalTable"] = $"{fk["ref_schema"]}.{fk["ref_table"]}",
                    ["principalColumns"] = new JsonArray(fkCols.Select(fc => (JsonNode?)(string?)fc["ref_column_name"]).ToArray()),
                    ["onDelete"] = onDelete,
                    ["onUpdate"] = onUpdate,
                    ["enabled"] = !(bool)fk["is_disabled"]!,
                    ["trusted"] = !(bool)fk["is_not_trusted"]!,
                });
            }

            if ((bool)t["is_ms_shipped"]!)
            {
                sql.Append($"-- EF6 marks {tableName} as a system object (is_ms_shipped = 1).\n");
            }
            sql.Append($"CREATE TABLE {tableName} (\n{string.Join(",\n", columnLines)}\n);\n\n");
            tablesJson.Add(new JsonObject
            {
                ["schema"] = (string?)t["schema_name"],
                ["name"] = (string?)t["name"],
                ["systemObject"] = (bool)t["is_ms_shipped"]!,
                ["columns"] = columnsJson,
                ["primaryKey"] = primaryKeyJson,
                ["indexes"] = indexesJson,
                ["foreignKeys"] = foreignKeysJson,
            });
        }
        sql.Append(indexSql).Append('\n').Append(foreignKeySql);

        var json = new JsonObject
        {
            ["source"] = "Captured by docs/legacy/capture/capture.cs from the database eShopLegacyMVC creates on its first start.",
            ["collation"] = collation,
            ["objectCounts"] = new JsonObject(objectCounts.Select(o => KeyValuePair.Create((string)o["type_desc"]!, (JsonNode?)Convert.ToInt32(o["count"])))),
            ["sequences"] = sequencesJson,
            ["tables"] = tablesJson,
        };
        return (json, sql.ToString());
    }

    public static async Task<JsonObject> CaptureSeedDataAsync(Db db, string edmxPath, string repo)
    {
        var seed = new JsonObject
        {
            ["source"] = "Rows and sequence values right after the legacy app created and seeded a fresh database (UseCustomizationData=false).",
        };
        foreach (var table in new[] { "CatalogBrand", "CatalogType", "Catalog" })
        {
            var rows = await db.QueryAsync($"SELECT * FROM [dbo].[{table}] ORDER BY Id");
            seed[table] = new JsonArray(rows.Select(r => (JsonNode?)r.ToJson()).ToArray());
        }

        var history = new JsonArray();
        foreach (var row in await db.QueryAsync("SELECT MigrationId, ContextKey, ProductVersion, Model FROM [dbo].[__MigrationHistory] ORDER BY MigrationId"))
        {
            var model = (byte[])row["Model"]!;
            await using var gzip = new GZipStream(new MemoryStream(model), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var edmx = XDocument.Parse(await reader.ReadToEndAsync());
            Tools.WriteText(edmxPath, edmx.ToString().Replace("\r\n", "\n") + "\n");
            history.Add(new JsonObject
            {
                ["MigrationId"] = (string?)row["MigrationId"],
                ["ContextKey"] = (string?)row["ContextKey"],
                ["ProductVersion"] = (string?)row["ProductVersion"],
                ["Model"] = new JsonObject
                {
                    ["length"] = model.Length,
                    ["format"] = "gzip-compressed EDMX",
                    ["decompressedTo"] = Path.GetRelativePath(repo, edmxPath).Replace('\\', '/'),
                },
            });
        }
        seed["__MigrationHistory"] = history;

        var sequences = await db.QueryAsync("SELECT name, CAST(current_value AS bigint) AS current_value FROM sys.sequences ORDER BY name");
        seed["sequenceCurrentValues"] = new JsonObject(sequences.Select(s => KeyValuePair.Create((string)s["name"]!, (JsonNode?)(long)s["current_value"]!)));
        return seed;
    }

    static string StoreType(string type, int maxLength, int precision, int scale) => type switch
    {
        "nvarchar" or "nchar" => $"{type}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})",
        "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength == -1 ? "max" : maxLength.ToString())})",
        "decimal" or "numeric" => $"{type}({precision},{scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{type}({scale})",
        _ => type,
    };
}

sealed class IisExpress : IAsyncDisposable
{
    readonly Process process;
    readonly StringBuilder output = new();

    IisExpress(Process process) => this.process = process;

    public static string FindExecutable() =>
        new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), "IIS Express", "iisexpress.exe"))
            .FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException("IIS Express is not installed.");

    public static async Task<IisExpress> StartAsync(string appDir, int port)
    {
        Tools.Require(!await Tools.IsListeningAsync(port), $"Port {port} is already in use. Stop the other server or pass --port.");
        var info = new ProcessStartInfo(FindExecutable())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add($"/path:{appDir}");
        info.ArgumentList.Add($"/port:{port}");
        info.ArgumentList.Add("/clr:v4.0");
        info.ArgumentList.Add("/systray:false");
        var iis = new IisExpress(Process.Start(info)!);
        iis.process.OutputDataReceived += (_, e) => { lock (iis.output) { iis.output.AppendLine(e.Data); } };
        iis.process.ErrorDataReceived += (_, e) => { lock (iis.output) { iis.output.AppendLine(e.Data); } };
        iis.process.BeginOutputReadLine();
        iis.process.BeginErrorReadLine();

        for (var attempt = 0; attempt < 60 && !await Tools.IsListeningAsync(port); attempt++)
        {
            if (iis.process.HasExited)
            {
                throw new InvalidOperationException($"IIS Express exited with code {iis.process.ExitCode}:\n{iis.output}");
            }
            await Task.Delay(500);
        }
        if (!await Tools.IsListeningAsync(port))
        {
            await iis.DisposeAsync();
            throw new InvalidOperationException($"IIS Express did not start listening on port {port}:\n{iis.output}");
        }
        Console.WriteLine($"IIS Express is listening on http://localhost:{port}/ (pid {iis.process.Id}).");
        return iis;
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process.Dispose();
        Console.WriteLine("IIS Express stopped.");
    }
}

static class Json
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string path, JsonNode node) => Tools.WriteText(path, node.ToJsonString(Options) + "\n");

    public static JsonNode? FromDb(object? value) => value switch
    {
        null => null,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        decimal d => d,
        bool b => b,
        string s => s,
        DateTime dt => dt.ToString("O"),
        byte[] bytes => new JsonObject { ["length"] = bytes.Length, ["sha256"] = Tools.Sha256(bytes) },
        _ => value.ToString(),
    };
}

static class Tools
{
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            Console.Error.WriteLine(message);
            Environment.Exit(1);
        }
    }

    public static string FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "eShopLegacyMVC.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Run this from a checkout of the legacy-final tag (eShopLegacyMVC.sln not found).");
    }

    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Sha256File(string path) => Sha256(File.ReadAllBytes(path));

    public static async Task<bool> IsListeningAsync(int port)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1));
            return true;
        }
        catch (Exception e) when (e is SocketException or TimeoutException)
        {
            return false;
        }
    }

    public static string Run(string fileName, IEnumerable<string> arguments, bool quiet = false)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (!quiet)
        {
            Console.Write(stdout);
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
        return stdout;
    }

    public static void BuildLegacy(string repo)
    {
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        var msbuild = Run(vswhere, ["-latest", "-prerelease", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe"], quiet: true)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? throw new InvalidOperationException("MSBuild was not found by vswhere.");
        Run(msbuild, [Path.Combine(repo, "eShopLegacyMVC.sln"), "-restore", "-p:Configuration=Debug", "-m", "-nologo", "-v:minimal"]);
    }
}
