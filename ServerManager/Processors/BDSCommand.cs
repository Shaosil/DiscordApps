using ServerManager.Core;
using ServerManager.Core.Interfaces;
using ServerManager.Core.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ServerManager.Processors
{
	public class BDSCommand : IServerManagerCommand
	{
		private ManualResetEventSlim _waitSignal = new ManualResetEventSlim(true);
		private Process? _server = null;
		private Queue<string> _serverLogs = new Queue<string>();
		private StreamWriter? _serverInput = null;
		private readonly ILogger<BDSCommand> _logger;
		private readonly IConfiguration _configuration;

		public BDSCommand(ILogger<BDSCommand> logger, IConfiguration config)
		{
			_logger = logger;
			_configuration = config;
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
				switch (message.Instructions)
				{
					case SupportedCommands.BDS.Status:
						response = new QueueMessageResponse($"Bedrock Dedicated Server is **{(_server == null ? "OFFLINE" : "ONLINE")}**");
						break;

					case SupportedCommands.BDS.Startup:
						if (_server != null)
						{
							response = new QueueMessageResponse("A Bedrock dedicated server instance is already running. No action taken.");
						}
						else if (System.Diagnostics.Process.GetProcessesByName("bedrock_server").Any())
						{
							response = new QueueMessageResponse("WARNING: A Bedrock dedicated server instance is already running, but unmanaged. Use shutdown -force if you need to kill it.");
						}
						else
						{
							response = await Startup();
						}
						break;

					case SupportedCommands.BDS.ListPlayers:
						if (_server == null)
						{
							response = new QueueMessageResponse("Bedrock Dedicated Server is not running - No players online.");
						}
						else
						{
							var players = await GetConnectedPlayers();
							string playerList = players.Count > 0 ? string.Join('\n', players.Select(p => $"* {p}")) : string.Empty;
							string verbage = $"There {(players.Count == 1 ? "is" : "are")} currently {players.Count} player{(players.Count == 1 ? "" : "s")} online";
							response = new QueueMessageResponse($"{verbage}{(players.Count > 0 ? ":\n" : ".")}{playerList}");
						}
						break;

					case SupportedCommands.BDS.Shutdown:
						bool force = (bool?)message.Arguments.FirstOrDefault() ?? false;
						var connectedPlayers = await GetConnectedPlayers();
						if (connectedPlayers.Any() && !force)
						{
							string verbage = $"There {(connectedPlayers.Count == 1 ? "is" : "are")} {connectedPlayers.Count} player{(connectedPlayers.Count == 1 ? "" : "s")} online.";
							response = new QueueMessageResponse($"WARNING: {verbage} No action taken. Use shutdown -force if you need to kill it.");
						}
						else
						{
							response = await Shutdown(force);
						}
						break;

					case SupportedCommands.BDS.Logs:
						if (_server == null)
						{
							response = new QueueMessageResponse("Bedrock Dedicated Server is not running - no logs can be retrieved.");
						}
						else if (!_serverLogs.Any())
						{
							response = new QueueMessageResponse("WARNING: Bedrock Dedicated Server is running, but no logs could be retrieved.");
						}
						else
						{
							string num = message.Arguments.First().ToString()!;
							response = new QueueMessageResponse(string.Join('\n', _serverLogs.TakeLast(int.Parse(num))));
						}
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

		private async Task<QueueMessageResponse> Startup()
		{
			var serverFile = new FileInfo(_configuration["BDSLocation"] ?? string.Empty);
			if (!serverFile.Exists)
			{
				return new QueueMessageResponse("ERROR: Unable to find Bedrock server file at configured location.");
			}

			try
			{
				_logger.LogInformation("Starting new server process...");

				// Start up the server and keep the process in memory
				var startInfo = new ProcessStartInfo(serverFile.FullName);
				startInfo.RedirectStandardInput = true;
				startInfo.RedirectStandardOutput = true;
				startInfo.RedirectStandardError = true;
				startInfo.UseShellExecute = false;
				startInfo.CreateNoWindow = true;
				_server = new Process();
				_server.StartInfo = startInfo;
				_server.ErrorDataReceived += (sender, error) => HandleServerLog(error.Data, true);
				_server.OutputDataReceived += (sender, output) => HandleServerLog(output.Data, false);

				_server.Start();
				_server.BeginErrorReadLine();
				_server.BeginOutputReadLine();
				_serverInput = _server.StandardInput;

				// Monitor logs for "Server started" message, for up to 25 seconds
				bool startedSuccessfully = false;
				for (int i = 0; i < 25; i++)
				{
					await Task.Delay(1000);
					startedSuccessfully = _serverLogs.Any(l => l.Contains("Server started."));
					if (startedSuccessfully)
					{
						break;
					}
				}

				if (!startedSuccessfully)
				{
					// Kill process
					_server.Kill();
					_serverInput?.Close();
					_serverInput?.Dispose();
					_server = null;

					string logs = string.Join('\n', _serverLogs);
					return new QueueMessageResponse($"WARNING: Bedrock Dedicated Server process NOT started successfully. Logs:\n\n{logs}");
				}

				return new QueueMessageResponse("Bedrock Dedicated Server process started successfully.");
			}
			catch (Exception ex)
			{
				return new QueueMessageResponse($"EXCEPTION: {ex.Message}");
			}
		}

		private async Task<QueueMessageResponse> Shutdown(bool force = false)
		{
			if (_server != null && _serverInput != null)
			{
				_serverInput.WriteLine("stop");

				// Wait up to 25 seconds for it to finish shutting down
				bool quitSuccessfully = false;
				for (int i = 0; i < 25; i++)
				{
					await Task.Delay(1000);
					quitSuccessfully = _serverLogs.Any(l => l.Contains("Quit correctly"));
					if (quitSuccessfully)
					{
						break;
					}
				}

				if (!quitSuccessfully && force)
				{
					// Kill the process manually
					_server.Kill();
				}

				_serverInput?.Close();
				_serverInput?.Dispose();
				_server = null;

				if (quitSuccessfully)
				{
					return new QueueMessageResponse("Bedrock Dedicated Server successfully shut down.");
				}
				else if (force)
				{
					return new QueueMessageResponse("WARNING: Bedrock Dedicated Server NOT successfully shut down and had to be terminated forcefully.");
				}
				else
				{
					return new QueueMessageResponse("WARNING: Bedrock Dedicated Server was NOT successfully shut down.");
				}
			}
			else if (_server == null && force)
			{
				// Kill the process manually
				var existingProcess = System.Diagnostics.Process.GetProcessesByName("bedrock_server").FirstOrDefault();

				if (existingProcess != null)
				{
					existingProcess.Kill();
					return new QueueMessageResponse("Unmanaged Bedrock Dedicated Server has successfully been forcefully terminated.");
				}
				else
				{
					return new QueueMessageResponse("WARNING: No existing Bedrock Dedicated Server instance found. No action taken.");
				}
			}
			else
			{
				return new QueueMessageResponse("WARNING: No existing Bedrock Dedicated Server instance found. No action taken.");
			}
		}

		private async Task<List<string>> GetConnectedPlayers()
		{
			if (_server == null || _serverInput == null)
			{
				return new List<string>();
			}

			List<string> players = new();
			DataReceivedEventHandler logCapture = (s, e) =>
			{
				// Add to player list if there is no timestamp
				if (!string.IsNullOrWhiteSpace(e.Data) && !Regex.IsMatch(e.Data, @"^\[[\d\- \:]{20,} "))
				{
					players.Add(e.Data);
				}
			};
			_server!.OutputDataReceived += logCapture;

			// Send command and capture logs for half a second, then unsubscribe and return player list
			_serverInput!.WriteLine("list");
			await Task.Delay(500);
			_server!.OutputDataReceived -= logCapture;
			return players;
		}

		private void HandleServerLog(string? log, bool isError)
		{
			if (string.IsNullOrWhiteSpace(log))
			{
				return;
			}

			if (isError)
			{
				_logger.LogError(log);
			}
			else
			{
				_logger.LogInformation(log);
			}

			_serverLogs.Enqueue(log);
			if (_serverLogs.Count > 20)
			{
				_serverLogs.Dequeue();
			}
		}
	}
}