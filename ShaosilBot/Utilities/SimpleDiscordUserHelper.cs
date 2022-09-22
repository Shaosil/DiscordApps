using Discord;
using Discord.Rest;
using ShaosilBot.Interfaces;
using ShaosilBot.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot.Utilities
{
    public static class SimpleDiscordUserHelper
    {
        public static async Task<List<SimpleDiscordUser>> GetAndUpdateUsers(IDataBlobProvider dataBlobProvider, RestGuild guild, string blobFileName, bool keepLock = false)
        {
            var simpleUsers = JsonSerializer.Deserialize<List<SimpleDiscordUser>>(await dataBlobProvider.GetBlobTextAsync(blobFileName, keepLock));

            // Update subscriber names based on user list
            var guildUsers = (await guild.GetUsersAsync().FlattenAsync()).ToList();
            int subscribersToEdit = simpleUsers.Count(s => !guildUsers.Any(u => u.Id == s.ID) || guildUsers.First(u => u.Id == s.ID).DisplayName != s.FriendlyName);
            if (subscribersToEdit > 0)
            {
                simpleUsers.RemoveAll(s => !guildUsers.Any(u => u.Id == s.ID));
                simpleUsers.ForEach(s => s.FriendlyName = guildUsers.First(u => u.Id == s.ID).DisplayName);
                await dataBlobProvider.SaveBlobTextAsync(blobFileName, JsonSerializer.Serialize(simpleUsers, new JsonSerializerOptions { WriteIndented = true }), !keepLock);
            }

            return simpleUsers;
        }
    }
}