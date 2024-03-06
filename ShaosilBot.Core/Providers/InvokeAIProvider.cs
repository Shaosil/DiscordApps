using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShaosilBot.Core.Models.InvokeAI;

namespace ShaosilBot.Core.Providers
{
	public class InvokeAIProvider
	{
		private readonly ILogger<InvokeAIProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly HttpClient _httpClient;
		private readonly IReadOnlyList<string> _validModels;

		public InvokeAIProvider(ILogger<InvokeAIProvider> logger,
			IConfiguration configuration,
			IHttpClientFactory httpClientFactory)
		{
			_logger = logger;
			_configuration = configuration;
			_httpClient = httpClientFactory.CreateClient();
			_httpClient.BaseAddress = new Uri(_configuration["InvokeAIBaseURL"]!);
			_httpClient.Timeout = TimeSpan.FromSeconds(5); // None of the endpoints should take more than a second or two to be called
			_validModels = _configuration.GetValue<string>("InvokeAIValidModels")!.Split(',');
		}

		public async Task<bool> IsOnline()
		{
			var response = await _httpClient.GetAsync("app/version");
			return response.IsSuccessStatusCode;
		}

		public async Task<List<string>> GetModels()
		{
			_logger.LogInformation("Getting InvokeAI models");

			var response = await _httpClient.GetAsync("models/?model_type=main");
			if (response.IsSuccessStatusCode)
			{
				var body = JsonConvert.DeserializeObject<Model>(await response.Content.ReadAsStringAsync());
				return body?.Models?.Where(m => _validModels.Contains(m.ModelName)).Select(m => m.ModelName).ToList() ?? [];
			}
			else
			{
				throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");
			}
		}
	}
}