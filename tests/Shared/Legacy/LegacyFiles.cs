using System.Text.Json.Nodes;

namespace eShop.Catalog.Api.Tests.Legacy;

internal static class LegacyFiles
{
    public static string Path(string fileName) => System.IO.Path.Combine(AppContext.BaseDirectory, "Legacy", fileName);

    public static string ReadText(string fileName) => File.ReadAllText(Path(fileName));

    public static JsonNode ReadJson(string fileName) => JsonNode.Parse(ReadText(fileName))!;
}
