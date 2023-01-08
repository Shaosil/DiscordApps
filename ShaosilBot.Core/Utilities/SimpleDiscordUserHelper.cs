using Discord;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using System.Text.Json;

namespace ShaosilBot.Core.Utilities
{
	public static class SimpleDiscordUserHelper
    {
        public static async Task<List<SimpleDiscordUser>> GetAndUpdateUsers(IFileAccessHelper fileAccessHelper, IGuild guild, string blobFileName, bool keepLock = false)
        {
            var simpleUsers = JsonSerializer.Deserialize<List<SimpleDiscordUser>>(fileAccessHelper.GetFileText(blobFileName, keepLock));

            // Update subscriber names based on user list
            var guildUsers = (await guild.GetUsersAsync()).ToList();
            int subscribersToEdit = simpleUsers.Count(s => !guildUsers.Any(u => u.Id == s.ID) || guildUsers.First(u => u.Id == s.ID).DisplayName != s.FriendlyName);
            if (subscribersToEdit > 0)
            {
                simpleUsers.RemoveAll(s => !guildUsers.Any(u => u.Id == s.ID));
                simpleUsers.ForEach(s => s.FriendlyName = guildUsers.First(u => u.Id == s.ID).DisplayName);
                fileAccessHelper.SaveFileText(blobFileName, JsonSerializer.Serialize(simpleUsers, new JsonSerializerOptions { WriteIndented = true }), !keepLock);
            }

            return simpleUsers;
        }
    }
}