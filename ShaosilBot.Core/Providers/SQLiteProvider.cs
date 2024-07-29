using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Linq.Expressions;
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
				var connStringBuilder = new SqliteConnectionStringBuilder();
				connStringBuilder.DataSource = Path.Combine(configuration.GetValue<string>("FilesBasePath")!, "data.db");
				connStringBuilder.ForeignKeys = true;

				ConnectionString = connStringBuilder.ToString();
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

			var allProps = GetColumnProperties(table);

			// PK detection - Create table scoped PK if there is no autoincrement column
			var constraints = new List<string>();
			var pk = allProps.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null); // TODO - Enforce and support 1+ PK
			if (!pk.GetCustomAttribute<PrimaryKeyAttribute>()!.AutoIncrement)
			{
				constraints.Add($"\tPRIMARY KEY ({pk.Name})");
			}

			// FK detection
			var fkProps = allProps.Where(p => p.GetCustomAttribute<ForeignKeyAttribute>() != null).ToList();
			foreach (var fkProp in fkProps)
			{
				var curFk = fkProp.GetCustomAttribute<ForeignKeyAttribute>()!;
				constraints.Add($"\tFOREIGN KEY ([{fkProp.Name}]) REFERENCES {curFk.ReferenceTable.Name}s ([{curFk.ReferenceColumn}]) ON DELETE CASCADE ON UPDATE CASCADE");
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
			{ "TEXT", new[] { typeof(char), typeof(DateOnly), typeof(DateTime), typeof(DateTimeOffset), typeof(decimal), typeof(string), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid) } },
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

		public List<T> GetDataRecords<T>(Expression<Func<T, bool>>? filterQuery) where T : ITable, new()
		{
			// If the table type is not yet cached, get all records
			if (!_tableCache.ContainsKey(typeof(T)))
			{
				_tableCache[typeof(T)] = new();

				var propColumns = GetColumnProperties(typeof(T));
				var pkCol = propColumns.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

				using (var conn = new SqliteConnection(ConnectionString))
				{
					var cmd = conn.CreateCommand();
					cmd.CommandText = $"SELECT * FROM {typeof(T).Name}s";

					// Add a where clause if a filter query was provided
					if (filterQuery != null)
					{
						cmd.CommandText += new FilterTranslator<T>().GetWhereClause(filterQuery);
					}

					conn.Open();

					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							var result = new T();
							PopulateObjectData(result, propColumns, pkCol, reader);
						}
					}
				}
			}

			// Everything will be cached now, so return it from that
			return _tableCache[typeof(T)].Values.Cast<T>().ToList();
		}

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
					if (reader.Read())
					{
						PopulateObjectData(result, propColumns, pkCol, reader);
					}
				}
			}

			return result;
		}

		private void PopulateObjectData<T>(T item, List<PropertyInfo> itemProperties, PropertyInfo pkCol, SqliteDataReader openReader) where T : ITable
		{
			foreach (var propColumn in itemProperties)
			{
				object? val;
				var trueType = Nullable.GetUnderlyingType(propColumn.PropertyType) ?? propColumn.PropertyType;
				switch (trueType)
				{
					// Some types need a specific read
					case Type t when t == typeof(bool):
						val = openReader.IsDBNull(propColumn.Name) ? null : openReader.GetBoolean(propColumn.Name);
						break;

					// The rest can be read in as strings and converted like this
					default:
						val = openReader.IsDBNull(propColumn.Name) ? null : openReader.GetString(propColumn.Name);
						if (val != null) val = TypeDescriptor.GetConverter(propColumn.PropertyType).ConvertFrom(val)!;
						break;
				}
				propColumn.SetValue(item, val);
			}

			// Cache result
			object pkVal = pkCol.GetValue(item)!;
			_tableCache[typeof(T)][pkVal] = item;

			// Recursively load connected entity info via reflection. Infinite circular references should be prevented by the cache.
			var allProps = typeof(T).GetProperties();
			var connectedSingleEntities = allProps.Where(p => p.PropertyType.IsAssignableTo(typeof(ITable))).ToList();
			var connectedMultiEntities = allProps.Where(p => p.PropertyType.IsAssignableTo(typeof(IEnumerable<ITable>))).ToList();
			var genericGetRecord = GetType().GetMethod(nameof(GetDataRecord))!;

			foreach (var singleEntity in connectedSingleEntities)
			{
				// Find the FK constraint for the current type
				var fkIDProp = allProps.FirstOrDefault(p => p.GetCustomAttribute<ForeignKeyAttribute>()?.ReferenceTable == singleEntity.PropertyType);
				if (fkIDProp == null) continue;

				// Recrusively load the target FK type single record based on this entity's FK value
				var curMethod = genericGetRecord.MakeGenericMethod(singleEntity.PropertyType, fkIDProp.PropertyType);
				var loadedEntity = curMethod.Invoke(this, new[] { fkIDProp.GetValue(item) });
				singleEntity.SetValue(item, loadedEntity);
			}

			foreach (var multiEntity in connectedMultiEntities)
			{
				// Get the generic type of the current enumerable
				var listType = multiEntity.PropertyType.GenericTypeArguments[0];
				var listTypeProps = GetColumnProperties(listType);

				// Get the FK property of that type
				var fkIDProp = listTypeProps.FirstOrDefault(p => p.GetCustomAttribute<ForeignKeyAttribute>() != null);
				if (fkIDProp == null) continue;

				// Get the PK property of that type
				var pkIDProp = listTypeProps.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

				// Get target PK all records where the FK property is equal to this ID
				var stringIDs = GetSimpleData<string>($"SELECT [{pkIDProp.Name}] FROM {listType.Name}s WHERE [{fkIDProp.Name}] = @pkVal", new() { { "@pkVal", pkVal } });

				// Recursively load for each ID of that type
				var curMethod = genericGetRecord.MakeGenericMethod(listType, fkIDProp.PropertyType);
				var loadedEntities = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(listType))!;
				foreach (string sid in stringIDs)
				{
					var converter = TypeDescriptor.GetConverter(fkIDProp.PropertyType);
					loadedEntities.Add(curMethod.Invoke(this, [converter.ConvertFrom(sid)]));
				}
				multiEntity.SetValue(item, loadedEntities);
			}
		}

		public void UpsertDataRecords<T>(params T[] records) where T : ITable, new()
		{
			if (records.Length == 0) return;

			// Make sure unset autoincrement PKs are not included (only check the value of the first record and assume the rest are the same)
			var propColumns = GetColumnProperties(typeof(T));
			var pkCol = propColumns.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);
			var nonDefaultAutoIncCols = propColumns.Where(p => !(p.GetCustomAttribute<PrimaryKeyAttribute>()?.AutoIncrement ?? false)
				|| !p.GetValue(records[0])!.Equals(p.PropertyType.IsValueType ? Activator.CreateInstance(p.PropertyType) : null)).ToList();
			var nonPkCols = propColumns.Where(p => p != pkCol).ToList();

			// Build insert
			var upsertBuilder = new StringBuilder();
			upsertBuilder.AppendLine($"INSERT INTO {typeof(T).Name}s ({string.Join(", ", nonDefaultAutoIncCols.Select(c => $"[{c.Name}]"))}) VALUES");
			var recordVals = records.Select((r, i) => $"({string.Join(", ", nonDefaultAutoIncCols.Select(p => $"@{p.Name}_{i}"))})");
			upsertBuilder.AppendLine(string.Join($",{Environment.NewLine}", recordVals));

			// Upsert clause
			upsertBuilder.AppendLine($"ON CONFLICT([{pkCol.Name}]) DO UPDATE SET");
			upsertBuilder.AppendLine(string.Join($",{Environment.NewLine}", nonPkCols.Select(c => $"[{c.Name}] = excluded.[{c.Name}]")));

			// Returning clause
			upsertBuilder.AppendLine($"RETURNING [{pkCol.Name}];");

			using (var conn = new SqliteConnection(ConnectionString))
			{
				var cmd = conn.CreateCommand();
				cmd.CommandText = upsertBuilder.ToString();
				foreach (var col in nonDefaultAutoIncCols)
				{
					for (int i = 0; i < records.Length; i++)
					{
						cmd.Parameters.AddWithValue($"@{col.Name}_{i}", col.GetValue(records[i]) ?? DBNull.Value);
					}
				}
				conn.Open();

				// Make sure we have a cached table of this type in prep for the next part
				if (!_tableCache.ContainsKey(typeof(T))) _tableCache[typeof(T)] = new Dictionary<object, ITable>();

				// Read the returned PKs and make sure our objects reflect that, in case of autoincremented columns
				using (var reader = cmd.ExecuteReader())
				{
					foreach (var record in records)
					{
						reader.Read();
						var pkVal = TypeDescriptor.GetConverter(pkCol.PropertyType).ConvertFrom(reader.GetString(0))!;
						pkCol.SetValue(record, pkVal);

						// Update the cache
						_tableCache[typeof(T)][pkVal] = record;
					}
				}
			}

			// Set any parent FK single properties in this class
			var iTableProps = typeof(T).GetProperties().Where(p => p.PropertyType.IsAssignableTo(typeof(ITable))).ToList();
			var fkColumns = nonDefaultAutoIncCols.Where(p => p.GetCustomAttribute<ForeignKeyAttribute>() != null).ToList();
			foreach (var fkColumn in fkColumns)
			{
				// Get the matching FK type
				var fkAttr = fkColumn.GetCustomAttribute<ForeignKeyAttribute>()!;

				// Find the matching single property type
				var matchingFKProp = iTableProps.FirstOrDefault(p => p.PropertyType == fkAttr.ReferenceTable);
				if (matchingFKProp == null) continue;

				foreach (var record in records)
				{
					var fkey = fkColumn.GetValue(record);
					if (fkey != null)
					{
						ITable? parentVal = null;
						if (_tableCache.ContainsKey(matchingFKProp.PropertyType) && _tableCache[matchingFKProp.PropertyType].ContainsKey(fkey))
						{
							// Load the value from cache if it exists
							parentVal = _tableCache[matchingFKProp.PropertyType][fkey];
						}
						else
						{
							// Otherwise load it from the DB. This will ensure everything is freshly cached, including autoincremented properties
							var genDataMethod = GetType().GetMethod(nameof(GetDataRecord))!.MakeGenericMethod(matchingFKProp.PropertyType, fkey.GetType());
							parentVal = (ITable?)genDataMethod.Invoke(this, [fkey]);
						}
						matchingFKProp.SetValue(record, parentVal);
					}
				}
			}

			// Insert this item to any List<T> in linked parent entities that do not have it already
			UpdateParentListsFromChildEntities(true, records);
		}

		public void DeleteDataRecords<T>(params T[] records) where T : ITable
		{
			if (records.Length == 0) return;

			// Get the PK column of this type and remove all matching records
			var pkColumn = GetColumnProperties(typeof(T)).First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

			using (var conn = new SqliteConnection(ConnectionString))
			{
				var cmd = conn.CreateCommand();
				cmd.CommandText = $"DELETE FROM {typeof(T).Name}s WHERE {pkColumn.Name} IN ({string.Join(", ", records.Select((r, i) => $"@{pkColumn.Name}_{i}"))})";
				for (int i = 0; i < records.Length; i++)
				{
					cmd.Parameters.AddWithValue($"{pkColumn.Name}_{i}", pkColumn.GetValue(records[i]));
				}
				conn.Open();
				cmd.ExecuteNonQuery();

				// Manually update sequence info
				cmd.CommandText = $"UPDATE sqlite_sequence SET seq = (SELECT MAX({pkColumn.Name}) FROM {typeof(T).Name}s) WHERE name = '{typeof(T).Name}s'";
				cmd.ExecuteNonQuery();
			}

			// Remove this item from any List<T> in linked parent entities
			UpdateParentListsFromChildEntities(false, records);
		}

		private void UpdateParentListsFromChildEntities<T>(bool isUpsert, params T[] childEntities) where T : ITable
		{
			// Remove from or update cache
			var propColumns = GetColumnProperties(typeof(T));
			if (!isUpsert)
			{
				var pkCol = propColumns.First(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);

				// If this was not an upsert, remove each entry from the cache if it existsw
				foreach (var child in childEntities)
				{
					var pkVal = pkCol.GetValue(child)!;
					if (_tableCache.ContainsKey(typeof(T)) && _tableCache[typeof(T)].ContainsKey(pkVal))
					{
						_tableCache[typeof(T)].Remove(pkVal);
					}
				}
			}

			// If the current type has a parent reference, find that parent's list of the current (child) type. If it exists, populate it with our records
			var parentRecordProp = typeof(T).GetProperties().FirstOrDefault(p => p.PropertyType.IsAssignableTo(typeof(ITable)));
			if (parentRecordProp != null)
			{
				var theListOfUs = parentRecordProp.PropertyType.GetProperties().FirstOrDefault(p => p.PropertyType.IsAssignableTo(typeof(IEnumerable<T>)));
				if (theListOfUs != null)
				{
					// Get the FK column of this type for the current parent
					var fkIdProp = propColumns.FirstOrDefault(p => p.GetCustomAttribute<ForeignKeyAttribute>()?.ReferenceTable == parentRecordProp.PropertyType);
					if (fkIdProp == null) return;

					// Load parent object from cache. All children should have the same parent, so just grab the first
					var parentVal = _tableCache[parentRecordProp.PropertyType][fkIdProp.GetValue(childEntities.First())!];
					var listVal = (List<T>)theListOfUs.GetValue(parentVal)!;

					foreach (var child in childEntities)
					{
						if (isUpsert)
						{
							// Upsert
							var matchingRecord = listVal.FirstOrDefault(r => r.Equals(child));
							if (matchingRecord == null)
							{
								listVal.Add(child);
							}
						}
						else
						{
							// Delete
							listVal.Remove(child);
						}
					}
				}
			}
		}

		private class FilterTranslator<T> : ExpressionVisitor
		{
			private StringBuilder _queryBuilder;

			public string GetWhereClause(Expression expression)
			{
				_queryBuilder = new StringBuilder();

				_queryBuilder.Append(" WHERE ");
				Visit(expression);

				return _queryBuilder.ToString();
			}

			protected override Expression VisitBinary(BinaryExpression node)
			{
				_queryBuilder.Append("(");
				Visit(node.Left);
				_queryBuilder.Append($" {GetSqlOperator(node.NodeType)} ");
				Visit(node.Right);
				_queryBuilder.AppendLine(")");

				return node;
			}

			protected override Expression VisitUnary(UnaryExpression node)
			{
				_queryBuilder.Append($" {GetSqlOperator(node.NodeType)} ");
				Visit(node.Operand);
				return node;
			}

			protected override Expression VisitMember(MemberExpression node)
			{
				// If the reflected type is our current class's generic type, we want to use the word for the column name
				if (typeof(T) == node.Member.ReflectedType)
				{
					_queryBuilder.Append($"[{node.Member.Name}]");
				}
				else
				{
					// Otherwise, get the actual requested value of this member
					if (node.Expression is ParameterExpression pe && node.Type == typeof(bool)) _queryBuilder.Append(" = 1");
					else _queryBuilder.Append(Expression.Lambda(node).Compile().DynamicInvoke());
				}
				return node;
			}

			protected override Expression VisitConstant(ConstantExpression node)
			{
				_queryBuilder.Append(AppendValue(node.Value));
				return node;
			}

			private string AppendValue(object? val)
			{
				if (val == null) return "NULL";
				else if (val is bool b) return b ? "1" : "0";
				else return $"'{val}'";
			}

			private string GetSqlOperator(ExpressionType nodeType)
			{
				return nodeType switch
				{
					ExpressionType.Not => "NOT",
					ExpressionType.And or ExpressionType.AndAlso => "AND",
					ExpressionType.Or or ExpressionType.OrElse => "OR",
					ExpressionType.Equal => "=",
					ExpressionType.GreaterThanOrEqual => ">=",
					ExpressionType.LessThanOrEqual => "<=",
					ExpressionType.NotEqual => "!=",
					ExpressionType.GreaterThan => ">",
					ExpressionType.LessThan => "<",

					_ => throw new NotImplementedException($"FilterTranslator does not support '{nodeType}' node types.")
				};
			}
		}
	}
}