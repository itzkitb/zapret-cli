using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZapretCLI.Models
{
    public class DpiTarget
    {
        public string Id { get; set; }
        public string Provider { get; set; }
        public string Url { get; set; }
        public int Times { get; set; } = 1;
    }
}
