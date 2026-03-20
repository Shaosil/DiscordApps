using ShaosilBot.Core.Models.SQLite;

namespace ShaosilBot.Core.Interfaces
{
	public interface ISQLiteProvider
	{
		void UpdateSchema();

		List<T> GetDataRecords<T>() where T : IEntity, new();

		T? GetDataRecord<T, TID>(TID id) where T : IEntity, new() where TID : struct;

		void UpsertDataRecords<T>(params T[] records) where T : IEntity, new();

		void DeleteDataRecords<T>(params T[] records) where T : IEntity;
	}
}