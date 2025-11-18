namespace ZapretCLI.Models
{
    public class ListStats
    {
        public int TotalHosts { get; set; }
        public int TotalIPs { get; set; }
        public int ActiveProfiles { get; set; }
        public string DefaultProfile { get; set; }
    }
}