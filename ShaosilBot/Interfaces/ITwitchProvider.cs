using ShaosilBot.Models.Twitch;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
	public interface ITwitchProvider
	{
		Task<bool> DeleteSubscriptions(List<TwitchSubscriptions.Datum> subscriptions);
		Task<TwitchSubscriptions> GetSubscriptionsAsync();
		Task<TwitchUsers> GetUsers(bool byID, params string[] parameters);
		Task HandleNotification(TwitchPayload payload);
		Task<bool> PostSubscription(string userId);
	}
}