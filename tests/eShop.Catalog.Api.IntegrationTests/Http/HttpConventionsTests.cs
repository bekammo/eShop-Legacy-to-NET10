using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using eShop.Catalog.Api.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eShop.Catalog.Api.IntegrationTests.Http;

public sealed class HttpConventionsTests
{
    private const string Detail = "GET /gone has been retired.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string?> AcceptHeaders =>
    [
        (string?)null,
        "application/json",
        "*/*",
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "application/xml",
        "text/html",
        "image/png",
    ];

    public static TheoryData<string> EnvironmentNames => [Environments.Development, Environments.Production];

    [Theory]
    [MemberData(nameof(AcceptHeaders))]
    public async Task Results_are_PascalCase_JSON_whatever_the_Accept_header(string? accept)
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/brand", accept);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("""{"Id":1,"Brand":"Azure"}""", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Theory]
    [MemberData(nameof(AcceptHeaders))]
    public async Task Route_that_matches_nothing_is_a_404_problem_whatever_the_Accept_header(string? accept)
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/no-such-route", accept);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "https://tools.ietf.org/html/rfc9110#section-15.5.5", "Not Found");
    }

    [Fact]
    public async Task Error_status_that_an_endpoint_returns_without_a_body_becomes_a_problem()
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/brand/missing", accept: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "https://tools.ietf.org/html/rfc9110#section-15.5.5", "Not Found");
    }

    [Fact]
    public async Task Method_that_the_route_does_not_allow_is_a_405_problem_with_the_Allow_header()
    {
        await using var app = await StartAsync(Environments.Production);
        using var client = app.GetTestClient();

        using var response = await client.DeleteAsync("/brand", CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "https://tools.ietf.org/html/rfc9110#section-15.5.6", "Method Not Allowed");
        Assert.Equal(["GET"], response.Content.Headers.Allow);
    }

    [Theory]
    [MemberData(nameof(EnvironmentNames))]
    public async Task Parameter_that_does_not_bind_is_a_400_problem_in_every_environment(string environment)
    {
        await using var app = await StartAsync(environment);

        using var response = await GetAsync(app, "/brands/abc", accept: null);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "https://tools.ietf.org/html/rfc9110#section-15.5.1", "Bad Request");
    }

    [Fact]
    public async Task Parameter_that_breaks_a_validation_attribute_is_a_400_problem_with_its_errors()
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/page?pageSize=0", accept: "application/xml");

        var problem = await AssertProblemAsync(
            response, HttpStatusCode.BadRequest, "https://tools.ietf.org/html/rfc9110#section-15.5.1", "One or more validation errors occurred.");
        Assert.Equal("""{"pageSize":["The field pageSize must be between 1 and 100."]}""", problem.GetProperty("errors").GetRawText());
    }

    [Theory]
    [MemberData(nameof(EnvironmentNames))]
    public async Task Exception_is_a_500_problem_that_does_not_show_the_exception(string environment)
    {
        await using var app = await StartAsync(environment);

        using var response = await GetAsync(app, "/throw", accept: "text/html");

        var problem = await AssertProblemAsync(
            response, HttpStatusCode.InternalServerError, "https://tools.ietf.org/html/rfc9110#section-15.6.1", "An error occurred while processing your request.");
        Assert.Equal(["type", "title", "status", "traceId"], problem.EnumerateObject().Select(static member => member.Name));
    }

    [Fact]
    public async Task Exception_in_routing_is_a_500_problem()
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/conflict", accept: null);

        await AssertProblemAsync(
            response, HttpStatusCode.InternalServerError, "https://tools.ietf.org/html/rfc9110#section-15.6.1", "An error occurred while processing your request.");
    }

    [Theory]
    [MemberData(nameof(AcceptHeaders))]
    public async Task Problem_that_an_endpoint_returns_keeps_its_detail_and_gets_the_trace_id(string? accept)
    {
        await using var app = await StartAsync(Environments.Production);

        using var response = await GetAsync(app, "/gone", accept);

        var problem = await AssertProblemAsync(response, HttpStatusCode.Gone, "https://tools.ietf.org/html/rfc9110#section-15.5.11", "Gone");
        Assert.Equal(Detail, problem.GetProperty("detail").GetString());
    }

    private static async Task<WebApplication> StartAsync(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCatalogHttp();

        var app = builder.Build();
        app.UseCatalogErrorHandling();
        app.MapGet("/brand", static () => TypedResults.Ok(new Sample(1, "Azure")));
        app.MapGet("/brand/missing", static () => TypedResults.NotFound());
        app.MapGet("/brands/{id}", static (int id) => TypedResults.Ok(new Sample(id, "Azure")));
        app.MapGet("/page", static ([Range(1, 100)] int pageSize) => TypedResults.Ok(pageSize));
        app.MapGet("/throw", static IResult () => throw new InvalidOperationException("Thrown by a test."));
        app.MapGet("/gone", static () => TypedResults.Problem(statusCode: StatusCodes.Status410Gone, detail: Detail));
#pragma warning disable ASP0022 // Route conflict detected between route handlers: on purpose.
        app.MapGet("/conflict", static () => TypedResults.Ok());
        app.MapGet("/conflict", static () => TypedResults.Ok());
#pragma warning restore ASP0022

        await app.StartAsync(CancellationToken);
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path, string? accept)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    // traceId is only checked as non-empty: this host has no logging provider, so ASP.NET Core starts no Activity
    // and the trace ID is the TraceIdentifier, not the W3C form.
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(new MediaTypeHeaderValue("application/problem+json"), response.Content.Headers.ContentType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var problem = document.RootElement.Clone();
        Assert.Equal(type, problem.GetProperty("type").GetString());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        return problem;
    }

    private sealed record Sample(int Id, string Brand);
}
