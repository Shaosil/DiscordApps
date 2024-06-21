using ShaosilBot.Core.Models.SQLite;
using System.Linq.Expressions;

namespace ShaosilBot.Core.Interfaces
{
	public interface ISQLiteProvider
	{
		void UpdateSchema();

		List<T> GetDataRecords<T>(Expression<Func<T, bool>>? filterQuery = null) where T : ITable, new();

		T? GetDataRecord<T, TID>(TID id) where T : ITable, new() where TID : struct;

		void UpsertDataRecords<T>(params T[] records) where T : ITable, new();

		void DeleteDataRecords<T>(params T[] records) where T : ITable;
	}
}