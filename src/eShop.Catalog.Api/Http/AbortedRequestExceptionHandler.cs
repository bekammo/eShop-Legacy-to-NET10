using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Http;

// A request whose client went away while the database ran its command (ADR-0027). The request's token cancels the
// command, and SqlClient reports that as a SqlException, "Operation cancelled by user.", which SaveChanges wraps in a
// DbUpdateException. The exception handler answers an OperationCanceledException of an aborted request with 499, and
// logs it only at Debug, but takes any other exception for the server's error: a 500, logged at Error. This answers a
// database exception of an aborted request as the cancellation that it almost always is. An exception that a handler
// handles is not logged by the exception handler.
internal sealed class AbortedRequestExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not (DbException or DbUpdateException) || !httpContext.RequestAborted.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        return ValueTask.FromResult(true);
    }
}
