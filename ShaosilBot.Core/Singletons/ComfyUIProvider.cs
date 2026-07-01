using System.Net.Http.Json;
using System.Net.Mime;
using System.Net.WebSockets;
using System.Text;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.ImageGeneration;
using ShaosilBot.Core.Models.ImageGeneration.ComfyUI;
using static ShaosilBot.Core.Providers.MessageCommandProvider.MessageComponentNames;

namespace ShaosilBot.Core.Singletons
{
	public class ComfyUIProvider : IImageGenerationProvider
	{
		private readonly ILogger<ComfyUIProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly HttpClient _httpClient;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly ManualResetEventSlim _waitSignal = new ManualResetEventSlim(true);
		private const string ClientID = "ShaosilBotComfyUI";

		// Tracked items
		private ClientWebSocket? _webSocket;
		private Task? _listenToWebSocket;
		private float _generatorUpdateInterval = 3; // In seconds
		private DateTimeOffset? _generatorLastUpdate = null;
		private readonly Dictionary<Guid, KeyValuePair<IUser, IUserMessage>> _trackedBatches = new(); // <promptID, <initiator, bot response>>

		public IReadOnlyCollection<string> ValidSamplers { get; private set; } = ["euler", "euler_cfg_pp", "euler_ancestral", "dpm_2", "dpm_2_ancestral",
				  "lms", "dpm_fast", "dpm_adaptive", "dpmpp_2s_ancestral", "dpmpp_2s_ancestral_cfg_pp", "dpmpp_sde", "dpmpp_sde_gpu",
				  "dpmpp_2m", "dpmpp_2m_cfg_pp", "dpmpp_2m_sde", "dpmpp_2m_sde_gpu", "dpmpp_3m_sde", "dpmpp_3m_sde_gpu", "ddpm",
				  "gradient_estimation", "gradient_estimation_cfg_pp", "er_sde", "seeds_2", "seeds_3", "ddim"];

		public ComfyUIProvider(ILogger<ComfyUIProvider> logger,
			IConfiguration configuration,
			IHttpClientFactory httpClientFactory,
			IFileAccessHelper fileAccessHelper)
		{
			_logger = logger;
			_configuration = configuration;
			_fileAccessHelper = fileAccessHelper;
			_httpClient = httpClientFactory.CreateClient();
			_httpClient.BaseAddress = new Uri(_configuration["ImageAIBaseURL"]!);
			_httpClient.Timeout = TimeSpan.FromSeconds(2); // None of the endpoints should take more than a second or two to be called

			Task.Run(IsOnline); // Run this on a new thread so it doesn't block or throw exceptions during spin up
		}

		/// <summary>
		/// Gets the allowed ComfyUI models
		/// </summary>
		/// <returns>A collection of Value -> Name pairs of models</returns>
		public Dictionary<string, string> GetConfigValidModels()
		{
			string rawModels = _configuration.GetValue<string>("ImageAIValidModels")!;
			var validModels = rawModels.Split(',').ToDictionary(k => k.Split('|')[1], v => v.Split('|')[0]);

			return validModels;
		}

		private async Task<bool> ConnectWebsocket()
		{
			if (_webSocket == null)
			{
				_webSocket = new ClientWebSocket();
			}

			if (_webSocket.State == WebSocketState.Open)
			{
				_logger.LogInformation("Already connected to ComfyUI websocket.");
				return true;
			}

			try
			{
				// Handle reconnect attempts
				string addr = $"ws://{_httpClient.BaseAddress!.Host}:{_httpClient.BaseAddress.Port}/ws?clientId={ClientID}";
				int retries = 0;

				do
				{
					try
					{
						_logger.LogInformation($"Connecting to ComfyUI websocket - attempt #{++retries}");
						await _webSocket.ConnectAsync(new Uri(addr), CancellationToken.None);
					}
					catch (Exception ex)
					{
						_logger.LogError($"Error while connecting: {ex}");
					}

					if (_webSocket.State != WebSocketState.Open)
					{
						await Task.Delay(1000);
					}
					else
					{
						// If we are connected, start (or restart) listening for requests
						_listenToWebSocket?.Dispose();
						_listenToWebSocket = null;
						_listenToWebSocket = new Task(ListenToWebSocketData);
						_listenToWebSocket.Start();
						_logger.LogInformation("Connected to ComfyUI websocket!");
					}
				} while (_webSocket.State != WebSocketState.Open && retries < 5);

				return _webSocket.State == WebSocketState.Open;
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error connecting to ComfyUI websocket: {ex}");
				return false;
			}
		}

