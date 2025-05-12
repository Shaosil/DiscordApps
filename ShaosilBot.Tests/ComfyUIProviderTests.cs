using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Singletons;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class ComfyUIProviderTests : TestBase<ComfyUIProvider>
	{
		[TestInitialize]
		public void TestInitialize()
		{
			// Use a shared in memory database, leaving the connection open until the end of each test
			//Configuration["ComfyUIBaseURL"] = "http://127.0.0.1:9090/api/";
			Configuration["ComfyUIValidModels"] = "juggernautXL_juggXIByRundiffusion.safetensors,cyberrealisticXL_v56.safetensors,prefectPonyXL_v50.safetensors";
			SUT = new ComfyUIProvider(Logger, Configuration, new HttpClientFactoryHelper(), new Mock<IFileAccessHelper>().Object);
		}

		[TestMethod]
		public async Task Heartbeat_IsSuccessful()
		{
			Assert.IsTrue(await SUT.IsOnline());
		}

		[TestMethod]
		public async Task GetModels_Succeeds()
		{
			Assert.IsTrue((await SUT.GetModelsOfType("checkpoints")).Count() > 0);
		}
	}
}