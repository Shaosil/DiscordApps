using System.Collections.Generic;
using System.Text.Json.Serialization;
using System;

namespace ShaosilBot.Models.Twitch
{
    public class OAuthInfo
    {
        public string Token { get; set; }

        public DateTimeOffset Expires { get; set; }
    }

    public class TwitchSubscriptions
    {
        public List<Datum> data { get; set; }
        public int total { get; set; }
        public int total_cost { get; set; }
        public int max_total_cost { get; set; }
        public Pagination pagination { get; set; }

        public class Condition
        {
            public string broadcaster_user_id { get; set; }
            public string user_id { get; set; }
        }

        public class Datum
        {
            public string id { get; set; }
            public string status { get; set; }
            public string type { get; set; }
            public string version { get; set; }
            public int cost { get; set; }
            public Condition condition { get; set; }
            public DateTime created_at { get; set; }
            public Transport transport { get; set; }
        }

        public class Pagination
        {
        }

        public class Transport
        {
            public string method { get; set; }
            public string callback { get; set; }
        }
    }

    public class TwitchPayload
    {
        public string challenge { get; set; }
        public Subscription subscription { get; set; }
        [JsonPropertyName("event")]
        public Event event_type { get; set; }

        public class Subscription
        {
            public string id { get; set; }
            public string status { get; set; }
            public string type { get; set; }
            public string version { get; set; }
            public int cost { get; set; }
            public Condition condition { get; set; }
            public Transport transport { get; set; }
            public DateTime created_at { get; set; }

            public class Condition
            {
                public string broadcaster_user_id { get; set; }
            }

            public class Transport
            {
                public string method { get; set; }
                public string callback { get; set; }
            }
        }

        public class Event
        {
            public string user_id { get; set; }
            public string user_login { get; set; }
            public string user_name { get; set; }
            public string broadcaster_user_id { get; set; }
            public string broadcaster_user_login { get; set; }
            public string broadcaster_user_name { get; set; }
            public string started_at { get; set; }
            public string title { get; set; }
            public string category_name { get; set; }
            public string category_id { get; set; }
        }
    }

    public class TwitchUsers
    {
        public List<Datum> data { get; set; }

        public class Datum
        {
            public string id { get; set; }
            public string login { get; set; }
            public string display_name { get; set; }
            public string type { get; set; }
            public string broadcaster_type { get; set; }
            public string description { get; set; }
            public string profile_image_url { get; set; }
            public string offline_image_url { get; set; }
            public int view_count { get; set; }
            public DateTime created_at { get; set; }
        }
    }

    public class ChannelInfoRoot
    {
        [JsonPropertyName("data")]
        public List<ChannelInfo> Channels { get; set; } = new List<ChannelInfo>();

        public class ChannelInfo
        {
            public string broadcaster_id { get; set; }
            public string broadcaster_login { get; set; }
            public string broadcaster_name { get; set; }
            public string broadcaster_language { get; set; }
            public string game_id { get; set; }
            public string game_name { get; set; }
            public string title { get; set; }
            public int delay { get; set; }
        }
    }
}