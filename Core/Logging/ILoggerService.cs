using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZapretCLI.Core.Logging
{
    public interface ILoggerService
    {
        void LogDebug(string message, object data = null);
        void LogInformation(string message, object data = null);
        void LogWarning(string message, object data = null);
        void LogError(string message, Exception exception = null, object data = null);
        void LogCritical(string message, Exception exception = null, object data = null);
    }
}
