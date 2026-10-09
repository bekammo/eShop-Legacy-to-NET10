using System.Diagnostics;
using System.Text.Json;
using eShop.Catalog.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace eShop.Catalog.Api.UnitTests.Http;

public sealed class ProblemJsonWriterTests
{
    [Fact]
    public async Task Problem_gets_the_status_type_and_title_of_the_response_and_the_trace_id()
    {
        using var activity = new Activity("request").Start();
        var request = Request(StatusCodes.Status404NotFound);

        await Writer().WriteAsync(Context(request, new ProblemDetails()));

        Assert.Equal("application/problem+json", request.Response.ContentType);
        var problem = Body(request);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", problem.GetProperty("type").GetString());
        Assert.Equal("Not Found", problem.GetProperty("title").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal(activity.Id, problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Problem_written_before_gets_the_trace_id_of_the_current_request()
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status410Gone };
        var writer = Writer();
        using (new Activity("first request").Start())
        {
            await writer.WriteAsync(Context(Request(StatusCodes.Status410Gone), problem));
        }

        using var second = new Activity("second request").Start();
        var request = Request(StatusCodes.Status410Gone);

        await writer.WriteAsync(Context(request, problem));

        Assert.Equal(second.Id, Body(request).GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Request_that_the_client_aborted_does_not_make_the_writer_throw()
    {
        var request = Request(StatusCodes.Status500InternalServerError);
        request.RequestAborted = new CancellationToken(canceled: true);

        await Writer().WriteAsync(Context(request, new ProblemDetails()));
    }

    [Fact]
    public async Task Customization_of_the_problem_details_options_applies()
    {
        var request = Request(StatusCodes.Status404NotFound);
        var options = new ProblemDetailsOptions { CustomizeProblemDetails = static context => context.ProblemDetails.Extensions["customized"] = true };

        await Writer(options).WriteAsync(Context(request, new ProblemDetails()));

        Assert.True(Body(request).GetProperty("customized").GetBoolean());
    }

    private static ProblemJsonWriter Writer(ProblemDetailsOptions? options = null) =>
        new(Options.Create(new JsonOptions()), Options.Create(options ?? new ProblemDetailsOptions()));

    private static DefaultHttpContext Request(int status) => new() { Response = { StatusCode = status, Body = new MemoryStream() } };

    private static ProblemDetailsContext Context(HttpContext request, ProblemDetails problem) => new() { HttpContext = request, ProblemDetails = problem };

    private static JsonElement Body(HttpContext request)
    {
        request.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(request.Response.Body);
        return document.RootElement.Clone();
    }
}
