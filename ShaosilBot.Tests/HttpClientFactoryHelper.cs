namespace ShaosilBot.Tests
{
	internal class HttpClientFactoryHelper : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
		{
			return new HttpClient();
		}
	}
}