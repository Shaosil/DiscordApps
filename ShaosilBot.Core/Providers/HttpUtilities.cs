using Microsoft.Extensions.Configuration;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using System.Text.Json;

namespace ShaosilBot.Core.Providers
{
	public class HttpUtilities : IHttpUtilities
	{
		private readonly HttpClient _httpClient;
		private readonly IConfiguration _configuration;

		public HttpUtilities(IHttpClientFactory httpClientFactory, IConfiguration configuration)
		{
			_httpClient = httpClientFactory.CreateClient();
			_configuration = configuration;
		}

		public async Task<string> GetRandomGitBlameImage()
		{
			var req = new HttpRequestMessage(HttpMethod.Get, _configuration["ImgurGitBlameAlbum"]);
			req.Headers.Add("Authorization", $"Client-ID {_configuration["ImgurClientID"]}");
			var albumResponse = await (await _httpClient.SendAsync(req)).Content.ReadAsStringAsync();
			var allImages = JsonSerializer.Deserialize<ImgurData>(albumResponse)!.Images;

			return allImages[Random.Shared.Next(allImages.Count)].link;
		}
	}
}