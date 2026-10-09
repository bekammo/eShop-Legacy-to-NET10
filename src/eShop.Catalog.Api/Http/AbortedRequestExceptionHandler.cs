using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Http;

// SqlClient reports a command that the request token cancels as a SqlException (wrapped in a DbUpdateException by
// SaveChanges), not as an OperationCanceledException, so the exception handler would otherwise answer 500.
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