		public async Task<bool> IsOnline()
		{
			try
			{
				// Only allow one resource to attempt connection at a time
				_waitSignal.Wait();
				_waitSignal.Reset();

				// Ping app/version and make sure our socket is open
				var response = await _httpClient.GetAsync("system_stats");
				return response.IsSuccessStatusCode && (await ConnectWebsocket());
			}
			catch
			{
				return false;
			}
			finally
			{
				_waitSignal.Set();
			}
		}

		public async Task<IEnumerable<string>> GetModelsOfType(string type, bool unfiltered = false)
		{
			_logger.LogInformation("Getting ComfyUI models");

			var response = await _httpClient.GetAsync($"models/{type}");
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");
			var models = JsonConvert.DeserializeObject<string[]>(await response.Content.ReadAsStringAsync())!;

			// If we requested main models, filter and order by our defined model list
			if (type.Equals("main", StringComparison.OrdinalIgnoreCase))
			{
				var validModels = GetConfigValidModels().Keys.ToList();
				return models.Where(m => unfiltered || validModels.Any(vm => vm == m)).OrderBy(m => validModels.IndexOf(m)).ToList();
			}

			return models;
		}

		public async Task<QueueResult> GetQueueItemsInternal()
		{
			_logger.LogInformation("Getting current ComfyUI queue items");

			var response = await _httpClient.GetAsync("queue");
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");

			string body = await response.Content.ReadAsStringAsync()!;
			var fullResult = JsonConvert.DeserializeObject<QueueResult>(body)!;

			return fullResult;
		}

		public async Task<List<Guid>> GetQueueItemIDs()
		{
			return (await GetQueueItemsInternal()).AllItems?.Select(r => r.Key)?.ToList() ?? [];
		}

