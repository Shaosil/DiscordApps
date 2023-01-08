namespace ShaosilBot.Core.Interfaces
{
	public interface IHttpUtilities
	{
		Task<string> GetRandomGitBlameImage();
	}
}