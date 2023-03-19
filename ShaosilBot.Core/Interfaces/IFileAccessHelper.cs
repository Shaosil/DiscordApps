namespace ShaosilBot.Core.Interfaces
{
	public interface IFileAccessHelper
	{
		T LoadFileJSON<T>(string filename, bool keepLease = false) where T : new();
		string LoadFileText(string filename, bool keepLease = false);
		void ReleaseFileLease(string filename);
		void SaveFileJSON<T>(string filename, T content, bool releaseLease = true);
	}
}