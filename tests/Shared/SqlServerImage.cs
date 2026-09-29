namespace eShop.Catalog.Api.Tests;

// The SQL Server image of the integration tests, which compose.yaml runs for local development too
// (ADR-0011, ADR-0014). Pinned, so that every machine and CI run the same server and an upgrade is a
// deliberate change. SQL Server 2025 is the version the legacy capture ran on.
internal static class SqlServerImage
{
    public const string Name = "mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04";
}
