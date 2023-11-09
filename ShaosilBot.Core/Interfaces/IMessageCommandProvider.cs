using Discord.Rest;

namespace ShaosilBot.Core.Interfaces
{
	public interface IMessageCommandProvider
	{
		string HandleMessageCommand(RestMessageCommand command);
		Task<string> HandleMessageComponent(RestMessageComponent messageComponent);
		Task<string> HandleModel(RestModal modal);
	}
}