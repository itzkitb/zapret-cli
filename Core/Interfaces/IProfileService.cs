using ZapretCLI.Models;

namespace ZapretCLI.Core.Interfaces
{
    public interface IProfileService
    {
        Task<List<ZapretProfile>> LoadProfilesFromArchive(string extractedPath);
        ZapretProfile ParseBatFile(string filePath);
        Task SaveProfile(ZapretProfile profile);
        Task<List<ZapretProfile>> GetAvailableProfilesAsync();
        Task<ZapretProfile> GetProfileByName(string name);
        void Dispose();
    }
}
