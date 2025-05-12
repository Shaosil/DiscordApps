using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServerManager.Core;
using ServerManager.Core.Interfaces;
using ServerManager.Core.Models;
using ServerManager.Processors;
using static ServerManager.Core.Models.QueueMessage;

namespace ServerManager
{
	public class CommandProcessor : BackgroundService
	{
		private readonly ILogger<CommandProcessor> _logger;
		private readonly ConnectionFactory _factory;
		private IConnection? _connection;
		private IChannel _channel;
		private Dictionary<eCommandType, IServerManagerCommand> _commandProcessors = new Dictionary<eCommandType, IServerManagerCommand>();

		public CommandProcessor(ILogger<CommandProcessor> logger, IServiceProvider serviceProvider)
		{
			_logger = logger;
			_factory = new ConnectionFactory { HostName = "localhost" };

			// Map a resolved instance of every implementation of IServerManagerCommand to the CommandType enums
			_commandProcessors.Add(eCommandType.BDS, serviceProvider.GetRequiredService<BDSCommand>());
			_commandProcessors.Add(eCommandType.ComfyUI, serviceProvider.GetRequiredService<ComfyUICommand>());
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation($"Command Processor Service started at: {DateTime.Now}.");

			// Attempt to get a connection every 5 seconds, up to 3 tries, in case the RabbitMQ hasn't started yet
			for (int i = 1; i <= 3; i++)
			{
				_logger.LogInformation("Creating connection from ConnectionFactory...");
				try
				{
					_connection = await _factory.CreateConnectionAsync();
				}
				catch
				{
					_logger.LogWarning($"Warning - Connection unable to be created. {(i < 3 ? "Retrying in 5 seconds..." : string.Empty)}");
					await Task.Delay(5000);
					continue;
				}

				_logger.LogInformation("Connection created successfully.");
				break;
			}
			if (_connection == null)
			{
				_logger.LogError("Error - Connection could not be established. Is the RabbitMQ service running?");
				return;
			}

			// Open a channel and declare both queues
			_channel = await _connection.CreateChannelAsync();
			var ttlArgs = new Dictionary<string, object?> { { "x-message-ttl", 10000 } };
			await _channel.QueueDeclareAsync(queue: QueueNames.COMMAND_QUEUE, durable: false, exclusive: false, autoDelete: false, arguments: ttlArgs);

			var consumer = new AsyncEventingBasicConsumer(_channel);
			consumer.ReceivedAsync += ProcessQueueMessage;

			// Start basic consume on the main queue
			await _channel.BasicConsumeAsync(queue: QueueNames.COMMAND_QUEUE, autoAck: true, consumer: consumer);
			_logger.LogInformation($"Listening for messages on '{QueueNames.COMMAND_QUEUE}' queue...");
		}

		private async Task ProcessQueueMessage(object? sender, BasicDeliverEventArgs eventArgs)
		{
			// Deserialize the incoming message
			var message = QueueMessage.Deserialize(eventArgs.Body.ToArray());
			string commandType = Enum.GetName(message.CommandType)!;
			string args = message.Arguments.Any() ? string.Join(", ", message.Arguments) : string.Empty;
			_logger.LogInformation($"Message for {commandType} received: {message.Instructions} [{args}]");

			// Verify we have a matching mapped processor
			if (!_commandProcessors.ContainsKey(message.CommandType))
			{
				_logger.LogError($"No matching Processor for command type: {commandType}!");
				await ReplyToMessageIfRequested(eventArgs, new QueueMessageResponse($"ERROR: No matching processor found for command type '{commandType}'!"));
			}

			// Process message synchronously
			var processor = _commandProcessors[message.CommandType];
			_logger.LogInformation($"Sending instructions to {processor.GetType().Name} for processing...");
			var response = processor.Process(message).GetAwaiter().GetResult();

			// Send a response back over the callback pipeline if requested
			await ReplyToMessageIfRequested(eventArgs, response);
		}

		private async Task ReplyToMessageIfRequested(BasicDeliverEventArgs eventArgs, QueueMessageResponse response)
		{
			if (eventArgs.BasicProperties.IsReplyToPresent())
			{
				_logger.LogInformation($"Sending response to {eventArgs.BasicProperties.ReplyTo} queue...");
				var replyProps = new BasicProperties { CorrelationId = eventArgs.BasicProperties.CorrelationId }; // Sync correlation IDs
				await _channel.BasicPublishAsync(exchange: string.Empty, routingKey: eventArgs.BasicProperties.ReplyTo!, true, basicProperties: replyProps, body: response.Serialize());
			}
		}

		public override void Dispose()
		{
			// Gracefully shut down BDS if needed by passing it instructions
			var shutdownMessage = new QueueMessage { Instructions = SupportedCommands.BDS.Shutdown, Arguments = [true] };
			_commandProcessors[eCommandType.BDS].Process(shutdownMessage).GetAwaiter().GetResult();

			_channel?.CloseAsync().GetAwaiter().GetResult();
			_channel?.Dispose();
			_connection?.CloseAsync().GetAwaiter().GetResult();
			_connection?.Dispose();
			base.Dispose();
		}
	}
}