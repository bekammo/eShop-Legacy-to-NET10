using Serilog.Events;

namespace eShop.Catalog.Api.Logging;

// Endpoint metadata: the level of the request event for every request that the endpoint completes without an
// exception, whatever its status (ADR-0019).
internal sealed record RequestLogLevel(LogEventLevel Level);
