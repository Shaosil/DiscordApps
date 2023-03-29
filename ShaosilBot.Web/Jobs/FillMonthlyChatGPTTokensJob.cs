using Quartz;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Web.Jobs
{
	public class FillMonthlyChatGPTTokensJob : IJob
	{
		private readonly IChatGPTProvider _chatGPTProvider;

		public FillMonthlyChatGPTTokensJob(IChatGPTProvider chatGPTProvider)
		{
			_chatGPTProvider = chatGPTProvider;
		}

		public Task Execute(IJobExecutionContext context)
		{
			_chatGPTProvider.ResetAndFillAllUserBuckets();

			return Task.FromResult(true);
		}
	}
}