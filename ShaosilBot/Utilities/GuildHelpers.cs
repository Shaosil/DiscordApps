using Discord;
using System.Linq;

namespace ShaosilBot.Utilities
{
	public static class GuildHelpers
    {
        /// <summary>
        /// Returns true if the source user is equal to or outranks the target user. Admins can edit everyone except other admins.
        /// </summary>
        /// <returns>True if the source user IS the target user, or has a HIGHER rank than the target user.</returns>
        public static bool UserCanEditTargetUser(IGuild guild, IGuildUser sourceUser, IGuildUser targetUser)
        {
            int highestRequestorRolePosition = guild.Roles?.Where(r => sourceUser.RoleIds.Any(ur => ur == r.Id)).OrderByDescending(r => r.Position).FirstOrDefault()?.Position ?? -1;
            int highestTargetRolePosition = guild.Roles?.Where(r => targetUser.RoleIds.Any(ur => ur == r.Id)).OrderByDescending(r => r.Position).FirstOrDefault()?.Position ?? -1;

            return targetUser.Id == sourceUser.Id
                || !targetUser.GuildPermissions.Administrator && (sourceUser.GuildPermissions.Administrator || highestRequestorRolePosition > highestTargetRolePosition);
        }
    }
}