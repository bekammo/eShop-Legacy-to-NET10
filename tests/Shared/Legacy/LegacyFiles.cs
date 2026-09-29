using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

// The characterization files of docs/legacy, which tests/Directory.Build.props copies into the test
// output's Legacy folder.
internal static class LegacyFiles
{
    public static JsonNode ReadJson(string fileName) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Legacy", fileName)))!;
}
