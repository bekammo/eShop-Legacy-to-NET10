using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace eShop.Catalog.Api.IntegrationTests;

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
