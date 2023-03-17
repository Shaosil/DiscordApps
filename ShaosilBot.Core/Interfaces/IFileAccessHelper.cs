namespace ShaosilBot.Core.Interfaces
{
	public interface IFileAccessHelper
	{
		string GetFileText(string filename, bool keepLease = false);
		void SaveFileText(string filename, string content, bool releaseLease = true);
		void ReleaseFileLease(string filename);
	}
}