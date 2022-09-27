using Discord;
using Discord.Rest;
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
			var request = new HttpRequestDataBag();
			var randomSignatureBytes = new byte[badSignature ? 64 : 2];
			var randomPublicKeyBytes = new byte[32];
			Random.Shared.NextBytes(randomSignatureBytes);
			Random.Shared.NextBytes(randomPublicKeyBytes);
			Environment.SetEnvironmentVariable("PublicKey", Convert.ToHexString(randomPublicKeyBytes).ToLower());
			request.Headers.Add("X-Signature-Ed25519", Convert.ToHexString(randomSignatureBytes).ToLower());
			request.Headers.Add("X-Signature-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

			// Act
			var response = await RunInteractions(request);

			// Assert - Ensure unauthorized
			Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
			string bodyText = GetResponseBody(response);
			Assert.AreEqual((badSignature ? typeof(BadSignatureException) : typeof(ArgumentException)).Name, bodyText);
		}

		[TestMethod]
		public async Task AcknowledgesPing()
		{
			// Arrange
			var request = CreateInteractionRequest(DiscordInteraction.CreatePing());

			// Act
			var response = await RunInteractions(request);

			// Assert
			Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
			var responseObj = DeserializeResponse(response);
			Assert.AreEqual(InteractionResponseType.Pong, responseObj!.type);
		}
	}
}