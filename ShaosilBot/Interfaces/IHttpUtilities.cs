using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
	public interface IHttpUtilities
	{
		Task<string> GetRandomGitBlameImage();
	}
}