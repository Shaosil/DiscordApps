using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Text;

namespace ShaosilBot.Core.Providers
{
	public class SQLiteProvider : ISQLiteProvider
	{
		private readonly ILogger<SQLiteProvider> _logger;
		private readonly Dictionary<Type, Dictionary<object, ITable>> _tableCache = new(); // Caches entities by type and PK

		public static string ConnectionString { get; private set; }

		public SQLiteProvider(ILogger<SQLiteProvider> logger, IConfiguration configuration)
		{
			_logger = logger;
			if (string.IsNullOrWhiteSpace(ConnectionString))
			{
				ConnectionString = $"Data Source={Path.Combine(configuration.GetValue<string>("FilesBasePath")!, "data.db")}";
			}
		}

		#region Schema Update Methods

		public void UpdateSchema()
		{
			// Create or delete tables based on existing schema
			string ns = typeof(ITable).Namespace!;
			var ourTables = GetType().Assembly.GetTypes().Where(t => t.Namespace == ns && !t.IsInterface && t.IsAssignableTo(typeof(ITable))).ToList();
			List<string> existingTableNames = GetSimpleData<string>("SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'QRTZ_%' AND name NOT LIKE 'sqlite_%'");
			foreach (var table in ourTables.Where(t => !existingTableNames.Contains($"{t.Name}s"))) CreateTable(table);
			foreach (var table in existingTableNames.Where(t => !ourTables.Any(ot => $"{ot.Name}s" == t))) DropTable(table);

			// Compare columns between all of our tables and their matching counterparts
			using (var conn = new SqliteConnection(ConnectionString))
			{
				var cmd = conn.CreateCommand();
				conn.Open();

				foreach (var ourTable in ourTables)
				{
					var curColDefs = GetCodeColumnDefinitions(ourTable);
					var existingColDefs = GetDBColumnDefinitions($"{ourTable.Name}s");

					// Remove columns that no longer exist
					foreach (var colToDelete in existingColDefs.Where(e => !curColDefs.Any(c => c.Name == e.Name)))
					{
						_logger.LogInformation($"Removing column '{colToDelete.Name}' from '{ourTable.Name}s'");
						cmd.CommandText = $"ALTER TABLE {ourTable.Name}s DROP COLUMN {colToDelete.Name}";
						cmd.ExecuteNonQuery();
					}

					// Add columns that do not exist
					foreach (var colToAdd in curColDefs.Where(c => !existingColDefs.Any(e => e.Name == c.Name)))
					{
						_logger.LogInformation($"Adding column '{colToAdd.Name}' to '{ourTable.Name}s'");
						cmd.CommandText = $"ALTER TABLE {ourTable.Name}s ADD COLUMN {colToAdd}";
						cmd.ExecuteNonQuery();
					}

					// TODO: Modify columns that have different types or constraints
				}
			}
		}

		private List<T> GetSimpleData<T>(string selectClause, Dictionary<string, object>? parameters = null)
		{
			List<T> data = new();

			using (var conn = new SqliteConnection(ConnectionString))
			{
				conn.Open();
				var cmd = conn.CreateCommand();
				cmd.CommandText = selectClause;
				foreach (var param in parameters ?? new Dictionary<string, object>())
				{
					cmd.Parameters.AddWithValue(param.Key, param.Value);
				}
				using (var reader = cmd.ExecuteReader())
				{
					while (reader.Read()) data.Add(reader.GetFieldValue<T>(0));
				}
			}

			return data;
		}

		private void CreateTable(Type table)
		{
			_logger.LogInformation($"Creating table '{table.Name}s'");
			var propertyDefinitions = GetCodeColumnDefinitions(table).Select(p => $"\t{p}").ToList();

			// PK detection - Create table scoped PK if there is no autoincrement column
			var constraints = new List<string>();
			var pk = GetColumnProperties(table).First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null); // TODO - Enforce and support 1+ PK
			if (!pk.GetCustomAttribute<PrimaryKeyAttribute>()!.AutoIncrement)
			{
				constraints.Add($"\tPRIMARY KEY ({pk.Name})");
			}

