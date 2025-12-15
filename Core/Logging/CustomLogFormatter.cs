using Serilog.Events;
using Serilog.Formatting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZapretCLI.Core.Logging
{
    public class CustomLogFormatter : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output)
        {
            // Date: 14.12.25-11:28:24
            var timestamp = logEvent.Timestamp.ToString("dd.MM.yy-HH:mm:ss");

            // Log level
            var level = GetLevelAbbreviation(logEvent.Level);

            // Message
            var message = logEvent.MessageTemplate.Text;

            // Extra
            var exceptionDetails = "";
            if (logEvent.Exception != null)
            {
                exceptionDetails = $" Exception: {logEvent.Exception.GetType().Name} - {logEvent.Exception.Message}\n{logEvent.Exception.StackTrace}";
            }

            if (logEvent.Properties.TryGetValue("LogData", out var logDataProperty))
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var jsonData = JsonSerializer.Serialize(logDataProperty, options);
                    exceptionDetails += $" Data: {jsonData}";
                }
                catch
                {
                    exceptionDetails += $" Data: {logDataProperty}";
                }
            }

            // Collect everything
            output.WriteLine($"[{timestamp}] [{level}] {message}{exceptionDetails}");
        }

        private string GetLevelAbbreviation(LogEventLevel level)
        {
            return level switch
            {
                LogEventLevel.Verbose => "VRB",
                LogEventLevel.Debug => "DBG",
                LogEventLevel.Information => "INF",
                LogEventLevel.Warning => "WRN",
                LogEventLevel.Error => "ERR",
                LogEventLevel.Fatal => "FTL",
                _ => "UNK"
            };
        }
    }
}
