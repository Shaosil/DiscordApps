using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.InvokeAI;
using ShaosilBot.Core.Models.InvokeAI.SocketIO;
using SocketIOClient;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static ShaosilBot.Core.Providers.MessageCommandProvider.MessageComponentNames;

namespace ShaosilBot.Core.Singletons
{
	public class InvokeAIProvider : IImageGenerationProvider
	{
		private readonly ILogger<InvokeAIProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly HttpClient _httpClient;
		private readonly SocketIOClient.SocketIO _socket;
		private readonly ManualResetEventSlim _waitSignal = new ManualResetEventSlim(true);

		// Tracked items
		private float _generatorUpdateInterval = 3; // In seconds
		private DateTime? _generatorLastUpdate = null;
		private readonly Dictionary<string, KeyValuePair<IUserMessage, IUser>> _trackedBatches = new Dictionary<string, KeyValuePair<IUserMessage, IUser>>();

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

			// Subscribe to socket.io events
			var socketOptions = new SocketIOOptions { Path = "/ws/socket.io", ConnectionTimeout = TimeSpan.FromSeconds(3), ReconnectionAttempts = 0 };
			_socket = new SocketIOClient.SocketIO($"http://{_httpClient.BaseAddress.Host}:{_httpClient.BaseAddress.Port}", socketOptions);
			_socket.OnConnected += async (_, _) => await _socket.EmitAsync("subscribe_queue", new { queue_id = "default" });
			_socket.OnDisconnected += OnSocketDisconnect;
			_socket.On("queue_item_status_changed", OnQueueItemStatusChanged);
			_socket.On("invocation_complete", r => OnLinearUIOutputNodeComplete(r, false));
			_socket.On("invocation_error", r => OnLinearUIOutputNodeComplete(r, true));
			_socket.On("generator_progress", OnGeneratorProgress);
		}

		/// <summary>
		/// Gets the allowed InvokeAI models
		/// </summary>
		/// <returns>A collection of Value -> Name pairs of models</returns>
		public Dictionary<string, string> GetConfigValidModels()
		{
			string rawModels = _configuration.GetValue<string>("InvokeAIValidModels")!;
			var validModels = rawModels.Split(',').ToDictionary(k => k.Split('|')[1], v => v.Split('|')[0]);

			return validModels;
		}

		public async Task<bool> IsOnline()
		{
			try
			{
				// Only allow one resource to attempt connection at a time
				_waitSignal.Wait();
				_waitSignal.Reset();

				// Ping app/version and make sure our socket is listening
				var response = await _httpClient.GetAsync("app/version");
				if (response.IsSuccessStatusCode && !_socket.Connected)
				{

					// Handle reconnect attempts manually since it often decides to speed through the timeouts internally
					int retries = 0;
					do
					{
						try
						{
							_logger.LogInformation($"Connecting to socket.io - attempt #{++retries}");
							await _socket.ConnectAsync();
						}
						catch (ConnectionException connEx)
						{
							_logger.LogError($"Connection error! {connEx}");
							return false; // TODO: Don't have a hard dependency on socket.io
						}

						if (!_socket.Connected)
						{
							await Task.Delay(1000);
						}
					} while (!_socket.Connected && retries < 5);
				}

				return response.IsSuccessStatusCode;
			}
			catch (HttpRequestException)
			{
				return false;
			}
			finally
			{
				_waitSignal.Set();
			}
		}

		public async Task<List<string>> GetModels()
		{
			_logger.LogInformation("Getting InvokeAI models");

			var response = await _httpClient.GetAsync("models/?model_type=main");
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");

			// Show the first one as default
			var body = JsonConvert.DeserializeObject<Model>(await response.Content.ReadAsStringAsync());
			var validModels = GetConfigValidModels().Keys;
			return validModels.Where(m => body?.Models?.Any(b => b.ModelName == m) ?? false).ToList() ?? [];
		}

