using Microsoft.Extensions.Logging;

namespace Shared.Utils;

public static class LoggingExtensions
{
    public static void ConfigureDefaultLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            options.SingleLine = true;
            options.IncludeScopes = false;
        });
    }
}
