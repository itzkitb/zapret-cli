using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZapretCLI.Models;

namespace ZapretCLI.Core.Interfaces
{
    public interface ITestService
    {
        Task RunTestsAsync(TestType testType);
        List<DpiTarget> GetDpiTargets(string customUrl = null);
        List<string> GetStandardTargets();
    }
}
