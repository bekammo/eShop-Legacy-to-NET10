using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace eShop.Catalog.Api.IntegrationTests.Data;

internal static partial class SqlScripts
{
    public static async Task RunAsync(SqlConnection connection, string script, CancellationToken cancellationToken)
    {
        foreach (var batch in BatchSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();
}
