using Microsoft.Extensions.Logging;
using Serilog.Context;
using System.Diagnostics;

namespace ZapretCLI.Core.Logging
{
    public class LoggerService : ILoggerService
    {
        private readonly ILogger<LoggerService> _logger;

        public LoggerService(ILogger<LoggerService> logger)
        {
            _logger = logger;

            var frame = new StackFrame(1, true);
            var method = frame.GetMethod();
        }

        private void LogWithCallerInfo(LogLevel logLevel, string message, Exception exception = null, object data = null)
        {
            var frame = new StackFrame(2, true);
            var method = frame.GetMethod();
            var sourceFile = method?.DeclaringType?.FullName ?? "Unknown";
            var lineNumber = frame.GetFileLineNumber();

            var eventData = new Dictionary<string, object>
            {
                ["LineNumber"] = lineNumber,
                ["SourceContext"] = sourceFile
            };

            if (data != null)
            {
                eventData["LogData"] = data;
            }

            using (LogContext.PushProperty("LineNumber", lineNumber))
            using (LogContext.PushProperty("SourceContext", sourceFile))
            {
                if (exception != null)
                {
                    _logger.Log(logLevel, exception, message);
                }
                else if (data != null)
                {
                    _logger.Log(logLevel, message, data);
                }
                else
                {
                    _logger.Log(logLevel, message);
                }
            }
        }

        public void LogDebug(string message, object data = null) =>
            LogWithCallerInfo(LogLevel.Debug, message, null, data);

        public void LogInformation(string message, object data = null) =>
            LogWithCallerInfo(LogLevel.Information, message, null, data);

        public void LogWarning(string message, object data = null) =>
            LogWithCallerInfo(LogLevel.Warning, message, null, data);

        public void LogError(string message, Exception exception = null, object data = null) =>
            LogWithCallerInfo(LogLevel.Error, message, exception, data);

        public void LogCritical(string message, Exception exception = null, object data = null) =>
            LogWithCallerInfo(LogLevel.Critical, message, exception, data);
    }
}
