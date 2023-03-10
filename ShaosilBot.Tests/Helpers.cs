using Discord;
using Moq;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using System.Text.Json;

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
		public static List<SimpleDiscordUser> GenerateSimpleDiscordUsers(Mock<IFileAccessHelper> fileAccessHelper, Mock<IGuild> guild, string fileName, ChannelPermissions permissions)
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
				guildUserMock.SetupGet(m => m.Mention).Returns($"@{name}");
				guildUserMock.Setup(m => m.GetPermissions(It.IsAny<IGuildChannel>())).Returns(permissions);
				guildUsers.Add(guildUserMock.Object);

				guild.Setup(m => m.GetUserAsync(newID, It.IsAny<CacheMode>(), It.IsAny<RequestOptions>())).ReturnsAsync(guildUserMock.Object);
			}
			var simpleDiscordUsers = guildUsers.Select(u => new SimpleDiscordUser { ID = u.Id, FriendlyName = u.DisplayName }).ToList();

			// Make sure both the guild and blob file return the new users when asked
			guild.Setup(m => m.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>())).ReturnsAsync(guildUsers);
			fileAccessHelper.Setup(m => m.GetFileText(fileName, It.IsAny<bool>())).Returns(JsonSerializer.Serialize(simpleDiscordUsers));

			return simpleDiscordUsers;
		}
	}
}