			using (var conn = new SqliteConnection(ConnectionString))
			{
				var tableBuilder = new StringBuilder();
				tableBuilder.AppendLine($"CREATE TABLE {table.Name}s");
				tableBuilder.AppendLine("(");
				tableBuilder.AppendLine(string.Join($",{Environment.NewLine}", propertyDefinitions.Concat(constraints)));
				tableBuilder.AppendLine(")");

				conn.Open();
				var cmd = conn.CreateCommand();
				cmd.CommandText = tableBuilder.ToString();
				cmd.ExecuteNonQuery();
			}
		}

		private record ColumnDefinition(string Name, string Type, List<string> Constraints)
		{
			public override string ToString() => $"[{Name}] {Type}{(Constraints.Any() ? " " : string.Empty)}{string.Join(" ", Constraints)}";
		}

		// Official datatype conversions - https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types
		private readonly IReadOnlyDictionary<string, Type[]> NetToSQLiteTypes = new Dictionary<string, Type[]>
		{
			{ "INTEGER", new[] { typeof(bool), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong) } },
			{ "REAL", new[] { typeof(double), typeof(float) } },
			{ "TEXT", new[] { typeof(char), typeof(DateOnly), typeof(DateTime), typeof(DateTimeOffset), typeof(decimal), typeof(string), typeof(TimeOnly), typeof(TimeSpan) } },
			{ "BLOB", new[] { typeof(byte[]) } }
		};

		private List<PropertyInfo> GetColumnProperties(Type tableClass)
		{
			// Return all properties that are not marked as NotMapped and have an official conversion defined
			return tableClass.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null
				&& NetToSQLiteTypes.Any(t => t.Value.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))).ToList();
		}

		private List<ColumnDefinition> GetCodeColumnDefinitions(Type tableClass)
		{
			List<ColumnDefinition> definitions = new();

			var propColumns = GetColumnProperties(tableClass);
			foreach (var prop in propColumns)
			{
				// Load type from defined list
				List<string> constraints = new();
				var nullableType = Nullable.GetUnderlyingType(prop.PropertyType);
				string cType = NetToSQLiteTypes.First(t => t.Value.Contains(nullableType ?? prop.PropertyType)).Key;

				// Only include the PK clause if this is an autoincremented int
				bool autoIncrement = prop.GetCustomAttribute<PrimaryKeyAttribute>()?.AutoIncrement ?? false;
				if (cType == "INTEGER" && autoIncrement) constraints.Add("PRIMARY KEY AUTOINCREMENT");

				// Apart from required columns, mark value types (that are not nullable) as NOT NULL
				bool required = prop.GetCustomAttribute<RequiredAttribute>() != null;
				if (required || (prop.PropertyType.IsValueType && nullableType == null)) constraints.Add("NOT NULL");

				// Foreign key
				var fkAttr = prop.GetCustomAttribute(typeof(ForeignKeyAttribute<>));
				Type? fkTable = fkAttr?.GetType().GetProperty(nameof(ForeignKeyAttribute<ITable>.ReferenceTable))!.GetValue(fkAttr) as Type;
				string? fkCol = fkAttr?.GetType().GetProperty(nameof(ForeignKeyAttribute<ITable>.ReferenceColumn))!.GetValue(fkAttr) as string;
				if (fkTable != null && fkCol != null) constraints.Add($"REFERENCES {fkTable.Name}s ({fkCol})");

				definitions.Add(new ColumnDefinition(prop.Name, cType, constraints));
			}

			return definitions;
		}

		private List<ColumnDefinition> GetDBColumnDefinitions(string table)
		{
			List<ColumnDefinition> definitions = new();

			// Autoincrement info
			bool hasAutoIncrement = GetSimpleData<string>($"SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = @table", new() { { "@table", table } })
				.First().ToUpper().Contains("AUTOINCREMENT");
			using (var conn = new SqliteConnection(ConnectionString))
			{
				conn.Open();
				var cmd = conn.CreateCommand();
				cmd.CommandText = $"SELECT name, type, [notnull], pk FROM pragma_table_info(@table)";
				cmd.Parameters.AddWithValue("@table", table);
				using (var reader = cmd.ExecuteReader())
				{
					while (reader.Read())
					{
						List<string> constraints = new();

						// Name and type
						string name = reader.GetFieldValue<string>(0);
						string cType = reader.GetFieldValue<string>(1);

						// Not null constraint
						if (reader.GetFieldValue<bool>(2)) constraints.Add("NOT NULL");

						// PK constraint (only if autoincrement)
						if (hasAutoIncrement && reader.GetFieldValue<bool>(3)) constraints.Add("PRIMARY KEY AUTOINCREMENT");

						// FK constraint
						var parameters = new Dictionary<string, object> { { "@table", table }, { "@name", name } };
						string? fk = GetSimpleData<string>($"SELECT 'REFERENCES ' || [table] || ' (' || [to] || ')' FROM pragma_foreign_key_list(@table) WHERE [from] = @name", parameters).FirstOrDefault();
						if (!string.IsNullOrWhiteSpace(fk)) constraints.Add(fk);

						definitions.Add(new ColumnDefinition(name, cType, constraints));
					}
				}
			}

			return definitions;
		}

		private void DropTable(string table)
		{
			_logger.LogInformation($"Dropping table '{table}'");
			using (var conn = new SqliteConnection(ConnectionString))
			{
				conn.Open();
				var cmd = conn.CreateCommand();
				cmd.CommandText = $"DROP TABLE {table}";
				cmd.ExecuteNonQuery();
			}
		}

		#endregion

		public T? GetDataRecord<T, TID>(TID primaryKeyValue) where T : ITable, new() where TID : struct
		{
			// If we already have it cached, return it
			if (_tableCache.ContainsKey(typeof(T)))
			{
				var typeCache = _tableCache[typeof(T)];
				if (typeCache.ContainsKey(primaryKeyValue)) return (T)typeCache[primaryKeyValue];
			}
			else
			{
				_tableCache[typeof(T)] = new();
			}

			var propColumns = GetColumnProperties(typeof(T));
			var pkCol = propColumns.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

			var result = new T();
			using (var conn = new SqliteConnection(ConnectionString))
			{
				var cmd = conn.CreateCommand();
				cmd.CommandText = $"SELECT * FROM {typeof(T).Name}s WHERE {pkCol.Name} = @pkVal";
				cmd.Parameters.AddWithValue("@pkVal", primaryKeyValue);
				conn.Open();

				using (var reader = cmd.ExecuteReader())
				{
					// Load direct properties
					reader.Read();
					foreach (var propColumn in propColumns)
					{
						object? val;
						var trueType = Nullable.GetUnderlyingType(propColumn.PropertyType) ?? propColumn.PropertyType;
						switch (trueType)
						{
							// Some types need a specific read
							case Type t when t == typeof(bool):
								val = reader.IsDBNull(propColumn.Name) ? null : reader.GetBoolean(propColumn.Name);
								break;

							// The rest can be read in as strings and converted like this
							default:
								val = reader.IsDBNull(propColumn.Name) ? null : reader.GetString(propColumn.Name);
								if (val != null) val = TypeDescriptor.GetConverter(propColumn.PropertyType).ConvertFrom(val)!;
								break;
						}
						propColumn.SetValue(result, val);
					}
				}
			}

			// Cache result
			if (!_tableCache.ContainsKey(typeof(T))) _tableCache[typeof(T)] = new();
			_tableCache[typeof(T)][pkCol.GetValue(result)!] = result;

			// Recursively load connected entity info via reflection. Infinite circular references should be prevented by the cache.
			if (result != null)
			{
				var allProps = typeof(T).GetProperties();
				var connectedSingleEntities = allProps.Where(p => p.PropertyType.IsAssignableTo(typeof(ITable))).ToList();
				var connectedMultiEntities = allProps.Where(p => p.PropertyType.IsAssignableTo(typeof(IEnumerable<ITable>))).ToList();
				var genericGetRecord = GetType().GetMethod(nameof(GetDataRecord))!;

				foreach (var singleEntity in connectedSingleEntities)
				{
					// Find the FK constraint for the current type
					var fkIDProp = allProps.FirstOrDefault(p => GetForeignKeyAttributeType(p) == singleEntity.PropertyType);
					if (fkIDProp == null) continue;

					// Recrusively load the target FK type single record based on this entity's FK value
					var curMethod = genericGetRecord.MakeGenericMethod(singleEntity.PropertyType, fkIDProp.PropertyType);
					var loadedEntity = curMethod.Invoke(this, new[] { fkIDProp.GetValue(result) });
					singleEntity.SetValue(result, loadedEntity);
				}

				foreach (var multiEntity in connectedMultiEntities)
				{
					// Get the generic type of the current enumerable
					var listType = multiEntity.PropertyType.GenericTypeArguments[0];
					var listTypeProps = GetColumnProperties(listType);

					// Get the FK property of that type
					var fkIDProp = listTypeProps.FirstOrDefault(p => p.GetCustomAttribute<ForeignKeyAttribute<T>>() != null);
					if (fkIDProp == null) continue;

					// Get the PK property of that type
					var pkIDProp = listTypeProps.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

					// Get target PK all records where the FK property is equal to this ID
					var stringIDs = GetSimpleData<string>($"SELECT [{pkIDProp.Name}] FROM {listType.Name}s WHERE [{fkIDProp.Name}] = @pkVal", new() { { "@pkVal", primaryKeyValue } });

					// Recursively load for each ID of that type
					var curMethod = genericGetRecord.MakeGenericMethod(listType, fkIDProp.PropertyType);
					var loadedEntities = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(listType))!;
					foreach (string sid in stringIDs)
					{
						var converter = TypeDescriptor.GetConverter(fkIDProp.PropertyType);
						loadedEntities.Add(curMethod.Invoke(this, new[] { converter.ConvertFrom(sid) }));
					}
					multiEntity.SetValue(result, loadedEntities);
				}
			}

			return result;
		}

		public void UpsertDataRecords<T>(params T[] records) where T : ITable, new()
		{
			// Exclude autoincrement PKs from upsert
			var propColumns = GetColumnProperties(typeof(T));
			var nonPkColumns = propColumns.Where(p => !(p.GetCustomAttribute<PrimaryKeyAttribute>()?.AutoIncrement ?? false)).ToList();

			var upsertBuilder = new StringBuilder();
			upsertBuilder.AppendLine($"INSERT INTO {typeof(T).Name}s ({string.Join(", ", nonPkColumns.Select(c => $"[{c.Name}]"))}) VALUES");
			var recordVals = records.Select((r, i) => $"({string.Join(", ", nonPkColumns.Select(p => $"@{p.Name}_{i}"))})");
			upsertBuilder.AppendLine(string.Join($",{Environment.NewLine}", recordVals));
			upsertBuilder.AppendLine("ON CONFLICT DO UPDATE SET");
			upsertBuilder.AppendLine(string.Join($",{Environment.NewLine}", nonPkColumns.Select(c => $"[{c.Name}] = excluded.[{c.Name}]")));

			using (var conn = new SqliteConnection(ConnectionString))
			{
				var cmd = conn.CreateCommand();
				cmd.CommandText = upsertBuilder.ToString();
				foreach (var col in nonPkColumns)
				{
					for (int i = 0; i < records.Length; i++)
					{
						cmd.Parameters.AddWithValue($"@{col.Name}_{i}", col.GetValue(records[i]) ?? DBNull.Value);
					}
				}
				conn.Open();
				cmd.ExecuteNonQuery();
			}

			// Set any parent FK single properties in this class
			var iTableProps = typeof(T).GetProperties().Where(p => p.PropertyType.IsAssignableTo(typeof(ITable))).ToList();
			var fkColumns = nonPkColumns.Where(p => p.GetCustomAttribute(typeof(ForeignKeyAttribute<>)) != null).ToList();
			foreach (var fkColumn in fkColumns)
			{
				// Get the matching FK type
				var fkAttr = fkColumn.GetCustomAttribute(typeof(ForeignKeyAttribute<>))!;
				var fkType = fkAttr.GetType().GetProperty(nameof(ForeignKeyAttribute<ITable>.ReferenceTable))!.GetValue(fkAttr) as Type;

				// Find the matching single property type
				var matchingFKProp = iTableProps.FirstOrDefault(p => p.PropertyType == fkType);
				if (matchingFKProp == null) continue;

				foreach (var record in records)
				{
					// Load the value from cache
					var parentVal = _tableCache[matchingFKProp.PropertyType][fkColumn.GetValue(record)!];
					matchingFKProp.SetValue(record, parentVal);
				}
			}

			// Insert this item to any List<T> in linked parent entities that do not have it already
			UpdateParentListsFromChildEntities(true, records);
		}

		public void DeleteDataRecords<T>(params T[] records) where T : ITable
		{
			// Get the PK column of this type and remove all matching records
			var pkColumn = GetColumnProperties(typeof(T)).First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

			using (var conn = new SqliteConnection(ConnectionString))
			{
				var cmd = conn.CreateCommand();
				cmd.CommandText = $"DELETE FROM {typeof(T).Name}s WHERE {pkColumn.Name} IN ({string.Join(", ", records.Select(r => $"'{pkColumn.GetValue(r)}'"))})";
				conn.Open();
				cmd.ExecuteNonQuery();

				// Manually update sequence info
				cmd.CommandText = $"UPDATE sqlite_sequence SET seq = (SELECT MAX({pkColumn.Name}) FROM {typeof(T).Name}s) WHERE name = '{typeof(T).Name}s'";
				cmd.ExecuteNonQuery();
			}

			// Remove this item from any List<T> in linked parent entities
			UpdateParentListsFromChildEntities(false, records);
		}

		private Type? GetForeignKeyAttributeType(PropertyInfo prop)
		{
			var fkAttr = prop.GetCustomAttribute(typeof(ForeignKeyAttribute<>));
			return fkAttr?.GetType().GetProperty(nameof(ForeignKeyAttribute<ITable>.ReferenceTable))!.GetValue(fkAttr) as Type;
		}

		private void UpdateParentListsFromChildEntities<T>(bool isUpsert, params T[] childEntities) where T : ITable
		{
			// Remove from or update cache
			var propColumns = GetColumnProperties(typeof(T));
			var pkCol = propColumns.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);
			foreach (var child in childEntities)
			{
				if (isUpsert)
				{
					if (!_tableCache.ContainsKey(typeof(T))) _tableCache[typeof(T)] = new();
					_tableCache[typeof(T)][pkCol.GetValue(child)!] = child;
				}
				else
				{
					var pkVal = pkCol.GetValue(child)!;
					if (_tableCache.ContainsKey(typeof(T)) && _tableCache[typeof(T)].ContainsKey(pkVal))
					{
						_tableCache[typeof(T)].Remove(pkVal);
					}
				}
			}

			var parentRecordProps = typeof(T).GetProperties().Where(p => p.PropertyType.IsAssignableTo(typeof(ITable))).ToList();
			foreach (var parentRecord in parentRecordProps)
			{
				var listsOfUs = parentRecord.PropertyType.GetProperties().Where(p => p.PropertyType.IsAssignableTo(typeof(IEnumerable<T>))).ToList();
				foreach (var theListOfUs in listsOfUs)
				{
					foreach (var child in childEntities)
					{
						// Get the FK column of this type for the current parent
						var fkIdProp = propColumns.FirstOrDefault(p => GetForeignKeyAttributeType(p) == parentRecord.PropertyType);
						if (fkIdProp == null) continue;

						// Load parent object from cache
						var parentVal = _tableCache[parentRecord.PropertyType][fkIdProp.GetValue(child)!];

						if (isUpsert)
						{
							// Upsert
							var listVal = (List<T>)theListOfUs.GetValue(parentVal)!;
							var matchingRecord = listVal.FirstOrDefault(r => r.Equals(child));
							if (matchingRecord == null)
							{
								listVal.Add(child);
							}
						}
						else
						{
							// Delete
							var listVal = (List<T>)theListOfUs.GetValue(parentVal)!;
							listVal.Remove(child);
						}
					}
				}
			}
		}
	}
}