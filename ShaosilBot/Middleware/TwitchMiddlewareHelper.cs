using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ShaosilBot.Interfaces;
using System.Net;
using System.Threading.Tasks;

namespace ShaosilBot.Middleware
{
    public class TwitchMiddlewareHelper : ITwitchMiddlewareHelper
    {
        public HttpResponseData ResponseData { get; }

        public async Task<HttpRequestData> GetRequestData(FunctionContext context)
        {
            return await context.GetHttpRequestDataAsync();
        }

        public async Task SetUnauthorizedResult(FunctionContext context, HttpRequestData request)
        {
            var response = request.CreateResponse(HttpStatusCode.Unauthorized);
            await response.WriteStringAsync("Unauthorized");
            context.GetInvocationResult().Value = response;
        }
    }
}