using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace eShop.Catalog.Api.IntegrationTests;

// Throws for every request that no endpoint takes, inside the app's middleware, the request logging and the error
// handling included: the filter runs the app's own pipeline first, and adds this middleware after it. With
// afterResponseStarted, it first sends the headers and part of a body, so that no error response can replace them.
internal sealed class ThrowAfterEndpoints(bool afterResponseStarted) : IStartupFilter
{
    public const string Message = "Thrown by a test.";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);
            app.Run(async context =>
            {
                if (afterResponseStarted)
                {
                    await context.Response.WriteAsync("Part of a body", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }

                throw new InvalidOperationException(Message);
            });
        };
}
