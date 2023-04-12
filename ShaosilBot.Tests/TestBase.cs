namespace ShaosilBot.Tests
{
	/// <summary>
	/// Provides wrapper functionality for ILogger<typeparamref name="T"/> and IConfiguration if needed
	/// </summary>
	/// <typeparam name="T"></typeparam>
	[TestClass]
	public abstract class TestBase<T>
	{
		protected ILogger<T> Logger { get; private set; }
		protected IConfiguration Configuration { get; private set; }

		[TestInitialize]
		public void LoggerBaseTestInit()
		{
			Logger = new LoggerWrapper<T>();
			Configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
		}

		public class LoggerWrapper<LT> : ILogger<T>
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull
			{
				throw new NotImplementedException();
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				throw new NotImplementedException();
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			{
				// Basic console logging
				Console.WriteLine($"{logLevel} - (T:{Thread.CurrentThread.ManagedThreadId,3}): {state}{(exception != null ? $"\n{exception}" : string.Empty)}");
			}
		}
	}
}