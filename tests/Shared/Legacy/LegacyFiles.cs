using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

// The files of docs/legacy that tests/Directory.Build.props copies into the test output's Legacy
// folder: the Stage 1.2 characterization data and the Stage 4.3 baseline script.
internal static class LegacyFiles
{
    public static string Path(string fileName) => System.IO.Path.Combine(AppContext.BaseDirectory, "Legacy", fileName);

    public static string ReadText(string fileName) => File.ReadAllText(Path(fileName));

    public static JsonNode ReadJson(string fileName) => JsonNode.Parse(ReadText(fileName))!;
}
