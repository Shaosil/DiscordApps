namespace ShaosilBot.Core.Models.SQLite
{
	public interface ITable { } // Tables MUST inherit from this

	public class NotMappedAttribute : Attribute { } // Tells the ORM to not include this property in the DB columns

	public class RequiredAttribute : Attribute { } // Tells the ORM to ensure this field is NOT NULL

	public class PrimaryKeyAttribute : Attribute // Tells the ORM this is the primary key of the current table, and whether it is auto incremented or not. Only one PK currently supported
	{
		public bool AutoIncrement { get; }

		public PrimaryKeyAttribute(bool autoIncrement) { AutoIncrement = autoIncrement; }
	}

	public class ForeignKeyAttribute<T> : Attribute where T : ITable // Tells the ORM this is a FK to the specified table type and column
	{
		public Type ReferenceTable { get; }
		public string ReferenceColumn { get; }

		public ForeignKeyAttribute(string referenceColumn) { ReferenceTable = typeof(T); ReferenceColumn = referenceColumn; }
	}
}