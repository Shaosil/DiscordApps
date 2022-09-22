using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
    public interface ITwitchMiddlewareHelper
    {
        HttpResponseData ResponseData { get; }

        Task<HttpRequestData> GetRequestData(FunctionContext context);
        Task SetUnauthorizedResult(FunctionContext context, HttpRequestData request);
    }
}