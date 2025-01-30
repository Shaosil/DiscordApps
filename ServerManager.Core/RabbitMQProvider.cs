using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServerManager.Core.Interfaces;
using ServerManager.Core.Models;
using static ServerManager.Core.Models.QueueMessage;

namespace ServerManager.Core
{
	public class RabbitMQProvider : IRabbitMQProvider
	{
		private readonly ILogger<RabbitMQProvider> _logger;
		private readonly ConnectionFactory _factory;
		private IConnection? _connection;
		private IChannel _channel;
		private Dictionary<string, TaskCompletionSource<QueueMessageResponse>> _correlationTasks;

		public RabbitMQProvider(ILogger<RabbitMQProvider> logger)
		{
			_logger = logger;
			_factory = new ConnectionFactory { HostName = "localhost" };
			_correlationTasks = new Dictionary<string, TaskCompletionSource<QueueMessageResponse>>();
		}

		private async Task<bool> EnsureConnected()
		{
			try
			{
				// Initialize connection and response pipeline listener
				if (_connection == null)
				{
					_logger.LogInformation("Creating new connection to RabbitMQ.");
					_connection = await _factory.CreateConnectionAsync();
					_channel = await _connection.CreateChannelAsync();
				}

				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error opening connection");
				return false;
			}
		}

		public async Task<QueueMessageResponse> SendCommand(eCommandType commandType, string instructions, object[] args)
		{
			// Lazy load connection
			if (!(await EnsureConnected())) return new QueueMessageResponse("ERROR OPENING CONNECTION TO RABBITMQ");

			// Generate a new correlation ID and add it to our pending tasks
			string correlationID = $"{Guid.NewGuid()}";
			var tcs = new TaskCompletionSource<QueueMessageResponse>();
			_correlationTasks.Add(correlationID, tcs);

			// First, create a temporary queue for the callback
			var consumer = new AsyncEventingBasicConsumer(_channel);
			consumer.ReceivedAsync += HandleResponsePipeline;
			var tempQueue = await _channel.QueueDeclareAsync();
			await _channel.BasicConsumeAsync(queue: tempQueue.QueueName, false, consumer: consumer);

			// Then send the command over the typical pipeline, with a new correlation ID
			QueueMessage message = new QueueMessage
			{
				CommandType = commandType,
				Instructions = instructions,
				Arguments = args
			};
			var props = new BasicProperties { CorrelationId = correlationID, ReplyTo = tempQueue.QueueName };
			_logger.LogInformation($"Sending message {props.CorrelationId} to {QueueNames.COMMAND_QUEUE}...");
			await _channel.BasicPublishAsync(exchange: string.Empty, QueueNames.COMMAND_QUEUE, mandatory: true, basicProperties: props, body: message.Serialize());

			// Return the new task - will be given a result in the response pipeline handler
			return await tcs.Task;
		}

		private async Task HandleResponsePipeline(object? sender, BasicDeliverEventArgs eventArgs)
		{
			string correlationID = eventArgs.BasicProperties.CorrelationId!;
			if (!_correlationTasks.ContainsKey(correlationID))
			{
				_logger.LogError($"Received a response message without a matching task correlation! Correlation ID: {correlationID}");
				await _channel.BasicRejectAsync(eventArgs.DeliveryTag, false);
			}

			// Retrieve the corresponding task and set its completion status
			var tcs = _correlationTasks[correlationID];
			_correlationTasks.Remove(correlationID);
			var result = QueueMessageResponse.Deserialize(eventArgs.Body.ToArray());
			tcs.SetResult(result);

			await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);
		}
	}
}