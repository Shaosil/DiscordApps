using Discord;
using Discord.Rest;
using System.Linq;

namespace ShaosilBot.Utilities
{
    public static class GuildHelpers
    {
        /// <summary>
        /// Returns true if the source user is equal to or outranks the target user. Admins can edit everyone except other admins.
        /// </summary>
        /// <returns>True if the source user IS the target user, or has a HIGHER rank than the target user.</returns>
        public static bool UserCanEditTargetUser(IGuild guild, RestGuildUser sourceUser, RestGuildUser targetUser)
        {
            var highestRequestorRole = guild.Roles.Where(r => sourceUser.RoleIds.Any(ur => ur == r.Id)).OrderByDescending(r => r.Position).FirstOrDefault();
            var highestTargetRole = guild.Roles.Where(r => targetUser.RoleIds.Any(ur => ur == r.Id)).OrderByDescending(r => r.Position).FirstOrDefault();

            return targetUser.Id == sourceUser.Id
                || !targetUser.GuildPermissions.Administrator && (sourceUser.GuildPermissions.Administrator || highestRequestorRole.Position > highestTargetRole.Position);
        }
    }
}