		public async Task<List<Board>> GetAllBoards()
		{
			_logger.LogInformation($"Getting all InvokeAI boards");

			var response = await _httpClient.GetAsync("boards/?all=true");
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");

			return JsonConvert.DeserializeObject<List<Board>>(await response.Content.ReadAsStringAsync())!;
		}

		public async Task<Board?> GetBoardByName(string boardName)
		{
			_logger.LogInformation($"Getting InvokeAI board by name {boardName}");

			if (string.IsNullOrWhiteSpace(boardName))
			{
				return null;
			}

			// First get ALL boards, then find the one with this name, if any
			var allBoards = await GetAllBoards();
			var targetBoards = allBoards.Where(b => b.BoardName == boardName).ToList();
			if (targetBoards.Count > 1)
			{
				throw new Exception($"ERROR: Expected 1 board by name '{boardName}', but found {targetBoards.Count}!");
			}

			return targetBoards.FirstOrDefault();
		}

		public async Task<Board> CreateBoard(string boardName)
		{
			if (string.IsNullOrWhiteSpace(boardName))
			{
				throw new ArgumentException("Missing board name!");
			}

			_logger.LogInformation($"Creating new InvokeAI board with ID name {boardName}");

			var response = await _httpClient.PostAsync($"boards/?board_name={boardName}", null);
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");

			return JsonConvert.DeserializeObject<Board>(await response.Content.ReadAsStringAsync())!;
		}

		public async Task<QueueItemCollection.QueueItem?> GetCurrentQueueItem()
		{
			_logger.LogInformation("Getting current InvokeAI queue item");

			var response = await _httpClient.GetAsync("queue/default/current");
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");

			string? body = await response.Content.ReadAsStringAsync();
			return string.IsNullOrWhiteSpace(body) ? null : JsonConvert.DeserializeObject<QueueItemCollection.QueueItem>(body);
		}

		public async Task<QueueItemCollection> GetPendingQueueItems()
		{
			_logger.LogInformation("Getting pending InvokeAI queue items");

			var response = await _httpClient.GetAsync("queue/default/list?status=pending");
			if (!response.IsSuccessStatusCode) throw new Exception($"ERROR: {response.StatusCode} Response. Reason: {response.ReasonPhrase}");

			return JsonConvert.DeserializeObject<QueueItemCollection>(await response.Content.ReadAsStringAsync())!;
		}

		/// <summary>
		/// Enqueues a batch for image generation based on passed parameters.
		/// </summary>
		/// <returns>The batch item that was queued, if any, and the position in the queue</returns>
		/// <exception cref="Exception"></exception>
		public async Task<FriendlyEnqueueResult> EnqueueBatchItem(IUserMessage message, IUser requestor, string posPrompt, string negPrompt, int width, int height, string? seedStr, string? model, string scheduler, int steps, string cfg)
		{
			try
			{
				// One request at a time
				_waitSignal.Wait();
				_waitSignal.Reset();

				// Validate the arguments that can be out of range or unsupported
				if (seedStr == null || !uint.TryParse(seedStr, out var seed))
				{
					var uintBytes = new byte[4];
					new Random().NextBytes(uintBytes);
					seed = BitConverter.ToUInt32(uintBytes);
				}

				var allModels = await GetModels();
				if (!allModels.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase)))
				{
					model = allModels[0];
				}
				var modelName = GetConfigValidModels()[model!];

				// If this user does not already have a board, create one
				string boardName = GetUserBoardName(requestor);
				var userBoard = await GetBoardByName(boardName) ?? await CreateBoard(boardName);

				// Create batch item based on passed parameters
				var modelNode = new BatchNodes.Model(model!, "sdxl", "main");
				var vaeNode = new BatchNodes.VaeModel("sdxl-1-0-vae-fix", "sdxl");

