namespace ShaosilBot.Tests
{
	[TestClass]
	public abstract class LoggerTestBase<T>
	{
		protected static ILogger<T> Logger { get; private set; }

		[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
		public static void ClassInit(TestContext context)
		{
			Logger = new LoggerWrapper<T>();
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