﻿using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Singletons;
using System.Reflection;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class FileAccessHelperTests : TestBase<FileAccessHelper>
	{
		private Mock<IDiscordRestClientProvider> _restClientProviderMock;
		private static string _testDir;

		private record TestJson(Guid guid, string str, ulong num) { public TestJson() : this(Guid.Empty, string.Empty, default) { } };

		[ClassInitialize]
		public static void ClassInit(TestContext context)
		{
			// Create test file path if needed and delete all existing files
			_testDir = Path.Combine(Environment.CurrentDirectory, "TestFiles");
			var testDir = new DirectoryInfo(_testDir);
			if (!testDir.Exists) testDir.Create();
			foreach (var file in testDir.GetFiles()) file.Delete();
		}

		[TestInitialize]
		public void TestInit()
		{
			Configuration["FilesBasePath"] = _testDir;
			_restClientProviderMock = new Mock<IDiscordRestClientProvider>();

			var loggerMock = new Mock<ILogger<FileAccessHelper>>();
			SUT = new FileAccessHelper(Logger, Configuration, _restClientProviderMock.Object);
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
			var thread2StartSignal = new ManualResetEventSlim();
			string fileName = $"{Guid.NewGuid()}.json";
			var initialData = new TestJson(Guid.NewGuid(), "Initial Value", Random.Shared.NextULong());
			Action intensiveJob = () =>
			{
				var data = SUT.LoadFileJSON<TestJson>(fileName, true);                              // Get file and lock
				thread2StartSignal.Set();                                                           // Signal unit test to continue
				Thread.Sleep(1000);                                                                 // Simulate work
				data = new TestJson(Guid.NewGuid(), "Updated Value", Random.Shared.NextULong());    // Update data
				SUT.SaveFileJSON(fileName, data);                                                   // Save and release lock
			};

			// Act - Spin up the job while trying to read the file contents shortly after
			Task.Run(intensiveJob);
			thread2StartSignal.Wait();
			var updatedData = SUT.LoadFileJSON<TestJson>(fileName);

			// Assert - Job 2 should see the data written by job 1
			Assert.IsNotNull(updatedData);
			Assert.AreNotEqual(initialData.guid, updatedData.guid);
			Assert.AreNotEqual(initialData.str, updatedData.str);
			Assert.AreNotEqual(initialData.str, updatedData.str);
		}

		[TestMethod]
		public void DeadlockDoesNotBlock()
		{
			// Arrange - Get and keep a lock on a file, and prepare to capture log message
			string dmMessage = string.Empty;
			string fileName = $"{Guid.NewGuid()}.txt";
			SUT.LoadFileText(fileName, true);
			_restClientProviderMock.Setup(m => m.DMShaosil(It.IsAny<string>())).Callback<string>(s => dmMessage = s);

			// Act - Attempt to access the same file (override the waiting period to be 1 second)
			SUT.GetType().GetField("_lockWaitMaxMs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(SUT, 1000);
			SUT.LoadFileText(fileName);

			// Assert - If we get here at all that is half the test, so now just verify a DM message was sent (check test log as well if desired)
			_restClientProviderMock.Verify(m => m.DMShaosil(It.IsAny<string>()), Times.Once);
			Assert.IsTrue(dmMessage.Contains("deadlock"));
		}
	}
}