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
		private IModel _channel;
		private Dictionary<string, TaskCompletionSource<QueueMessageResponse>> _correlationTasks;

		public RabbitMQProvider(ILogger<RabbitMQProvider> logger)
		{
			_logger = logger;
			_factory = new ConnectionFactory { HostName = "localhost" };
			_correlationTasks = new Dictionary<string, TaskCompletionSource<QueueMessageResponse>>();
		}

		private bool EnsureConnected()
		{
			try
			{
				// Initialize connection and response pipeline listener
				if (_connection == null)
				{
					_logger.LogInformation("Creating new connection to RabbitMQ.");
					_connection = _factory.CreateConnection();
					_channel = _connection.CreateModel();
				}

				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error opening connection");
				return false;
			}
		}

		public Task<QueueMessageResponse> SendCommand(eCommandType commandType, string instructions, object[] args)
		{
			// Lazy load connection
			if (!EnsureConnected()) return Task.FromResult(new QueueMessageResponse("ERROR OPENING CONNECTION TO RABBITMQ"));

			// Generate a new correlation ID and add it to our pending tasks
			string correlationID = $"{Guid.NewGuid()}";
			var tcs = new TaskCompletionSource<QueueMessageResponse>();
			_correlationTasks.Add(correlationID, tcs);

			// First, create a temporary queue for the callback
			var consumer = new EventingBasicConsumer(_channel);
			consumer.Received += HandleResponsePipeline;
			var tempQueue = _channel.QueueDeclare();
			_channel.BasicConsume(queue: tempQueue.QueueName, false, consumer: consumer);

			// Then send the command over the typical pipeline, with a new correlation ID
			QueueMessage message = new QueueMessage
			{
				CommandType = commandType,
				Instructions = instructions,
				Arguments = args
			};
			var props = _channel.CreateBasicProperties();
			props.CorrelationId = correlationID;
			props.ReplyTo = tempQueue.QueueName;
			_logger.LogInformation($"Sending message {props.CorrelationId} to {QueueNames.COMMAND_QUEUE}...");
			_channel.BasicPublish(exchange: string.Empty, QueueNames.COMMAND_QUEUE, mandatory: true, basicProperties: props, body: message.Serialize());

			// Return the new task - will be given a result in the response pipeline handler
			return tcs.Task;
		}

		private void HandleResponsePipeline(object? sender, BasicDeliverEventArgs eventArgs)
		{
			string correlationID = eventArgs.BasicProperties.CorrelationId;
			if (!_correlationTasks.ContainsKey(correlationID))
			{
				_logger.LogError($"Received a response message without a matching task correlation! Correlation ID: {correlationID}");
				_channel.BasicReject(eventArgs.DeliveryTag, false);
				return;
			}

			// Retrieve the corresponding task and set its completion status
			var tcs = _correlationTasks[correlationID];
			_correlationTasks.Remove(correlationID);
			var result = QueueMessageResponse.Deserialize(eventArgs.Body.ToArray());
			tcs.SetResult(result);

			_channel.BasicAck(eventArgs.DeliveryTag, false);
		}
	}
}