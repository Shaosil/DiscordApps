using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using System.Net;

namespace ShaosilBot.Tests.Models
{
    public class HttpResponseDataBag : HttpResponseData
    {
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new HttpHeadersCollection();
        public override Stream Body { get; set; } = new MemoryStream();

        public override HttpCookies Cookies => throw new NotImplementedException();

        public HttpResponseDataBag() : base(new Mock<FunctionContext>().Object) { }
    }
}