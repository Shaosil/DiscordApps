using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.Twitch;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShaosilBot.Web.Controllers
{
	public class TwitchCallbackController : Controller
	{
		private readonly ILogger _logger;
		private readonly IConfiguration _configuration;
		private readonly ITwitchProvider _twitchProvider;

		public TwitchCallbackController(ILogger<TwitchCallbackController> logger, IConfiguration configuration, ITwitchProvider twitchProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_twitchProvider = twitchProvider;
		}

		[HttpPost("TwitchCallback")]
		public async Task<IActionResult> TwitchCallback()
		{
			// Verify signature
			string body = await new StreamReader(Request.Body).ReadToEndAsync();
			if (!IsValidSignature(Request, body))
				return Unauthorized();

			// Get message type header and body payload
			Request.Headers.TryGetValue("Twitch-Eventsub-Message-Type", out var messageType);
			var payload = JsonSerializer.Deserialize<TwitchPayload>(body)!;

			// Handle the various event types
			switch (messageType.FirstOrDefault()?.ToLower())
			{
				case "webhook_callback_verification":
					// Just respond with challenge
					_logger.LogInformation("Twitch webhook callback verification request received.");
					await Response.WriteAsync(payload.challenge);
					break;

				case "notification":
					// Handle the event
					await _twitchProvider.HandleNotification(payload);
					break;

				case "revocation":
					// Simply log it and return 200 as usual
					_logger.LogWarning($"Twitch revocation occured! Reason: {payload.subscription.status}");
					break;
			}

			Response.Headers.Add("Content-Type", "text/plain");
			return Ok();
		}

		/// <summary>
		/// Repurposed from https://github.com/TwitchLib/TwitchLib.EventSub.Webhooks
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		private bool IsValidSignature(HttpRequest request, string body)
		{
			try
			{
				// Calculate a valid signature based on our secret, the supplied message ID, timestamp, and body, and safely compare it to what the request provided
				byte[] secret = Encoding.UTF8.GetBytes(_configuration["TwitchAPISecret"] ?? "");
				byte[] message = Encoding.UTF8.GetBytes(
					request.Headers["Twitch-Eventsub-Message-Id"].ToString()
					+ request.Headers["Twitch-Eventsub-Message-Timestamp"].ToString()
					+ body
				);

				// Compute hash of message and get resulting hex string
				string hashHex;
				using (var hmac = new HMACSHA256(secret))
				{
					var hashBytes = hmac.ComputeHash(message);
					hashHex = Convert.ToHexString(hashBytes).ToLower();
				}
				string mySignature = $"sha256={hashHex}";
				string providedSignature = request.Headers["Twitch-Eventsub-Message-Signature"].ToString();

				return TimeSafeStringCompare(mySignature, providedSignature);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Exception occurred while calculating signature!");
				return false;
			}
		}

		private bool TimeSafeStringCompare(string s1, string s2)
		{
			// Always read the maximum length and compare each index if possible
			bool equal = true;
			int maxLength = Math.Max(s1.Length, s2.Length);

			for (int i = 0; i < maxLength; i++)
			{
				char c1 = s1.Length > i ? s1[i] : '\0';
				char c2 = s2.Length > i ? s2[i] : '\0';
				if (s1.Length <= i || s2.Length <= i || !c1.Equals(c2))
					equal = false;
			}

			return equal;
		}
	}
}