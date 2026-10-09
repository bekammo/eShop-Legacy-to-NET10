using System.Data.Common;
using eShop.Catalog.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.UnitTests.Http;

public sealed class AbortedRequestExceptionHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_exception_of_an_aborted_request_is_a_499(bool wrappedBySaveChanges)
    {
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };
        Exception exception = wrappedBySaveChanges ? new DbUpdateException("Saving failed.", new CancelledCommandException()) : new CancelledCommandException();

        Assert.True(await new AbortedRequestExceptionHandler().TryHandleAsync(request, exception, request.RequestAborted));
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, request.Response.StatusCode);
    }

    [Fact]
    public async Task Database_error_while_the_client_waits_is_left_to_the_exception_handler()
    {
        var request = new DefaultHttpContext();

        Assert.False(await new AbortedRequestExceptionHandler().TryHandleAsync(request, new CancelledCommandException(), request.RequestAborted));
        Assert.Equal(StatusCodes.Status200OK, request.Response.StatusCode);
    }

    [Fact]
    public async Task Other_error_after_the_client_has_gone_is_left_to_the_exception_handler()
    {
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        Assert.False(await new AbortedRequestExceptionHandler().TryHandleAsync(request, new InvalidOperationException("A bug."), request.RequestAborted));
        Assert.Equal(StatusCodes.Status200OK, request.Response.StatusCode);
    }

    private sealed class CancelledCommandException() : DbException("Operation cancelled by user.");
}
