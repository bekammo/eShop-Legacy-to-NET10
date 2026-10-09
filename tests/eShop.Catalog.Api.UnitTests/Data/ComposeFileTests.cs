using System.Text.RegularExpressions;
using eShop.Catalog.Api.Tests;

namespace eShop.Catalog.Api.UnitTests.Data;

public sealed partial class ComposeFileTests
{
    private static readonly string[] Lines =
        [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "compose.yaml"))
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))];

    [Fact]
    public void Compose_runs_the_sql_server_image_of_the_integration_tests()
    {
        Assert.Equal([SqlServerImage.Name], Values("image"));
    }

    [Fact]
    public void Compose_takes_the_sa_password_from_the_environment()
    {
        var password = Assert.Single(Values("MSSQL_SA_PASSWORD"));

        Assert.Matches(RequiredVariable(), password);
        Assert.Empty(Values("SA_PASSWORD"));
    }

    [Fact]
    public void Compose_publishes_sql_server_on_loopback_only()
    {
        var ports = Items("ports");

        Assert.NotEmpty(ports);
        Assert.All(ports, static port => Assert.StartsWith("127.0.0.1:", port, StringComparison.Ordinal));
    }

    private static List<string> Values(string key) =>
        [.. Lines.Select(static line => KeyValue().Match(line))
            .Where(match => match.Success && match.Groups["key"].Value == key)
            .Select(static match => Unquoted(match.Groups["value"].Value))];

    private static List<string> Items(string key)
    {
        var items = new List<string>();
        int? listIndent = null;
        foreach (var line in Lines)
        {
            var indent = line.Length - line.TrimStart().Length;
            if (listIndent is { } parentIndent && indent >= parentIndent && ListItem().Match(line) is { Success: true } item)
            {
                items.Add(Unquoted(item.Groups["value"].Value));
                continue;
            }

            listIndent = null;
            var match = KeyValue().Match(line);
            if (match.Success && match.Groups["key"].Value == key)
            {
                if (match.Groups["value"].Length == 0)
                {
                    listIndent = indent;
                }
                else
                {
                    items.Add(match.Groups["value"].Value);
                }
            }
        }

        return items;
    }

    private static string Unquoted(string value) =>
        value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z_][\w.-]*):(?:\s+(?<value>.*?))?\s*$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"^\s*-\s+(?<value>.*?)\s*$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\$\{MSSQL_SA_PASSWORD:\?[^}]*\}$")]
    private static partial Regex RequiredVariable();
}
