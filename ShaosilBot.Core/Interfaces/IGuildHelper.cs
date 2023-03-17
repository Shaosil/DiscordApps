using Discord;

namespace ShaosilBot.Core.Interfaces
{
	public interface IGuildHelper
	{
		bool UserCanEditTargetUser(IGuild guild, IGuildUser sourceUser, IGuildUser targetUser);
		List<ulong> LoadUserIDs(string userFile);
	}
}