		/// <summary>
		/// Enqueues an item for image generation based on passed prompt parameters.
		/// </summary>
		/// <returns>The item that was queued, if any, and the position in the queue</returns>
		/// <exception cref="Exception"></exception>
		public async Task<SharedEnqueueResult> EnqueuePrompt(IUserMessage message, IUser requestor, string posPrompt, string? negPrompt, int? width, int? height,
			string? seedStr, string? model, string? sampler, int? steps, int? cfg, string? imageName)
		{
			try
			{
				// One request at a time
				_waitSignal.Wait();
				_waitSignal.Reset();

				// Make sure there are not more than N queue messages already
				var numPending = (await GetQueueItemIDs()).Count;
				if (numPending >= _configuration.GetValue<int>("ImageAIQueueLimit"))
				{
					return new SharedEnqueueResult("Sorry, too many items in the image queue! Wait a little then try again.");
				}

				// Validate the arguments that can be out of range or unsupported
				if (seedStr == null || !ulong.TryParse(seedStr, out var seed))
				{
					if (seedStr != null)
					{
						// If the seed string has a value, calculate a 64 bit FNV-1a hash and convert it
						const ulong offset = 14695981039346656037;
						const ulong prime = 1099511628211;
						seed = offset;
						seedStr.ToList().ForEach(c => seed = (seed ^ c) * prime);
					}
					else
					{
						// Otherwise just randomize it
						var ulongBytes = new byte[8];
						new Random().NextBytes(ulongBytes);
						seed = BitConverter.ToUInt64(ulongBytes);
					}
				}

				var allModels = await GetModelsOfType("checkpoints", true);
				var validModels = GetConfigValidModels();
				var targetModel = allModels.FirstOrDefault(m => m.Equals(model, StringComparison.OrdinalIgnoreCase))    // Specific model name
					?? allModels.FirstOrDefault(m => validModels.FirstOrDefault(v => v.Value == model).Key == m)        // Specific friendly model name
					?? allModels.FirstOrDefault(m => m == validModels.Keys.First())                                     // Fallback to first valid model
					?? allModels.First();                                                                               // Fallback to first discovered model

				// Load workflow nodes
				bool isImageEdit = !string.IsNullOrWhiteSpace(imageName);
				string workflowType = isImageEdit ? "QwenImageEdit"
					: !targetModel.ToLower().Contains("pony") ? "SDXL"
					: "SDXL Pony";
				var workflow = _fileAccessHelper.LoadFileJSON<WorkflowNodes>($"ComfyUI Workflows/{workflowType}.json");

				// Modify based on prompt parameters
				if (!isImageEdit)
				{
					workflow.Checkpoint!.Inputs["ckpt_name"] = targetModel;
					if (width.HasValue) workflow.Image!.Inputs["width"] = width;
					if (height.HasValue) workflow.Image!.Inputs["height"] = height;
					workflow.Sampler!.Inputs["noise_seed"] = seed;
					if (steps.HasValue) workflow.Sampler!.Inputs["steps"] = steps;
					if (cfg.HasValue) workflow.Sampler!.Inputs["cfg"] = cfg;
					if (!string.IsNullOrWhiteSpace(sampler)) workflow.Sampler!.Inputs["sampler_name"] = sampler;
				}
				else
				{
					var loadImage = workflow.Values.First(v => v.ClassType == "LoadImage");
					loadImage.Inputs["image"] = imageName!;
				}
				if (workflow.PositiveText != null) workflow.PositiveText.SetPrompt(posPrompt);
				if (workflow.NegativeText != null) workflow.NegativeText.SetPrompt(negPrompt ?? string.Empty);
				workflow.SaveImage!.Inputs["filename_prefix"] += $"{requestor.Username}/Image";

				// Serialize and send to queue
				_logger.LogInformation("Queueing new prompt item");
				string serializedPrompt = $"{{ \"prompt\": {JsonConvert.SerializeObject(workflow)} }}";
				var response = await _httpClient.PostAsync("prompt", new StringContent(serializedPrompt, Encoding.UTF8, MediaTypeNames.Application.Json));
				string responseContent = await response.Content.ReadAsStringAsync();
				if (response.IsSuccessStatusCode)
				{
					var promptID = Guid.Parse(JObject.Parse(responseContent)["prompt_id"]!.ToString());
					_trackedBatches[promptID] = new(requestor, message);
					_logger.LogInformation($"Queued new item with prompt ID {promptID}");

					// Get pending items to check if we were added to the queue or are next in line
					var queueItems = await GetQueueItemIDs();
					int linePos = Math.Max(1, (queueItems?.IndexOf(promptID) ?? -1) + 1);

					// Return the queued information
					int parsedSteps = int.Parse(workflow.Sampler?.Inputs["steps"]?.ToString() ?? "4");
					int parsedCfg = int.Parse(workflow.Sampler?.Inputs["cfg"].ToString() ?? "1");
					return new SharedEnqueueResult(posPrompt, negPrompt, seed, validModels.GetValueOrDefault(targetModel) ?? targetModel, parsedSteps, parsedCfg, linePos, promptID, isImageEdit);
				}
				else
				{
					// Failed validation - manually throw exception
					_logger.LogError($"Validation Error(s): {responseContent}");
					return new SharedEnqueueResult("Prompt had validation error(s). Check server logs for details.");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error enqueueing batch item.");
				return new SharedEnqueueResult("Exception during enqueue! Check server logs for details.");
			}
			finally
			{
				// Let other threads through
				_waitSignal.Set();
			}
		}

		public async Task<KeyValuePair<bool, string>> TryCancelQueueItem(IUser user, Guid ID)
		{
			_logger.LogInformation($"User {user.Id} is attempting to cancel queue item by batch ID {ID}");

			if (!_trackedBatches.ContainsKey(ID))
			{
				return new KeyValuePair<bool, string>(false, $"Warning: Could not find queue item by batch ID '{ID}'.");
			}

			var deleteResult = await TryDeleteImage(user, ID);
			if (!deleteResult.Key)
			{
				return new KeyValuePair<bool, string>(false, deleteResult.Value);
			}

			_trackedBatches.Remove(ID);
			return new KeyValuePair<bool, string>(true, string.Empty);
		}

		/// <summary>
		/// Will either remove an image from the queue before it finishes generating, or delete the image file from disk. Will clear item from queue history either way.
		/// </summary>
		public async Task<KeyValuePair<bool, string>> TryDeleteImage(IUser user, Guid imageName)
		{
			try
			{
				// First, get the requested job, and check queue items
				var historyResult = await _httpClient.GetAsync($"jobs/{imageName}");
				var jobItem = JsonConvert.DeserializeObject<Job>(await historyResult.Content.ReadAsStringAsync());

				if (jobItem != null)
				{
					string? outputFolder;

					// If the job status is pending or in progress, also pull queue to verify workflow and permissions
					if (jobItem.Status == "pending" || jobItem.Status == "in_progress")
					{
						var queueResult = await _httpClient.GetAsync("queue");
						var queueItems = JsonConvert.DeserializeObject<QueueResult>(await queueResult.Content.ReadAsStringAsync());

						outputFolder = queueItems?.AllItems?.FirstOrDefault(q => $"{q.Key}" == jobItem.ID).Value
							?.SaveImage?.Inputs["filename_prefix"].ToString();
					}
					else
					{
						outputFolder = jobItem.Output?.Subfolder;
					}

					if (!outputFolder?.ToLower().Contains(user.Username) ?? false)
					{
						return new KeyValuePair<bool, string>(false, "Warning: You are not authorized to delete an image that you did not create.");
					}
					else
					{
						bool jobCleared = true;
						var requestBody = JsonContent.Create(new { delete = new Guid[] { imageName } });

						// Cancel or interrupt if pending or in progress, delete file (if needed) otherwise
						switch (jobItem.Status)
						{
							case "pending":
								jobCleared = _httpClient.PostAsync("queue", requestBody).GetAwaiter().GetResult().IsSuccessStatusCode;
								break;

							case "in_progress":
								jobCleared = _httpClient.PostAsync("interrupt", null).GetAwaiter().GetResult().IsSuccessStatusCode;
								break;

							default:
								if (!string.IsNullOrWhiteSpace(jobItem.Output?.Subfolder))
								{
									string file = Path.Combine(_configuration["ImageAIOutputFolder"]!, jobItem.Output.Subfolder, jobItem.Output.FileName);
									File.Delete(file);
									jobCleared = !File.Exists(file);
								}
								break;
						}

						// Try to clear it from the job history
						jobCleared &= _httpClient.PostAsync("history", requestBody).GetAwaiter().GetResult().IsSuccessStatusCode;

						if (jobCleared)
						{
							return new KeyValuePair<bool, string>(true, "Successfully deleted image.");
						}
						else
						{
							return new KeyValuePair<bool, string>(false, "Warning: Successfully sent request to server but it failed to fully delete.");
						}
					}
				}
				else
				{
					return new KeyValuePair<bool, string>(false, "Warning: Image not found in server history.");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error: {ex}");
				return new KeyValuePair<bool, string>(false, $"Exception: {ex.Message}");
			}
		}

		private async void ListenToWebSocketData()
		{
			Guid curPrompt = Guid.NewGuid();
			int curProgress = 0;
			int maxProgress = 100;
			string? currentNode = string.Empty;

			while (_webSocket?.State == WebSocketState.Open)
			{
				try
				{
					byte[] buffer = new byte[1024 * 128]; // 128 KB buffer
					var result = await _webSocket.ReceiveAsync(buffer, CancellationToken.None);

					if (!_trackedBatches.Any())
					{
						// Do nothing after receiving a signal if we are not tracking any prompts
						continue;
					}

					// Ensure threads don't overlap
					_waitSignal.Wait();
					_waitSignal.Reset();

					if (result.MessageType == WebSocketMessageType.Text)
					{
						string msgStr = Encoding.UTF8.GetString(buffer, 0, result.Count);
						dynamic msg = JsonConvert.DeserializeObject<dynamic>(msgStr)!;
						Guid.TryParse($"{msg.data.prompt_id}", out var parsedPrompt);

						if (msg.type == "progress" && parsedPrompt != Guid.Empty)
						{
							// If it is a progress message, keep track of the current prompt and progress stats
							curPrompt = parsedPrompt;
							curProgress = (int)msg.data.value;
							maxProgress = (int)msg.data.max;
							currentNode = msg.data.node;
						}
						else if (msg.type == "status" && curProgress == maxProgress && curPrompt != Guid.Empty)
						{
							// If this is a status message after progress is complete, update final and all queued items
							_logger.LogInformation($"Item {curPrompt} marked as complete.");
							await UpdateAllTrackedMessages(curPrompt);
							curPrompt = Guid.Empty;
						}
					}
					else if (result.MessageType == WebSocketMessageType.Binary && _trackedBatches.ContainsKey(curPrompt) && curProgress > 0)
					{
						// Binary data is an image after skipping 8 bytes
						byte[] imgBytes = new byte[result.Count - 8];
						Array.Copy(buffer.Skip(8).ToArray(), imgBytes, imgBytes.Length);

						// Update with preview image
						await ShowGenerationProgress(imgBytes, curPrompt, curProgress, maxProgress);
					}

				}
				catch (WebSocketException)
				{
					// If ComfyUI closes while we listen, it will throw this exception type. Silently catch it
					_logger.LogDebug("ComfyUI websocket closed unexpectedly. Untracking any known items.");
				}
				finally
				{
					_waitSignal.Set();
				}
			}

			// After web socket closes, update all in progress messages and remove them
			foreach (var trackedBatch in _trackedBatches.Keys)
			{
				try
				{
					// No need to load the original message for each one, just attempt an update and silently fail if needed
					await _trackedBatches[trackedBatch].Value.ModifyAsync(p =>
					{
						p.Content = "Error: Disconnected from websocket handler!";
						p.Attachments = null;
						p.Embed = null;
						p.Components = null;
					});
				}
				finally
				{
					// Remove from tracked items
					_trackedBatches.Remove(trackedBatch);
				}
			}

			// Smoothly close and dispose the websocket
			_webSocket?.Dispose();
			_listenToWebSocket?.Dispose();
			_webSocket = null;
			_listenToWebSocket = null;
		}

		private async Task UpdateAllTrackedMessages(Guid currentPrompt)
		{
			// Update all tracked messages with queue ordering and completion results
			foreach (var batch in _trackedBatches.Keys)
			{
				IUserMessage? originalMessage = null;

				try
				{
					// Make sure the message still exists
					originalMessage = (await _trackedBatches[batch].Value.Channel.GetMessageAsync(_trackedBatches[batch].Value.Id)) as IUserMessage;
					if (originalMessage != null)
					{
						_logger.LogInformation($"Found original message for {batch}, sending update");

						if (batch == currentPrompt)
						{
							// If this item was completed, retrieve and send the image
							await ShowFinalImage(originalMessage, currentPrompt);
						}
						else
						{
							// Otherwise, find it in the queue and updapte message with its position in line
							var otherQueueItems = (await GetQueueItemsInternal()).AllItems?.ToList();
							int linePos = Math.Max(1, (otherQueueItems?.FindIndex(i => i.Key == batch) ?? 0) + 1);

							await originalMessage.ModifyAsync(p =>
							{
								p.Embed = originalMessage.Embeds.FirstOrDefault()?.Copy
								(
									newTitle: $"Status: Queued ({(linePos <= 1 ? "Next" : $"#{linePos}")} in line)"
								);
							});
						}
					}
					else
					{
						// Clean up tracked item if the message no longer exists
						_logger.LogWarning("Could not find original message! Removing tracked object.");
					}
				}
				catch (Exception ex)
				{
					// Notify user
					_logger.LogError($"Error caught at end of generation: {ex}");
					if (originalMessage != null)
					{
						originalMessage.ModifyAsync(p =>
						{
							p.Content = "Error during completion! Your image still exists, but something went wrong when updating the message.";
							p.Embed = null;
							p.Attachments = null;
							p.Components = null;
						}).GetAwaiter().GetResult();
					}
				}
				finally
				{
					// Remove item from tracked commands if needed
					if (originalMessage == null || currentPrompt == batch)
					{
						_trackedBatches.Remove(batch);
					}
				}
			}
		}

		private async Task ShowGenerationProgress(byte[] imgBytes, Guid curPrompt, int curStep, int maxStep)
		{
			// Limit message updates to every N seconds
			if (!_generatorLastUpdate.HasValue || (DateTimeOffset.Now - _generatorLastUpdate.Value).TotalSeconds >= _generatorUpdateInterval)
			{
				// Load message. If it's not available, remove from tracked items
				var msgRef = _trackedBatches[curPrompt].Value;
				var originalMessage = await msgRef.Channel.GetMessageAsync(msgRef.Id) as IUserMessage;

				// If we can't find the original message anymore, stop tracking it and continue
				if (originalMessage == null)
				{
					// Clean up tracked item if the message no longer exists
					_logger.LogWarning("Could not find original message! Removing tracked object.");
					_trackedBatches.Remove(curPrompt);
					return;
				}

				_logger.LogDebug($"Generator progress event with batch ID {curPrompt}.");
				_generatorLastUpdate = DateTimeOffset.Now;

				// Update message, silently failing if unable to do so (i.e. if the message no longer exists)
				try
				{
					var modifiedEmbed = originalMessage.Embeds.First().Copy
					(
						newTitle: $"Status: Generating (step {curStep} of {maxStep})",
						newThumbnailURL: "attachment://progress.jpg"
					);

					using (var stream = new MemoryStream(imgBytes))
					{
						await originalMessage.ModifyAsync(p =>
						{
							p.Embed = modifiedEmbed;
							p.Attachments = new List<FileAttachment>([new FileAttachment(stream, "progress.jpg")]);
						});
					}
				}
				catch (Exception ex)
				{
					_logger.LogWarning($"Silent error caught while trying to modify original response during generator progress: {ex}");
				}
			}
		}

		private async Task ShowFinalImage(IUserMessage originalMessage, Guid completedPrompt)
		{
			// Get a history item so we can retrieve the filename based on this prompt ID
			var jobResult = await _httpClient.GetAsync($"jobs/{completedPrompt}");
			var jobItem = JsonConvert.DeserializeObject<Job>(await jobResult.Content.ReadAsStringAsync())!;
			bool isImageEdit = jobItem.Workflow?.Nodes?.Values?.FirstOrDefault(v => v.ClassType == "TextEncodeQwenImageEditPlus") != null;

			var modifiedEmbed = originalMessage.Embeds.First().Copy
			(
				newTitle: $"Status: Complete",
				newThumbnailURL: string.Empty,
				newImgURL: "attachment://completed.jpg"
			);

			// Update message with final image
			var imageResult = await _httpClient.GetAsync($"view?filename={jobItem.Output.FileName}&subfolder={jobItem.Output.Subfolder}");
			using (var stream = imageResult.Content.ReadAsStream())
			{
				originalMessage.ModifyAsync(p =>
				{
					p.Content = $"{_trackedBatches[completedPrompt].Key.Mention} has {(isImageEdit ? "edited" : "generated")} an image!";
					p.Embed = modifiedEmbed;
					p.Attachments = new List<FileAttachment>([new FileAttachment(stream, "completed.jpg")]);
					var actionRow = new ActionRowBuilder();
					if (!isImageEdit)
					{
						actionRow = actionRow
							.WithButton("Requeue", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdRequeue}-{completedPrompt}")
							.WithButton("Remix", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdRemix}-{completedPrompt}");
					}
					actionRow = actionRow.WithButton("Delete", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdDelete}-{completedPrompt}", style: ButtonStyle.Danger);

					p.Components = new ComponentBuilder().AddRow(actionRow).Build();
				}).GetAwaiter().GetResult();
			}
		}

		// TODO: Stop listening for websocket things (and free model/memory) after a certain time of inactivity
	}
}