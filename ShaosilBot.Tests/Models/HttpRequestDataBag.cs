using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using System.Security.Claims;

namespace ShaosilBot.Tests.Models
{
    public class HttpRequestDataBag : HttpRequestData
    {
        public override Stream Body { get; } = new MemoryStream();

        public override HttpHeadersCollection Headers { get; } = new HttpHeadersCollection();

        public override IReadOnlyCollection<IHttpCookie> Cookies => throw new NotImplementedException();

        public override Uri Url => throw new NotImplementedException();

        public override IEnumerable<ClaimsIdentity> Identities => throw new NotImplementedException();

        public override string Method => HttpMethod.Post.Method;

        public HttpRequestDataBag(string? body = null, Dictionary<string, string>? headers = null) : base (new Mock<FunctionContext>().Object)
        {
            if (body != null)
            {
                var writer = new StreamWriter(Body);
                writer.Write(body);
                writer.Flush();
                Body.Position = 0;
            }

            if (headers != null)
            {
                foreach (var item in headers)
                {
                    Headers.Add(item.Key, item.Value);
                }
            }
        }

        public override HttpResponseDataBag CreateResponse() => new HttpResponseDataBag();
    }
}