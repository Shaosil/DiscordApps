﻿namespace ShaosilBot.Core.Interfaces
{
	public interface IDiscordSocketClientProvider
	{
		Task Init(bool isDevelopment);
	}
}