namespace ZapretCLI.Core.Interfaces
{
    public interface IFileSystemService
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        Task<string[]> ReadAllLinesAsync(string path);
        Task WriteAllTextAsync(string path, string content);
        void CreateDirectory(string path);
        string[] GetFiles(string path, string searchPattern);
        Stream OpenRead(string path);
    }
}
