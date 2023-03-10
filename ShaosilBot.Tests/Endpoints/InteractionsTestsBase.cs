using Discord.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NSec.Cryptography;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using ShaosilBot.Tests.Models;
using ShaosilBot.Web.Controllers;
using System.Text;
using System.Text.Json;

namespace ShaosilBot.Tests.Endpoints
{
	[TestClass]
	public abstract class InteractionsTestsBase
	{
		private InteractionsController _interactionsSUT;
		protected Mock<ISlashCommandProvider> SlashCommandProviderMock { get; private set; }
		protected Mock<SlashCommandWrapper> SlashCommandWrapperMock { get; private set; }

		protected static IConfiguration Configuration;
		private static Mock<IDiscordSocketClientProvider> _socketClientProviderMock;
		private static Mock<IDiscordRestClientProvider> _restClientProviderMock;

		[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
		public static void ClassInitialize(TestContext context)
		{
			Configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();
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
			_interactionsSUT = new InteractionsController(new Mock<ILogger<InteractionsController>>().Object, Configuration, SlashCommandProviderMock.Object, SlashCommandWrapperMock.Object, _restClientProviderMock.Object);
		}

		protected DiscordInteractionResponse DeserializeResponse(string? content)
		{
			return JsonSerializer.Deserialize<DiscordInteractionResponse>(content ?? string.Empty)!;
		}

		protected async Task<IActionResult> RunInteractions(HttpContext customContext)
		{
			// Arrange - Simply pass the custom context down to the controller
			_interactionsSUT.ControllerContext.HttpContext = customContext;

			// Act - Call Interactions endpoint
			return await _interactionsSUT.Interactions();

			// Assert - Should be done by inheriting classes
		}

		protected async Task<IActionResult> RunInteractions(DiscordInteraction interaction)
		{
			// Arrange - Convert the discord interaction into a valid HTTPContext
			var bodyStr = interaction.Serialize();
			var timestampStr = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
			var bodyBytes = Encoding.UTF8.GetBytes(timestampStr + bodyStr);

			// Generate an ED25519 signature using a random key
			var algorithm = SignatureAlgorithm.Ed25519;
			var key = Key.Create(algorithm);
			Configuration["PublicKey"] = Convert.ToHexString(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)).ToLower();
			var signatureBytes = algorithm.Sign(key, bodyBytes);
			string signatureStr = Convert.ToHexString(signatureBytes).ToLower();

			var context = new DefaultHttpContext();
			context.Request.Method = HttpMethods.Post;
			context.Request.Headers.Add("X-Signature-Ed25519", signatureStr);
			context.Request.Headers.Add("X-Signature-Timestamp", timestampStr);
			context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(bodyStr));

			return await RunInteractions(context);
		}
	}
}