using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Moq;
using ShaosilBot.Interfaces;
using ShaosilBot.Middleware;
using ShaosilBot.Tests.Models;
using System.Security.Cryptography;
using System.Text;

namespace ShaosilBot.Tests
{
    [TestClass]
    public class TwitchMiddlewareTests
    {
        private TwitchMiddleware SUT;

        private static Mock<FunctionContext> _functionContextMock;
        private Mock<ITwitchMiddlewareHelper> _twitchMiddlewareHelperMock;
        private readonly FunctionExecutionDelegate _emptyFunctionExecutionDelegate = (c) => Task.Run(() => { });

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            _functionContextMock = new Mock<FunctionContext>();
        }

        [TestInitialize]
        public void TestInitialize()
        {
            _twitchMiddlewareHelperMock = new Mock<ITwitchMiddlewareHelper>();
            SUT = new TwitchMiddleware(new Mock<ILogger<TwitchMiddleware>>().Object, _twitchMiddlewareHelperMock.Object);
        }

        [TestMethod]
        public async Task ValidSignature_ReturnsTrue()
        {
            // Arrange
            HttpRequestDataBag validRequest = GenerateRequest(true);
            _twitchMiddlewareHelperMock.Setup(m => m.GetRequestData(_functionContextMock.Object)).Returns(Task.FromResult((HttpRequestData)validRequest));

            // Act
            await SUT.Invoke(_functionContextMock.Object, _emptyFunctionExecutionDelegate);

            // Assert
            _twitchMiddlewareHelperMock.Verify(m => m.SetUnauthorizedResult(It.IsAny<FunctionContext>(), It.IsAny<HttpRequestData>()), Times.Never, "Valid signature returned 401.");
        }

        [TestMethod]
        public async Task InvalidSignature_ReturnsFalse()
        {
            // Arrange
            HttpRequestDataBag invalidRequest = GenerateRequest(false);
            _twitchMiddlewareHelperMock.Setup(m => m.GetRequestData(_functionContextMock.Object)).Returns(Task.FromResult((HttpRequestData)invalidRequest));

            // Act
            await SUT.Invoke(_functionContextMock.Object, _emptyFunctionExecutionDelegate);

            // Assert
            _twitchMiddlewareHelperMock.Verify(m => m.SetUnauthorizedResult(It.IsAny<FunctionContext>(), It.IsAny<HttpRequestData>()), Times.Once, "Inalid signature never returned 401.");
        }

        private HttpRequestDataBag GenerateRequest(bool validSignature)
        {
            // Set up fake secret and encryption
            var apiSecret = new byte[32];
            for (int i = 0; i < 32; i++)
                apiSecret[i] = (byte)(48 + Random.Shared.Next(75));
            Environment.SetEnvironmentVariable("TwitchAPISecret", Encoding.UTF8.GetString(apiSecret));
            string messageId = Guid.NewGuid().ToString();
            string timestamp = DateTime.UtcNow.ToString("O");
            string body = "This is a random body test";

            string hashHex = string.Empty;
            if (validSignature)
            {
                using (var hmac = new HMACSHA256(apiSecret))
                {
                    var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{messageId}{timestamp}{body}"));
                    hashHex = Convert.ToHexString(hashBytes).ToLower();
                }
            }
            string calculatedSignature = validSignature ? $"sha256={hashHex}" : "THIS IS AN INVALID SIGNATURE";

            // Make sure the correct signature headers are included
            var headers = new Dictionary<string, string>
            {
                { "Twitch-Eventsub-Message-Id", messageId },
                { "Twitch-Eventsub-Message-Timestamp", timestamp },
                { "Twitch-Eventsub-Message-Signature", calculatedSignature }
            };
            var req = new HttpRequestDataBag(body, headers);

            return req;
        }
    }
}