namespace ShaosilBot.Core.Interfaces
{
	public interface IDockerProvider
	{
		Task<bool> CheckComfyRunningAsync();
		Task<KeyValuePair<bool, string>> StartComfyAsync();
		Task<KeyValuePair<bool, string>> StopComfyAsync();
	}
}