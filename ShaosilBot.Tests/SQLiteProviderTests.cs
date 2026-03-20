using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using ShaosilBot.Core.Providers;
using ShaosilBot.Core.Singletons;
using System.Reflection;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class SQLiteProviderTests : TestBase<SQLiteProvider>
	{
		private static string _testDir;

		[ClassInitialize]
		public static void ClassInit(TestContext context)
		{
			// Create test file path if needed and delete existing db
			_testDir = "./TESTDATA/";
			var testDir = new DirectoryInfo(_testDir);
			if (!testDir.Exists) testDir.Create();
			var testdb = new FileInfo(Path.Combine(_testDir, "testData.db"));
			if (testdb.Exists) testdb.Delete();
		}

		[TestInitialize]
		public void TestInit()
		{
			Configuration["FilesBasePath"] = _testDir;

			SUT = new SQLiteProvider(Logger, Configuration);
			SUT.UpdateSchema();
		}

		[TestMethod]
		public void TestRecordCanBeAddedAndRetrieved()
		{
			// Arrange - Prepare random new record test data - Add random record to known table
			var newData = new WordleGame { WordID = "ATEST", UserID = 1234, IsDaily = true };
			
			// Act - Upsert and fetch
			SUT.UpsertDataRecords([newData]);
			var retrievedData = SUT.GetDataRecords<WordleGame>().First(w => w.UserID == newData.UserID && w.IsDaily == newData.IsDaily);

			// Assert - Should be no error
			Assert.IsNotNull(retrievedData);
			Assert.AreEqual(newData.WordID, retrievedData.WordID);
		}
	}
}