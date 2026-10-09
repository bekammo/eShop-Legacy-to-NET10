using Serilog.Events;

namespace eShop.Catalog.Api.Logging;

internal sealed record RequestLogLevel(LogEventLevel Level);
