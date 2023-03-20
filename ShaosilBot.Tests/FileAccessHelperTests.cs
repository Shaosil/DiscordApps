using ShaosilBot.Core.Singletons;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class FileAccessHelperTests : LoggerTestBase<FileAccessHelper>
	{
		private FileAccessHelper SUT;
		private Mock<IConfiguration> _mockConfig;
		private string _testDir;

		private record TestJson(Guid guid, string str, ulong num) { public TestJson() : this(Guid.Empty, string.Empty, default) { } };

		[TestInitialize]
		public void TestInit()
		{
			// Create test file path if needed and delete all existing files
			_testDir = Path.Combine(Environment.CurrentDirectory, "TestFiles");
			var testDir = new DirectoryInfo(_testDir);
			if (!testDir.Exists) testDir.Create();
			foreach (var file in testDir.GetFiles()) file.Delete();

			_mockConfig = new Mock<IConfiguration>();
			_mockConfig.Setup(c => c["FilesBasePath"]).Returns(_testDir);

			var loggerMock = new Mock<ILogger<FileAccessHelper>>();
			SUT = new FileAccessHelper(Logger, _mockConfig.Object);
		}

		[TestMethod]
		public void TextFileCreatedIfNotExists()
		{
			// Arrange - Random filename
			string fileName = $"{Guid.NewGuid()}.txt";

			// Act - Load text file
			SUT.LoadFileText(fileName);

			// Assert - Verify existance
			Assert.IsTrue(File.Exists(Path.Combine(_testDir, fileName)));
		}

		[TestMethod]
		public void JSONFileCreatedIfNotExists()
		{
			// Arrange - Random filename
			string fileName = $"{Guid.NewGuid()}.json";

			// Act - Load JSON file
			SUT.LoadFileJSON<TestJson>(fileName);

			// Assert - Verify existance
			Assert.IsTrue(File.Exists(Path.Combine(_testDir, fileName)));
		}

		[TestMethod]
		public void JSONFileSavedCorrectly()
		{
			// Arrange
			string fileName = $"{Guid.NewGuid()}.json";
			var dataToSave = new TestJson(Guid.NewGuid(), "This is a test", Random.Shared.NextULong());

			// Act
			SUT.SaveFileJSON(fileName, dataToSave);
			var loadedData = SUT.LoadFileJSON<TestJson>(fileName);

			// Assert
			Assert.IsNotNull(loadedData);
			Assert.AreEqual(dataToSave.guid, loadedData.guid);
			Assert.AreEqual(dataToSave.str, loadedData.str);
			Assert.AreEqual(dataToSave.num, loadedData.num);
		}

		[TestMethod]
		public void FileLockPreventsAccess()
		{
			// Arrange - Set up a job to open and lock a file, simulate work, and release it
			string fileName = $"{Guid.NewGuid()}.json";
			var initialData = new TestJson(Guid.NewGuid(), "Initial Value", Random.Shared.NextULong());
			Action intensiveJob = () =>
			{
				var data = SUT.LoadFileJSON<TestJson>(fileName, true);                              // Get file and lock
				Thread.Sleep(1000);                                                                 // Simulate work
				data = new TestJson(Guid.NewGuid(), "Updated Value", Random.Shared.NextULong());    // Update data
				SUT.SaveFileJSON(fileName, data);                                                   // Save and release lock
			};

			// Act - Spin up the job while trying to read the file contents shortly after
			Task.Run(intensiveJob);
			Thread.Sleep(100);
			var updatedData = SUT.LoadFileJSON<TestJson>(fileName);

			// Assert - Job 2 should see the data written by job 1
			Assert.IsNotNull(updatedData);
			Assert.AreNotEqual(initialData.guid, updatedData.guid);
			Assert.AreNotEqual(initialData.str, updatedData.str);
			Assert.AreNotEqual(initialData.str, updatedData.str);
		}
	}
}