				var newBatchItem = new BatchRoot
				(
					Prepend: false,
					Batch: new BatchRoot.BatchItem
					(
						BatchID: null,
						Graph: new BatchRoot.Graph
						(
							Id: "sdxl_text_to_image_graph",
							Nodes: new BatchNodes
							(
								SdxlModelLoader: new BatchNodes.SdxlModelLoaderNode
								(
									Type: "sdxl_model_loader",
									Id: "sdxl_model_loader",
									Model: modelNode,
									IsIntermediate: true
								),
								PositiveConditioning: new BatchNodes.PositiveConditioningNode
								(
									Type: "sdxl_compel_prompt",
									Id: "positive_conditioning",
									Prompt: posPrompt,
									Style: posPrompt,
									IsIntermediate: true
								),
								NegativeConditioning: new BatchNodes.NegativeConditioningNode
								(
									Type: "sdxl_compel_prompt",
									Id: "negative_conditioning",
									Prompt: negPrompt,
									Style: negPrompt,
									IsIntermediate: true
								),
								Noise: new BatchNodes.NoiseNode
								(
									Type: "noise",
									Id: "noise",
									Seed: seed,
									Width: width,
									Height: height,
									UseCpu: true,
									IsIntermediate: true
								),
								SdxlDenoiseLatents: new BatchNodes.SdxlDenoiseLatentsNode
								(
									Type: "denoise_latents",
									Id: "sdxl_denoise_latents",
									CfgScale: cfg,
									CfgRescaleMultiplier: "0",
									Scheduler: scheduler,
									Steps: steps,
									DenoisingStart: "0",
									DenoisingEnd: "1",
									IsIntermediate: true
								),
								LatentsToImage: new BatchNodes.LatentsToImageNode
								(
									Type: "l2i",
									Id: "latents_to_image",
									Fp32: false,
									IsIntermediate: true,
									UseCache: false
								),
								CoreMetadata: new BatchNodes.CoreMetadataNode
								(
									Type: "core_metadata",
									Id: "core_metadata",
									GenerationMode: "sdxl_txt2img",
									CfgScale: cfg,
									CfgRescaleMultiplier: "0",
									Width: width,
									Height: height,
									NegativePrompt: negPrompt,
									Model: modelNode,
									Steps: steps,
									RandDevice: "cpu",
									Scheduler: scheduler,
									NegativeStylePrompt: negPrompt,
									Vae: vaeNode
								),
								VaeLoader: new BatchNodes.VaeLoaderNode
								(
									Type: "vae_loader",
									Id: "vae_loader",
									IsIntermediate: true,
									VaeModel: vaeNode
								),
								LinearUiOutput: new BatchNodes.LinearUiOutputNode
								(
									Type: "linear_ui_output",
									Id: "linear_ui_output",
									IsIntermediate: false,
									UseCache: false,
									Board: new BatchNodes.Board(userBoard.BoardId)
								)
							),
							Edges:
							[
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("sdxl_model_loader", "unet"), Destination: new BatchRoot.EdgeItem("sdxl_denoise_latents", "unet")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("sdxl_model_loader", "clip"), Destination: new BatchRoot.EdgeItem("positive_conditioning", "clip")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("sdxl_model_loader", "clip2"), Destination: new BatchRoot.EdgeItem("positive_conditioning", "clip2")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("sdxl_model_loader", "clip"), Destination: new BatchRoot.EdgeItem("negative_conditioning", "clip")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("sdxl_model_loader", "clip2"), Destination: new BatchRoot.EdgeItem("negative_conditioning", "clip2")),

								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("positive_conditioning", "conditioning"), Destination: new BatchRoot.EdgeItem("sdxl_denoise_latents", "positive_conditioning")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("negative_conditioning", "conditioning"), Destination: new BatchRoot.EdgeItem("sdxl_denoise_latents", "negative_conditioning")),

								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("noise", "noise"), Destination: new BatchRoot.EdgeItem("sdxl_denoise_latents", "noise")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("sdxl_denoise_latents", "latents"), Destination: new BatchRoot.EdgeItem("latents_to_image", "latents")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("core_metadata", "metadata"), Destination: new BatchRoot.EdgeItem("latents_to_image", "metadata")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("vae_loader", "vae"), Destination: new BatchRoot.EdgeItem("latents_to_image", "vae")),
								new BatchRoot.Edge(Source: new BatchRoot.EdgeItem("latents_to_image", "image"), Destination: new BatchRoot.EdgeItem("linear_ui_output", "image"))
							]
						),
						Runs: 1,
						Data:
						[
							[
								new BatchRoot.Data("noise", "seed", [seed]),
								new BatchRoot.Data("core_metadata", "seed", [seed])
							],
							[
								new BatchRoot.Data("positive_conditioning", "prompt", [posPrompt]),
								new BatchRoot.Data("positive_conditioning", "style", [posPrompt]),
								new BatchRoot.Data("core_metadata", "positive_prompt", [posPrompt]),
								new BatchRoot.Data("core_metadata", "positive_style_prompt", [posPrompt]),
							]
						]
					)
				);

				// Deserialize response
				_logger.LogInformation("Queueing new batch item");
				var content = new StringContent(JsonConvert.SerializeObject(newBatchItem), MediaTypeHeaderValue.Parse("application/json"));
				var response = await _httpClient.PostAsync("queue/default/enqueue_batch", content);
				string responseContent = await response.Content.ReadAsStringAsync();
				if (response.IsSuccessStatusCode)
				{
					var queueData = JsonConvert.DeserializeObject<BatchRoot>(responseContent)!;
					_trackedBatches[queueData.Batch.BatchID!] = new KeyValuePair<IUserMessage, IUser>(message, requestor);
					_logger.LogInformation($"Queued new item with batch ID {queueData.Batch.BatchID}");

					// Get pending items to check if we were added to the queue or are next in line
					var curQueueItem = await GetCurrentQueueItem();
					var allPendingItems = await GetPendingQueueItems();
					var ourItem = allPendingItems.Items.FirstOrDefault(i => i.BatchID == queueData.Batch.BatchID);
					int linePos = ourItem != null ? allPendingItems.Items.IndexOf(ourItem) + (curQueueItem == null ? 1 : 2) : 1;

					// Return the queued information
					return new FriendlyEnqueueResult(queueData.Batch.BatchID!, linePos, posPrompt, negPrompt, seed, modelName, steps, cfg);
				}
				else
				{
					// Failed validation - manually throw exception
					var validationData = JsonConvert.DeserializeObject<ValidationError>(responseContent)!;
					var validationErrors = string.Join("\n", validationData.Details.Select(d => $"* {d.Type}: {d.Msg}"));
					_logger.LogError($"Validation Error(s): {validationErrors}");
					throw new Exception($"Enqueue validation error(s):\n\n{validationErrors}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error enqueueing batch item.");
				throw new Exception($"Exception during enqueue! {ex.Message}");
			}
			finally
			{
				// Let other threads through
				_waitSignal.Set();
			}
		}

		public bool TryCancelQueueItem(IUser user, string ID, out string response)
		{
			_logger.LogInformation($"User {user.Id} is attempting to cancel queue item by batch ID {ID}");

			if (!_trackedBatches.ContainsKey(ID))
			{
				response = $"Warning: Could not find queue item by batch ID '{ID}'.";
			}
			else if (_trackedBatches[ID].Value.Id != user.Id)
			{
				response = $"Warning: You are not authorized to cancel a queued item that you did not initiate.";
			}
			else
			{
				try
				{

					var requestBody = JsonContent.Create(new { batch_ids = new string[] { ID } });
					var result = _httpClient.PutAsync($"queue/default/cancel_by_batch_ids", requestBody).GetAwaiter().GetResult();
					if (result.IsSuccessStatusCode)
					{
						dynamic dType = new { canceled = false };
						var data = JsonConvert.DeserializeAnonymousType(result.Content.ReadAsStringAsync().GetAwaiter().GetResult(), dType);
						if (data.canceled)
						{
							response = "Successfully cancelled queue item.";
							return true;
						}
						else
						{
							response = "Warning: Successfully sent request to server but it failed to cancel.";
						}

						_trackedBatches.Remove(ID);
					}
					else
					{
						response = "Error while sending cancel request.";
					}
				}
				catch (Exception ex)
				{
					_logger.LogError($"Error: {ex}");
					response = $"Exception: {ex.Message}";
				}
			}

			return false;
		}

		public async Task<FriendlyEnqueueResult> RequeueImage(string imageName, IUserMessage message, IUser requestor)
		{
			// Fetch image first
			var imageData = (await GetImageMetadata(imageName))!;

			// Requeue with a blank seed and return the result
			return await EnqueueBatchItem(message, requestor, imageData.PositivePrompt, imageData.NegativePrompt, imageData.Width, imageData.Height, null,
				imageData.Model.ModelName, imageData.Scheduler, imageData.Steps, imageData.CfgScale);
		}

		private async Task<ImageMetadata?> GetImageMetadata(string imageName)
		{
			_logger.LogInformation($"Getting InvokeAI image metadata for {imageName}");

			var response = await _httpClient.GetAsync($"images/i/{imageName}/metadata");
			if (!response.IsSuccessStatusCode) return null;

			return JsonConvert.DeserializeObject<ImageMetadata>(await response.Content.ReadAsStringAsync());
		}

		public bool TryDeleteImage(IUser user, string imageName, out string deleteResponse)
		{
			_logger.LogInformation($"User {user.Id} is attempting to delete image {imageName}");

			// Does the image exist inside of the requesting user's board?
			bool ownsImage = false;
			var userBoard = GetBoardByName(GetUserBoardName(user)).GetAwaiter().GetResult();
			if (userBoard != null)
			{
				var response = _httpClient.GetAsync($"boards/{userBoard.BoardId}/image_names").GetAwaiter().GetResult();
				if (response.IsSuccessStatusCode)
				{
					var data = JsonConvert.DeserializeObject<string[]>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())!;
					ownsImage = data.Any(d => d == imageName);
				}
			}

			if (!ownsImage)
			{
				deleteResponse = "Warning: You are not authorized to delete an image that you did not create.";
			}
			else
			{
				// The server doesn't provide feedback as to whether the image existed or not in the first place, so just assume a successful response = success
				var result = _httpClient.DeleteAsync($"images/i/{imageName}").GetAwaiter().GetResult();
				if (result.IsSuccessStatusCode)
				{
					deleteResponse = "Image successfully deleted.";
					return true;
				}
				else
				{
					deleteResponse = "Error while sending delete request.";
				}
			}

			return false;
		}

		private string GetUserBoardName(IUser user)
		{
			return $"ShaosilBot-{user.Id}";
		}

		/// <summary>
		/// Updates all active messages each time an item is marked as in progress
		/// </summary>
		private void OnQueueItemStatusChanged(SocketIOResponse response)
		{
			if (_trackedBatches.Any())
			{
				// Ensure threads don't overlap
				_waitSignal.Wait();
				_waitSignal.Reset();

				try
				{
					// Deserialize and check if this is for a tracked item
					var data = JsonConvert.DeserializeObject<QueueStatus[]>(response.ToString())![0];

					if (data.QueueItem.Status == "in_progress" && _trackedBatches.ContainsKey(data.QueueItem.BatchId))
					{
						_logger.LogInformation($"Item {data.QueueItem.ItemId} marked as {data.QueueItem.Status}");

						// Get current pending queue items
						var allPendingItems = GetPendingQueueItems().GetAwaiter().GetResult();
						foreach (var batch in _trackedBatches.Keys)
						{
							// Make sure the message still exists
							var originalMessage = _trackedBatches[batch].Key.Channel.GetMessageAsync(_trackedBatches[batch].Key.Id).GetAwaiter().GetResult() as IUserMessage;

							if (originalMessage != null)
							{
								_logger.LogInformation("Found original message, sending update");
								int linePos = allPendingItems.Items.FindIndex(i => i.BatchID == batch) + 2;

								originalMessage.ModifyAsync(p =>
								{
									p.Embed = originalMessage.Embeds.First().Copy
									(
										newTitle: $"Status: Queued ({(linePos <= 1 ? "Next" : $"#{linePos}")} in line)"
									); ;
								}).GetAwaiter().GetResult();
							}
							else
							{
								// Clean up tracked item if the message no longer exists
								_logger.LogWarning("Could not find original message! Removing tracked object.");
								_trackedBatches.Remove(batch);
							}
						}
					}
				}
				finally
				{
					_waitSignal.Set();
				}
			}
		}

		/// <summary>
		/// Updates all in progress queue messages, including the completed one that triggered this socket message
		/// </summary>
		private void OnLinearUIOutputNodeComplete(SocketIOResponse response, bool isError)
		{
			if (!_trackedBatches.Any())
			{
				return;
			}

			try
			{
				// Deserialize and ensure it's of type "linear_ui_output"
				var data = JsonConvert.DeserializeObject<InvocationComplete[]>(response.ToString())![0];
				if (data.SourceNodeID != "linear_ui_output")
				{
					return;
				}

				// One thread at a time (shouldn't be a problem, but just in case)
				_waitSignal.Wait();
				_waitSignal.Reset();
				_logger.LogInformation($"Queue item ID {data.QueueItemID} {(isError ? "errored" : "completed")} in batch ID {data.QueueBatchID}.");

				// If this is the item just completed, update the message with the final image and remove it from the active items dictionary
				if (_trackedBatches.TryGetValue(data.QueueBatchID, out var trackedMessage))
				{
					IUserMessage? originalMessage = null;

					try
					{
						// If the original message still exists, update it
						originalMessage = trackedMessage.Key.Channel.GetMessageAsync(trackedMessage.Key.Id).GetAwaiter().GetResult() as IUserMessage;

						if (originalMessage != null)
						{
							_logger.LogInformation("Found original message - sending update");

							if (isError)
							{
								// Dump the whole response string to the log
								_logger.LogError(response.ToString());

								var modifiedEmbed = originalMessage.Embeds.First().Copy
								(
									newTitle: $"Status: Error!",
									newThumbnailURL: string.Empty
								);

								originalMessage.ModifyAsync(p =>
								{
									p.Content = $"{trackedMessage.Value.Mention} tried to generated an image.";
									p.Embed = modifiedEmbed;
									p.Attachments = null;
									var actionRow = new ActionRowBuilder().WithButton("Requeue", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdRequeue}-{data.Result.Image.ImageName}");
									p.Components = new ComponentBuilder().AddRow(actionRow).Build();
								}).GetAwaiter().GetResult();
							}
							else
							{
								var modifiedEmbed = originalMessage.Embeds.First().Copy
								(
									newTitle: $"Status: Complete",
									newThumbnailURL: string.Empty,
									newImgURL: $"attachment://completed.jpg"
								);

								// Get full image
								var imageResult = _httpClient.GetAsync($"images/i/{data.Result.Image.ImageName}/full").GetAwaiter().GetResult();
								using (var stream = imageResult.Content.ReadAsStream())
								{
									originalMessage.ModifyAsync(p =>
									{
										p.Content = $"{trackedMessage.Value.Mention} has generated an image!";
										p.Embed = modifiedEmbed;
										p.Attachments = new List<FileAttachment>([new FileAttachment(stream, "completed.jpg")]);
										var actionRow = new ActionRowBuilder()
											.WithButton("Requeue", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdRequeue}-{data.Result.Image.ImageName}")
											.WithButton("Delete", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdDelete}-{data.Result.Image.ImageName}", style: ButtonStyle.Danger);
										p.Components = new ComponentBuilder().AddRow(actionRow).Build();
									}).GetAwaiter().GetResult();
								}
							}
						}
						else
						{
							// Clean up tracked item if the message no longer exists
							_logger.LogWarning("Could not find original message! Removing tracked object.");
							_trackedBatches.Remove(data.QueueBatchID);
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
								p.Content = "Error during completion! Your image still exists, but something went wrong when passing it to Discord.";
								p.Attachments = null;
								p.Components = null;
							}).GetAwaiter().GetResult();
						}
					}
					finally
					{
						// Remove item from tracked commands
						_trackedBatches.Remove(data.QueueBatchID);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error: {ex}");
			}
			finally
			{
				_waitSignal.Set();
			}
		}

		private void OnGeneratorProgress(SocketIOResponse response)
		{
			if (_trackedBatches.Any())
			{
				// Limit message updates to every 5 seconds
				if (!_generatorLastUpdate.HasValue || (DateTime.Now - _generatorLastUpdate.Value).TotalSeconds >= _generatorUpdateInterval)
				{
					_generatorLastUpdate = DateTime.Now;

					// Get bytes
					var data = JsonConvert.DeserializeObject<GenerationProgress[]>(response.ToString())![0];
					if (data?.ProgressImage?.DataURL != null)
					{
						int fixedStep = Math.Clamp(data.Step, 1, data.TotalSteps);

						// Make sure the progress matches the active batch and item ID
						if (_trackedBatches.TryGetValue(data.QueueBatchID, out var trackedMessage))
						{
							_logger.LogInformation($"Generator progress event with batch ID {data.QueueBatchID}, item ID {data.QueueItemID}");

							// Update message, silently failing if unable to do so (i.e. if the message no longer exists)
							try
							{
								var originalMessage = trackedMessage.Key.Channel.GetMessageAsync(trackedMessage.Key.Id).GetAwaiter().GetResult() as IUserMessage;

								if (originalMessage != null)
								{
									_logger.LogInformation("Found original message, sending update");

									// Get progress image bytes
									int commaIndex = data.ProgressImage.DataURL.IndexOf(',') + 1;
									byte[] bytes = Convert.FromBase64String(data.ProgressImage.DataURL.Substring(commaIndex));

									var modifiedEmbed = originalMessage.Embeds.First().Copy
									(
										newTitle: $"Status: Generating (step {fixedStep} of {data.TotalSteps})",
										newThumbnailURL: "attachment://progress.jpg"
									);

									using (var stream = new MemoryStream(bytes))
									{
										originalMessage.ModifyAsync(p =>
										{
											p.Embed = modifiedEmbed;
											p.Attachments = new List<FileAttachment>([new FileAttachment(stream, "progress.jpg")]);
										}).GetAwaiter().GetResult();
									}
								}
								else
								{
									// Clean up tracked item if the message no longer exists
									_logger.LogWarning("Could not find original message! Removing tracked object.");
									_trackedBatches.Remove(data.QueueBatchID);
								}
							}
							catch (Exception ex)
							{
								_logger.LogWarning($"Silent error caught while trying to modify original response during generator progress: {ex}");
							}
						}
					}
					else
					{
						_logger.LogWarning("Generator progress event received with no image data!");
					}
				}
			}
		}

		private void OnSocketDisconnect(object? sender, string e)
		{
			// Update all in progress messages and remove them
			foreach (var trackedBatch in _trackedBatches.Keys)
			{
				try
				{
					// No need to load the original message for each one, just attempt an update and silently fail if needed
					_trackedBatches[trackedBatch].Key.ModifyAsync(p =>
					{
						p.Content = "Error: Disconnected from websocket handlers!";
						p.Attachments = null;
						p.Embed = null;
					}).GetAwaiter().GetResult();
				}
				finally
				{
					// Remove from tracked items
					_trackedBatches.Remove(trackedBatch);
				}
			}
		}
	}
}