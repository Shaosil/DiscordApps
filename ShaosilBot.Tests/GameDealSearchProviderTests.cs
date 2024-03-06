using Microsoft.Data.Sqlite;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Reflection;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class GameDealSearchProviderTests : TestBase<GameDealSearchProvider>
	{
		private ISQLiteProvider _sqliteProvider;
		private Mock<IDiscordRestClientProvider> _restClientProvider;
		private SqliteConnection _activeSqliteConnection;

		[TestInitialize]
		public void TestInitialize()
		{
			// Use a shared in memory database, leaving the connection open until the end of each test
			Configuration["FilesBasePath"] = string.Empty;
			Configuration["IsThereAnyDealChannel"] = "1190155474243436656";
			_sqliteProvider = new SQLiteProvider(new Mock<ILogger<SQLiteProvider>>().Object, Configuration);
			var privateConnString = typeof(SQLiteProvider).GetProperty(nameof(SQLiteProvider.ConnectionString), BindingFlags.Static | BindingFlags.Public);
			privateConnString!.SetValue(null, "Data Source=InMemory;Mode=Memory;Cache=Shared");
			_activeSqliteConnection = new SqliteConnection(SQLiteProvider.ConnectionString);
			_activeSqliteConnection.Open();
			_sqliteProvider.UpdateSchema();

			_restClientProvider = new Mock<IDiscordRestClientProvider>();
			SUT = new GameDealSearchProvider(Logger, Configuration, _restClientProvider.Object, _sqliteProvider, new HttpClientFactoryHelper());
		}

		[TestCleanup]
		public void TestCleanup()
		{
			// Close the shared connection after each test, removing the database
			_activeSqliteConnection.Close();
		}

		[TestMethod]
		public async Task Search_Generic_Test()
		{
			await SUT.DoDefaultSearch();
		}
	}
}