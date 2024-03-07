using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.InvokeAI;

namespace ShaosilBot.Core.Providers
{
	public class InvokeAIProvider : IImageGenerationProvider
	{
		private readonly ILogger<InvokeAIProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly HttpClient _httpClient;

		private readonly IReadOnlyCollection<string> _validModels;
		public IReadOnlyCollection<string> ValidSchedulers { get; private set; } = ["ddim", "ddpm", "deis", "lms", "lms_k", "pndm", "heun", "heun_k", "euler", "euler_k", "euler_a",
			"kdpm_2", "kdpm_2_a", "dpmpp_2s", "dpmpp_2s_k", "dpmpp_2m", "dpmpp_2m_k", "dpmpp_2m_sde", "dpmpp_2m_sde_k", "dpmpp_sde", "dpmpp_sde_k", "unipc", "lcm"];

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
			try
			{
				var response = await _httpClient.GetAsync("app/version");
				return response.IsSuccessStatusCode;
			}
			catch (HttpRequestException)
			{
				return false;
			}
		}

		public async Task<List<string>> GetModels()
		{
			_logger.LogInformation("Getting InvokeAI models");

			var response = await _httpClient.GetAsync("models/?model_type=main");
			if (response.IsSuccessStatusCode)
			{
				// Show the first one as default
				var body = JsonConvert.DeserializeObject<Model>(await response.Content.ReadAsStringAsync());
				return _validModels.Where(m => body?.Models?.Any(b => b.ModelName == m) ?? false).Select((m, i) => $"{m}{(i == 0 ? " (Default)" : string.Empty)}").ToList() ?? [];
			}
			else
			{
				throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");
			}
		}
	}
}