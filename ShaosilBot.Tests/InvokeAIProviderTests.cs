﻿using ShaosilBot.Core.Singletons;
using System.Text.RegularExpressions;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class InvokeAIProviderTests : TestBase<InvokeAIProvider>
	{
		[TestInitialize]
		public void TestInitialize()
		{
			// Use a shared in memory database, leaving the connection open until the end of each test
			Configuration["InvokeAIBaseURL"] = "http://127.0.0.1:9090/api/v1/";
			Configuration["InvokeAIValidModels"] = "sdXL_v10VAEFix,newrealityxlAllInOne_21,dreamshaperXL_v21TurboDPMSDE";
			SUT = new InvokeAIProvider(Logger, Configuration, new HttpClientFactoryHelper());
		}

		[TestMethod]
		public async Task Heartbeat_IsSuccessful()
		{
			Assert.IsTrue(await SUT.IsOnline());
		}

		[TestMethod]
		public async Task GetModels_Succeeds()
		{
			Assert.IsTrue((await SUT.GetModels()).Count > 0);
		}

		[TestMethod]
		public void MyTestMethod()
		{
			var test = Regex.Match("Image Generation-Delete-4BF5A8DE-05C2-4D5C-B4C1-B211D2CECCB4", "(.+?)-(.+?)-(.+)");
		}
	}
}