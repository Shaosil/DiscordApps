using Quartz;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Jobs
{
	public class FillMonthlyChatGPTTokensJob : IJob
	{
		private readonly IChatGPTProvider _chatGPTProvider;

		public FillMonthlyChatGPTTokensJob(IChatGPTProvider chatGPTProvider)
		{
			_chatGPTProvider = chatGPTProvider;
		}

		public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
		{
			await _chatGPTProvider.ResetAndFillAllUserBuckets();
		}
	}
}