using System.Text.RegularExpressions;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.UnitTests.Data;

public sealed partial class LegacyBaselineScriptTests
{
    private static readonly string Script = LegacyFiles.ReadText("baseline.sql");

    [Fact]
    public void Baseline_records_the_first_migration()
    {
        using var context = CreateContext();

        Assert.Equal([context.Database.GetMigrations().First()], MigrationId().Matches(Script).Select(m => m.Value).Distinct());
    }

    [Fact]
    public void Baseline_creates_the_history_table_that_EF_Core_creates()
    {
        using var context = CreateContext();

        var create = context.GetService<IHistoryRepository>().GetCreateScript();

        Assert.Contains(Normalized(create), Normalized(Script), StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_is_one_batch_that_sqlcmd_and_SqlClient_run_alike()
    {
        Assert.DoesNotMatch(GoLine(), Script);
        Assert.DoesNotMatch(SqlcmdCommandLine(), Script);
        Assert.DoesNotContain("$(", Script, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", Script, StringComparison.Ordinal);
    }

    private static CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer();
        return new CatalogDbContext(options.Options);
    }

    private static string Normalized(string sql) => Whitespace().Replace(sql, " ").Trim();

    [GeneratedRegex(@"\d{14}_\w+")]
    private static partial Regex MigrationId();

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    [GeneratedRegex(@"^\s*(:|!!|(QUIT|EXIT|RESET|ED)\b)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SqlcmdCommandLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
