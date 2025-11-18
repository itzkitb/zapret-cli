namespace ZapretCLI.Models
{
    public class ZapretProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Arguments { get; set; } = new List<string>();
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}