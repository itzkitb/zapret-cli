using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZapretCLI.Models
{
    public class TestResult
    {
        public string ProfileName { get; set; }
        public string TargetName { get; set; }
        public string TestType { get; set; } // HTTP, TLS1.2, TLS1.3, Ping, DPI
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool IsLikelyBlocked { get; set; }
        public double? PingTimeMs { get; set; }
        public int? StatusCode { get; set; }
        public long? ContentLength { get; set; }
    }
}
