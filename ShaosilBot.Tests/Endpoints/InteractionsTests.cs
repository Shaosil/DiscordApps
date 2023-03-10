using Discord;
using Discord.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Tests.Models;
using System.Net;

namespace ShaosilBot.Tests.Endpoints
{
	[TestClass]
	public abstract class InteractionsTests : InteractionsTestsBase
	{
		[TestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public async Task BadSignature_ReturnsUnauthorized(bool badSignature)
		{
			// Arrange - Pass a dummy request based on bad signature or argument exception (arg exception needs no extra work)
			var context = new DefaultHttpContext();
			var randomSignatureBytes = new byte[badSignature ? 64 : 2];
			var randomPublicKeyBytes = new byte[32];
			Random.Shared.NextBytes(randomSignatureBytes);
			Random.Shared.NextBytes(randomPublicKeyBytes);
			Configuration["PublicKey"] = Convert.ToHexString(randomPublicKeyBytes).ToLower();
			context.Request.Headers.Add("X-Signature-Ed25519", Convert.ToHexString(randomSignatureBytes).ToLower());
			context.Request.Headers.Add("X-Signature-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

			// Act
			var response = await RunInteractions(context) as UnauthorizedObjectResult;

			// Assert - Ensure unauthorized
			Assert.IsNotNull(response);
			string? bodyText = response!.Value!.ToString();
			Assert.AreEqual((badSignature ? typeof(BadSignatureException) : typeof(ArgumentException)).Name, bodyText);
		}

		[TestMethod]
		public async Task AcknowledgesPing()
		{
			// Arrange
			var ping = DiscordInteraction.CreatePing();

			// Act
			var response = await RunInteractions(ping) as ContentResult;

			// Assert
			Assert.AreEqual((int)HttpStatusCode.OK, response!.StatusCode.GetValueOrDefault());
			var responseObj = DeserializeResponse(response!.Content);
			Assert.AreEqual(InteractionResponseType.Pong, responseObj!.type);
		}
	}
}