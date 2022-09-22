using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
    public interface IDataBlobProvider
    {
        Task<string> GetBlobTextAsync(string filename, bool aquireLease = false);
        void ReleaseFileLease(string filename);
        Task SaveBlobTextAsync(string filename, string content, bool releaseLease = true);
    }
}