using System.Diagnostics;
using ServerManager.Core;
using ServerManager.Core.Interfaces;
using ServerManager.Core.Models;

namespace ServerManager.Processors
{
	public class ComfyUICommand : IServerManagerCommand
	{
		private ManualResetEventSlim _waitSignal = new ManualResetEventSlim(true);
		private Process? _server = null;
		private readonly ILogger<ComfyUICommand> _logger;
		private readonly IConfiguration _configuration;
		private readonly HttpClient _httpClient;

		public ComfyUICommand(ILogger<ComfyUICommand> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
		{
			_logger = logger;
			_configuration = config;
			_httpClient = httpClientFactory.CreateClient();
			_httpClient.BaseAddress = new Uri(_configuration["ComfyUIBaseURL"]!);
		}

		public async Task<QueueMessageResponse> Process(QueueMessage message)
		{
			// Single thread all processing of BDS
			if (!_waitSignal.IsSet)
			{
				_logger.LogInformation("Waiting for previous process to finish...");
			}
			_waitSignal.Wait();
			_logger.LogInformation("Locking process...");
			_waitSignal.Reset();

			_logger.LogInformation($"Instrucitons received: {message.Instructions}");
			QueueMessageResponse response;

			try
			{
				switch (message.Instructions.ToLower())
				{
					case SupportedCommands.ComfyUI.Status:
						response = new QueueMessageResponse($"ComfyUI is **{(IsOnline() ? "ONLINE" : "OFFLINE")}**");
						break;

					case SupportedCommands.ComfyUI.Startup:
						response = await Startup();
						break;

					case SupportedCommands.ComfyUI.Shutdown:
						response = Shutdown();
						break;

					default:
						response = new QueueMessageResponse($"ERROR: '{message.Instructions}' is not a known command!");
						break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, null);
				response = new QueueMessageResponse($"Exception caught: '{ex.Message}'");
			}
			finally
			{
				_logger.LogInformation("Releasing locked process...");
				_waitSignal.Set();
			}

			return response;
		}

		private bool IsOnline()
		{
			// Is the server managed and running?
			if (!(_server?.HasExited ?? true))
			{
				return true;
			}

			// Return whether we can find an existing python process
			return System.Diagnostics.Process.GetProcessesByName("python").Any(p => p.Modules.Count > 100 && p.Modules.Cast<ProcessModule>().Any(m => m.FileName.Contains("\\ComfyUI\\")));
		}

		private async Task<QueueMessageResponse> Startup()
		{
			try
			{
				if (!_configuration.GetValue<bool>("ComfyUIEnabled"))
				{
					return new QueueMessageResponse("WARNING: The server owner has disabled the ComfyUI commands for now.");
				}

				if (IsOnline())
				{
					return new QueueMessageResponse("Existing ComfyUI process found. No action taken.");
				}

				_logger.LogInformation("Starting ComfyUI process...");

				var serverFile = new FileInfo(_configuration["ComfyUILocation"] ?? string.Empty);
				if (!serverFile.Exists)
				{
					return new QueueMessageResponse($"ERROR: Unable to find ComfyUI file at configured location ({serverFile.FullName}).");
				}

				// Start new process
				_server = new Process();
				_server.StartInfo.FileName = serverFile.FullName;
				_server.StartInfo.WorkingDirectory = serverFile.DirectoryName;
				_server.Start();

				// Ping version check API for up to 50 seconds
				bool startedSuccessfully = false;
				for (int i = 0; i < 50; i++)
				{
					await Task.Delay(1000);

					try
					{
						var versionResponse = await _httpClient.GetAsync("system_stats");
						startedSuccessfully = versionResponse.IsSuccessStatusCode;
					}
					catch (HttpRequestException)
					{
						// Fail silently
					}

					if (startedSuccessfully)
					{
						break;
					}
				}

				if (!startedSuccessfully)
				{
					// Kill process
					_server.Kill(true);
					_server = null;

					return new QueueMessageResponse($"WARNING: Failed to start ComfyUI process.");
				}

				return new QueueMessageResponse("ComfyUI started successfully.");
			}
			catch (Exception ex)
			{
				return new QueueMessageResponse($"EXCEPTION: {ex.Message}");
			}
		}

		private QueueMessageResponse Shutdown()
		{
			if (!_configuration.GetValue<bool>("ComfyUIEnabled"))
			{
				return new QueueMessageResponse("WARNING: The server owner has disabled the ComfyUI commands for now.");
			}

			if (_server != null)
			{
				// Simply kill the process
				_server.Kill(true);
				_server = null;

				return new QueueMessageResponse("ComfyUI process successfully terminated.");
			}
			else
			{
				// If we aren't aware of it here, do nothing
				return new QueueMessageResponse("WARNING: No existing managed ComfyUI instance found. No action taken.");
			}
		}
	}
}