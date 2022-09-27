using ShaosilBot.Interfaces;
using ShaosilBot.Models;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot.Providers
{
	public class HttpUtilities : IHttpUtilities
	{
		private readonly HttpClient _httpClient;

		public HttpUtilities(IHttpClientFactory httpClientFactory)
		{
			_httpClient = httpClientFactory.CreateClient();
		}

		public async Task<string> GetRandomGitBlameImage()
		{
			var req = new HttpRequestMessage(HttpMethod.Get, Environment.GetEnvironmentVariable("ImgurGitBlameAlbum"));
			req.Headers.Add("Authorization", $"Client-ID {Environment.GetEnvironmentVariable("ImgurClientID")}");
			var albumResponse = await (await _httpClient.SendAsync(req)).Content.ReadAsStringAsync();
			var allImages = JsonSerializer.Deserialize<ImgurData>(albumResponse).Images;

			return allImages[Random.Shared.Next(allImages.Count)].link;
		}
	}
}