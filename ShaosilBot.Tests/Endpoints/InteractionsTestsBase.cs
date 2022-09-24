using Discord;
using Discord.Rest;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NSec.Cryptography;
using ShaosilBot.Interfaces;
using ShaosilBot.Providers;
using ShaosilBot.Tests.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ShaosilBot.Tests.Endpoints
{
	[TestClass]
    public abstract class InteractionsTestsBase
    {
		private Interactions _interactionsSUT;
        protected Mock<ISlashCommandProvider> SlashCommandProviderMock { get; private set; }
        protected Mock<SlashCommandWrapper> SlashCommandWrapperMock { get; private set; }

        private static Mock<ILogger<Interactions>> _logger;
        private static Mock<IDiscordSocketClientProvider> _socketClientProviderMock;
        private static Mock<IDiscordRestClientProvider> _restClientProviderMock;

        [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
        public static void ClassInitialize(TestContext context)
        {
            _logger = new Mock<ILogger<Interactions>>();
            _socketClientProviderMock = new Mock<IDiscordSocketClientProvider>();
            _restClientProviderMock = new Mock<IDiscordRestClientProvider>();

            // Bypass our rest interaction client wrapper by calling Discord.Net's client parse
            var client = new DiscordRestClient(new DiscordRestConfig { UseInteractionSnowflakeDate = false });
            _restClientProviderMock.Setup(m => m.ParseHttpInteractionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string s1, string s2, string s3, string s4) => client.ParseHttpInteractionAsync(s1, s2, s3, s4));
        }

        [TestInitialize]
        public virtual void TestInitialize()
		{
			SlashCommandProviderMock = new Mock<ISlashCommandProvider>();
			SlashCommandWrapperMock = new Mock<SlashCommandWrapper>();
			_interactionsSUT = new Interactions(_logger.Object, SlashCommandProviderMock.Object, SlashCommandWrapperMock.Object, _socketClientProviderMock.Object, _restClientProviderMock.Object);
		}

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
			var response = await _interactionsSUT.Run(request);

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
			var response = await _interactionsSUT.Run(request);

			// Assert
			Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
			var responseObj = DeserializeResponse(response);
			Assert.AreEqual(InteractionResponseType.Pong, responseObj!.type);
		}

		protected string GetResponseBody(HttpResponseData response)
        {
            string body = string.Empty;
            using (response.Body)
            {
                response.Body.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(response.Body))
                {
                    body = reader.ReadToEnd();
                }
            }

            return body;
        }

		protected DiscordInteractionResponse DeserializeResponse(HttpResponseData response)
		{
			string body = GetResponseBody(response);
			return JsonSerializer.Deserialize<DiscordInteractionResponse>(body)!;
		}

        protected HttpRequestDataBag CreateInteractionRequest(DiscordInteraction interaction)
        {
            var bodyStr = interaction.Serialize();
            var timestampStr = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var bodyBytes = Encoding.UTF8.GetBytes(timestampStr + bodyStr);

            // Generate an ED25519 signature using a random key
            var algorithm = SignatureAlgorithm.Ed25519;
            var key = Key.Create(algorithm);
            Environment.SetEnvironmentVariable("PublicKey", Convert.ToHexString(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)).ToLower());
            var signatureBytes = algorithm.Sign(key, bodyBytes);
            string signatureStr = Convert.ToHexString(signatureBytes).ToLower();

            // Make sure the correct signature headers are included
            var headers = new Dictionary<string, string>
            {
                { "X-Signature-Ed25519", signatureStr },
                { "X-Signature-Timestamp", timestampStr }
            };
            var req = new HttpRequestDataBag(bodyStr, headers);

            return req;
        }

		protected async Task<HttpResponseData> RunInteractions(HttpRequestData request)
		{
			return await _interactionsSUT.Run(request);
		}

		protected async Task SafelyRunInteractions(HttpRequestData request)
		{
			// Run interactions endpoint without caring about a result
			try { await _interactionsSUT.Run(request); } catch { }
		}
	}
}