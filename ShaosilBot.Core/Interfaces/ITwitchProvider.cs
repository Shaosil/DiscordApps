using ShaosilBot.Core.Models.Twitch;

namespace ShaosilBot.Core.Interfaces
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