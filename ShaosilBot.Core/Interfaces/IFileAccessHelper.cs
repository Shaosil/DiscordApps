namespace ShaosilBot.Core.Interfaces
{
	public interface IFileAccessHelper
	{
		T LoadFileJSON<T>(string filename, bool lockFile = false) where T : new();
		string LoadFileText(string filename, bool lockFile = false);
		void SaveFileJSON<T>(string filename, T content, bool releaseLock = true);
		void ReleaseFileLease(string filename);
	}
}