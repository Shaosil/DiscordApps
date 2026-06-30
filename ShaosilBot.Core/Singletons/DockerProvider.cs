using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Singletons
{
	public class DockerProvider : IDockerProvider
	{
		private readonly ILogger<DockerProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly DockerClient _docker;

		private const string ComfyContainer = "comfyui";	

		public DockerProvider(ILogger<DockerProvider> logger, IConfiguration configuration, DockerClient docker)
		{
			_logger = logger;
			_configuration = configuration;
			_docker = docker;

		}

		public async Task<bool> CheckComfyRunningAsync()
		{
			var info = await _docker.Containers.InspectContainerAsync(ComfyContainer);
			return info?.State?.Running ?? false;
		}

		public async Task<KeyValuePair<bool, string>> StartComfyAsync()
		{
			if (!_configuration.GetValue<bool>("ImageAIEnabled"))
			{
				return new KeyValuePair<bool, string>(false, "Image generation service management is currently disabled.");
			}
			else if (await CheckComfyRunningAsync())
			{
				return new KeyValuePair<bool, string>(false, "The image generation service is already running.");
			}
			else
			{
				await _docker.Containers.StartContainerAsync(ComfyContainer, new ContainerStartParameters());
				bool success = await CheckComfyRunningAsync();
				return new KeyValuePair<bool, string>(success, success ? "The image generation service is now running." : "The image generation service failed to start.");
			}
		}

		public async Task<KeyValuePair<bool, string>> StopComfyAsync()
		{
			if (!_configuration.GetValue<bool>("ImageAIEnabled"))
			{
				return new KeyValuePair<bool, string>(false, "Image generation service management is currently disabled.");
			}
			else if (!await CheckComfyRunningAsync())
			{
				return new KeyValuePair<bool, string>(false, "The image generation service is not running.");
			}
			else
			{
				await _docker.Containers.StopContainerAsync(ComfyContainer, new ContainerStopParameters());
				bool success = !await CheckComfyRunningAsync();
				return new KeyValuePair<bool, string>(success, success ? "The image generation service is now stopped." : "The image generation service failed to stop.");
			}
		}
	}
}