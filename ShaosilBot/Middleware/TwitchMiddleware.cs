using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.Middleware
{
    public class TwitchMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<TwitchMiddleware> _logger;

        public TwitchMiddleware(ILogger<TwitchMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var request = await context.GetHttpRequestDataAsync();

            // Verify signature
            if (request.Method != HttpMethod.Post.Method || !IsValidSignature(request))
            {
                var response = request.CreateResponse(HttpStatusCode.Unauthorized);
                await response.WriteStringAsync("Unauthorized");
                context.GetInvocationResult().Value = response;
                return;
            }

            await next(context);
        }

        /// <summary>
        /// Repurposed from https://github.com/TwitchLib/TwitchLib.EventSub.Webhooks
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private bool IsValidSignature(HttpRequestData request)
        {
            try
            {
                // Calculate a valid signature based on our secret, the supplied message ID, timestamp, and body, and safely compare it to what the request provided
                byte[] secret = Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("TwitchAPISecret"));
                byte[] message = Encoding.UTF8.GetBytes(
                    request.Headers.GetValues("Twitch-Eventsub-Message-Id").First()
                    + request.Headers.GetValues("Twitch-Eventsub-Message-Timestamp").First()
                    + request.ReadAsString()
                );

                // Reset body stream position
                request.Body.Position = 0;

                // Compute hash of message and get resulting hex string
                string hashHex;
                using (var hmac = new HMACSHA256(secret))
                {
                    var hashBytes = hmac.ComputeHash(message);
                    hashHex = Convert.ToHexString(hashBytes).ToLower();
                }
                string mySignature = $"sha256={hashHex}";
                string providedSignature = request.Headers.GetValues("Twitch-Eventsub-Message-Signature").First();

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