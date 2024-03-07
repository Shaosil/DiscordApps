namespace ShaosilBot.Core.Interfaces
{
	public interface IImageGenerationProvider
	{
		IReadOnlyCollection<string> ValidSchedulers { get; }

		Task<List<string>> GetModels();
		Task<bool> IsOnline();
	}
}