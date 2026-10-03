using System.Data.Common;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.IntegrationTests.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Http;

// A client that goes away cancels the database command that its request is waiting for (ADR-0027): each endpoint passes
// the request's RequestAborted to EF Core, which passes it to SqlClient. SqlClient reports the cancelled command as a
// SqlException, and the request still ends as ASP.NET Core ends an aborted request: a 499, which is not an Error
// (ADR-0019).
public sealed class RequestCancellationTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const string RequestLoggingMiddleware = "Serilog.AspNetCore.RequestLoggingMiddleware";

    // How long the first command waits on the server before it runs, and how long the test waits for the command and
    // for its wait to start. A request that ignores the client's going away ends by itself after it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Every endpoint that reaches the database. The test holds the first command that each one sends.
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
        // Debug, so that the readiness probe's request event is written too.
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

        // The test server cancels RequestAborted as the client cancels, so the command's token is cancelled by now.
        Assert.True(commandToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        var requestEvent = await LogFile.WaitForEventAsync(logFile, logEvent =>
            LogFile.String(logEvent, "@tr") == traceId && LogFile.String(logEvent, "SourceContext") == RequestLoggingMiddleware);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, requestEvent.GetProperty("StatusCode").GetInt32());
        // The request event is written last, so an Error of the request, such as the exception handler's, is in the
        // file by now.
        Assert.DoesNotContain(LogFile.Events(logFile), static logEvent => LogFile.String(logEvent, "@l") is "Error" or "Fatal");
    }

    private static HttpRequestMessage Request(string method, string path, string traceId)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        if (method is "POST" or "PUT")
        {
            // A valid item, so that the request reaches the database.
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

    // Until SQL Server runs the WAITFOR of the first command, on that command's session. SqlClient cancels a command on
    // the server only once it has sent it: a token cancelled while it is still sending the command leaves the command to
    // run to its end.
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

    // Puts a WAITFOR before the request's first command, so that the command is still running on the server when the
    // test cancels, as when a client goes away during a slow query. The other commands run as they are.
    private sealed class SlowFirstCommand : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<(CancellationToken Token, int Session)> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The token that EF Core gives the first command, and the server session that runs it.
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
