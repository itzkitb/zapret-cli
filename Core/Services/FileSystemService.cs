using ZapretCLI.Core.Interfaces;

namespace ZapretCLI.Core.Services
{
    public class FileSystemService : IFileSystemService
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public async Task<string[]> ReadAllLinesAsync(string path) => await File.ReadAllLinesAsync(path);
        public async Task WriteAllTextAsync(string path, string content) => await File.WriteAllTextAsync(path, content);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public string[] GetFiles(string path, string searchPattern) => Directory.GetFiles(path, searchPattern);
        public Stream OpenRead(string path) => File.OpenRead(path);
    }
}
