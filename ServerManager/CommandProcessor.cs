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
		private IModel _channel;
		private Dictionary<eCommandType, IServerManagerCommand> _commandProcessors = new Dictionary<eCommandType, IServerManagerCommand>();

		public CommandProcessor(ILogger<CommandProcessor> logger, IServiceProvider serviceProvider)
		{
			_logger = logger;
			_factory = new ConnectionFactory { HostName = "localhost" };

			// Map a resolved instance of every implementation of IServerManagerCommand to the CommandType enums
			_commandProcessors.Add(eCommandType.BDS, serviceProvider.GetRequiredService<BDSCommand>());
			_commandProcessors.Add(eCommandType.InvokeAI, serviceProvider.GetRequiredService<InvokeAICommand>());
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
					_connection = _factory.CreateConnection();
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
			_channel = _connection.CreateModel();
			var ttlArgs = new Dictionary<string, object> { { "x-message-ttl", 10000 } };
			_channel.QueueDeclare(queue: QueueNames.COMMAND_QUEUE, durable: false, exclusive: false, autoDelete: false, arguments: ttlArgs);

			var consumer = new EventingBasicConsumer(_channel);
			consumer.Received += ProcessQueueMessage;

			// Start basic consume on the main queue
			_channel.BasicConsume(queue: QueueNames.COMMAND_QUEUE, autoAck: true, consumer: consumer);
			_logger.LogInformation($"Listening for messages on '{QueueNames.COMMAND_QUEUE}' queue...");
		}

		private void ProcessQueueMessage(object? sender, BasicDeliverEventArgs eventArgs)
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
				ReplyToMessageIfRequested(eventArgs, new QueueMessageResponse($"ERROR: No matching processor found for command type '{commandType}'!"));
				return;
			}

			// Process message synchronously
			var processor = _commandProcessors[message.CommandType];
			_logger.LogInformation($"Sending instructions to {processor.GetType().Name} for processing...");
			var response = processor.Process(message).GetAwaiter().GetResult();

			// Send a response back over the callback pipeline if requested
			ReplyToMessageIfRequested(eventArgs, response);
		}

		private void ReplyToMessageIfRequested(BasicDeliverEventArgs eventArgs, QueueMessageResponse response)
		{
			if (eventArgs.BasicProperties.IsReplyToPresent())
			{
				_logger.LogInformation($"Sending response to {eventArgs.BasicProperties.ReplyTo} queue...");
				var replyProps = _channel.CreateBasicProperties();
				replyProps.CorrelationId = eventArgs.BasicProperties.CorrelationId; // Sync correlation IDs
				_channel.BasicPublish(exchange: string.Empty, routingKey: eventArgs.BasicProperties.ReplyTo, basicProperties: replyProps, body: response.Serialize());
			}
		}

		public override void Dispose()
		{
			// Gracefully shut down BDS if needed by passing it instructions
			var shutdownMessage = new QueueMessage { Instructions = SupportedCommands.BDS.Shutdown, Arguments = [true] };
			_commandProcessors[eCommandType.BDS].Process(shutdownMessage).GetAwaiter().GetResult();

			_channel?.Close();
			_channel?.Dispose();
			_connection?.Close();
			_connection?.Dispose();
			base.Dispose();
		}
	}
}