﻿using ShaosilBot.Core.Models.SQLite;

namespace ShaosilBot.Core.Interfaces
{
	public interface ISQLiteProvider
	{
		void UpdateSchema();

		T? GetDataRecord<T, TID>(TID id) where T : ITable, new() where TID : struct;

		void UpsertDataRecords<T>(params T[] records) where T : ITable, new();

		void DeleteDataRecords<T>(params T[] records) where T : ITable;
	}
}