using Discord;
using Moq;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Tests
{
	public static class Extensions
	{
		public static ulong NextULong(this Random random)
		{
			var ulongBytes = new byte[8];
			random.NextBytes(ulongBytes);
			return BitConverter.ToUInt64(ulongBytes);
		}
	}

	public static class Helpers
	{
		public static List<ulong> GenerateSimpleDiscordUsers(Mock<IGuildHelper> guildHelper, Mock<IGuild> guild, string fileName, ChannelPermissions permissions)
		{
			// Generate 5-10 guild users with random IDs
			var guildUsers = new List<IGuildUser>(Random.Shared.Next(5, 11));
			for (int i = 0; i < guildUsers.Capacity; i++)
			{
				ulong newID = Random.Shared.NextULong();
				string name = $"User {i + 1}";
				var guildUserMock = new Mock<IGuildUser>();
				guildUserMock.SetupGet(m => m.Id).Returns(newID);
				guildUserMock.SetupGet(m => m.DisplayName).Returns(name);
				guildUserMock.SetupGet(m => m.Username).Returns(name);
				guildUserMock.SetupGet(m => m.Mention).Returns($"<@{newID}>");
				guildUserMock.Setup(m => m.GetPermissions(It.IsAny<IGuildChannel>())).Returns(permissions);
				guildUsers.Add(guildUserMock.Object);

				guild.Setup(m => m.GetUserAsync(newID, It.IsAny<CacheMode>(), It.IsAny<RequestOptions>())).ReturnsAsync(guildUserMock.Object);
			}
			var simpleDiscordUsers = guildUsers.Select(u => u.Id).ToList();

			// Make sure both the guild and helper return the new users when asked
			guild.Setup(m => m.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>())).ReturnsAsync(guildUsers);
			guildHelper.Setup(m => m.LoadUserIDs(fileName)).Returns(simpleDiscordUsers);
			guildHelper.Setup(m => m.UserCanEditTargetUser(It.IsAny<IGuild>(), It.IsAny<IGuildUser>(), It.IsAny<IGuildUser>())).Returns(true); // TODO: Return calculation

			return simpleDiscordUsers;
		}
	}
}