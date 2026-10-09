using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.IntegrationTests.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Http;

[Trait("Category", "Docker")]
public sealed class RequestCancellationTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const string RequestLoggingMiddleware = "Serilog.AspNetCore.RequestLoggingMiddleware";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/api/brands")]
    [InlineData("GET", "/api/brands/1")]
    [InlineData("DELETE", "/api/brands/1")]
    [InlineData("GET", "/api/items")]
    [InlineData("GET", "/api/items/1")]
    [InlineData("POST", "/api/items")]
    [InlineData("PUT", "/api/items/1")]
    [InlineData("DELETE", "/api/items/1")]
    [InlineData("GET", "/items/1/pic")]
    [InlineData("GET", "/api/types")]
    [InlineData("GET", "/health/ready")]
    public async Task Client_that_goes_away_cancels_the_database_command_and_the_request_is_not_an_Error(string method, string path)
    {
        var firstCommand = new SlowFirstCommand();
        var logFile = Path.Combine(Path.GetDirectoryName(factory.LogFilePath)!, $"{Guid.NewGuid():N}.log");
        // The Debug minimum level is for /health/ready: its request event is Debug, and the test waits for it.
        await using var host = factory.WithWebHostBuilder(builder =>
            CatalogApiFactory.UseLogFile(builder, logFile)
                .ConfigureAppConfiguration(static (_, configuration) =>
                    configuration.AddInMemoryCollection([new("Serilog:MinimumLevel:Default", "Debug")]))
                .ConfigureTestServices(services =>
                    services.ConfigureDbContext<CatalogDbContext>(options => options.AddInterceptors(firstCommand))));
        using var client = host.CreateClient();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        using var request = Request(method, path, traceId);
        using var goingAway = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var sending = client.SendAsync(request, goingAway.Token);
        var (commandToken, session) = await firstCommand.Started.WaitAsync(Wait, CancellationToken);
        await WaitUntilTheServerRunsTheCommandAsync(session);
        await goingAway.CancelAsync();

        Assert.True(commandToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        var requestEvent = await LogFile.WaitForEventAsync(logFile, logEvent =>
            LogFile.String(logEvent, "@tr") == traceId && LogFile.String(logEvent, "SourceContext") == RequestLoggingMiddleware);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, requestEvent.GetProperty("StatusCode").GetInt32());
        Assert.DoesNotContain(LogFile.Events(logFile), static logEvent => LogFile.String(logEvent, "@l") is "Error" or "Fatal");
    }

    private static HttpRequestMessage Request(string method, string path, string traceId)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        request.Headers.Authorization = AccessTokens.Writer;
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new JsonObject
            {
                ["Name"] = "Test mug",
                ["Price"] = 9.99m,
                ["CatalogTypeId"] = 1,
                ["CatalogBrandId"] = 2,
                ["AvailableStock"] = 10,
                ["RestockThreshold"] = 2,
                ["MaxStockThreshold"] = 50,
                ["OnReorder"] = false,
            });
        }

        return request;
    }

    // SqlClient cancels a command on the server only once it has sent it: cancelling earlier lets the command run
    // to its end, so the test waits until the server is running the WAITFOR.
    private async Task WaitUntilTheServerRunsTheCommandAsync(int session)
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var waits = new SqlCommand("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @session AND wait_type = 'WAITFOR'", connection);
        waits.Parameters.AddWithValue("@session", session);
        var waited = Stopwatch.StartNew();
        while ((int)(await waits.ExecuteScalarAsync(CancellationToken))! == 0)
        {
            Assert.True(waited.Elapsed < Wait, "The request's first command did not reach SQL Server.");
            await Task.Delay(20, CancellationToken);
        }
    }

    private sealed class SlowFirstCommand : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<(CancellationToken Token, int Session)> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(CancellationToken Token, int Session)> Started => _started.Task;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            SlowDownIfFirst(command, cancellationToken);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SlowDownIfFirst(command, cancellationToken);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void SlowDownIfFirst(DbCommand command, CancellationToken cancellationToken)
        {
            if (_started.TrySetResult((cancellationToken, ((SqlConnection)command.Connection!).ServerProcessId)))
            {
                command.CommandText = $"WAITFOR DELAY '{Wait:hh\\:mm\\:ss}'; {command.CommandText}";
            }
        }
    }
}
