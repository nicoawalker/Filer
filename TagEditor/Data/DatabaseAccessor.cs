using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TagEditor
{

	using Debug = System.Diagnostics.Debug;

	public abstract class DBDataContainerCreator<T>
	{
		public abstract T Create();
	}

	public abstract class DBDataContainer<T>
	{
		public abstract int PropertyCount();
		public abstract string this[string propertyName] { set; }
		public abstract string this[int index] { get; }
	}

	public enum DBOperator { BETWEEN, IN, LIKE, OR, EQUALS, NOTEQUALS, LESS, GREATER, LESSOREQUAL, GREATEROREQUAL }

	public class DBPredicate
	{
		public string Value { get; set; }
		public string Column { get; set; }
		public DBOperator Operator { get; set; }

		public DBPredicate( string column, DBOperator op, string value )
		{
			Column = column;
			Value = value;
			Operator = op;
		}
	}

	public class DatabaseAccessor
	{
		private SQLiteConnection m_sqlConnection;

		private readonly object m_databaseLock;

		private string m_databasePath;

		public DatabaseAccessor( string databasePath, string connectionStringName )
		{
			m_databasePath = databasePath;
			m_sqlConnection = null;
			m_databaseLock = new object();

			try
			{
				_CreateAndConnectDatabase(m_databasePath, connectionStringName);

			}
			catch ( Exception crap )
			{
				Debug.WriteLine($"Failed to create database accessor for db '{databasePath}': {crap.Message}");
			}
		}

		private static string _LoadConnectionString( string con )
		{
			return System.Configuration.ConfigurationManager.ConnectionStrings[con].ConnectionString;
		}

		/// <summary>
		/// attempts to create a new, read-to-use database file
		/// </summary>
		/// <returns>true if a database already existed or was successfully created, and false otherwise</returns>
		public bool _CreateAndConnectDatabase( string databasePath, string connectionStringName )
		{
			//if ( File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + @"\tags.db") == true ) return true;

			//create a new database if one it doesn't exist already
			if ( File.Exists(databasePath) == false )
			{
				FileStream databaseFile = System.IO.File.Create(databasePath);
				if ( databaseFile != null ) databaseFile.Close();
			}

			//initialize the connection and insert the default tables if required
			m_sqlConnection = new SQLiteConnection(_LoadConnectionString(connectionStringName));

			//string createTagTableQuery = @"CREATE TABLE IF NOT EXISTS [Tags] ([id]  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT UNIQUE, [label]  TEXT NOT NULL UNIQUE, [color]  TEXT NOT NULL UNIQUE, [children]  TEXT NOT NULL DEFAULT '');";

			//string createFileTagsTableQuery = @"CREATE TABLE IF NOT EXISTS [FileTags] ([path]  TEXT NOT NULL PRIMARY KEY UNIQUE, [tags]  TEXT NOT NULL UNIQUE);";

			//try
			//{
			//	m_sqlConnection.Open();

			//	using ( SQLiteCommand com = m_sqlConnection.CreateCommand() )
			//	{
			//		com.CommandText = createTagTableQuery;
			//		com.ExecuteNonQuery();

			//		com.CommandText = createFileTagsTableQuery;
			//		com.ExecuteNonQuery();
			//	}

			//	m_sqlConnection.Close();

			//}
			//catch ( Exception crap )
			//{
			//	m_sqlConnection.Close();

			//	throw crap;
			//}

			return true;
		}

		private void _VerifyConnection()
		{
			if ( m_sqlConnection == null ) throw new InvalidOperationException("Connection object is null");
		}

		public List<T> _ExecuteSelectCommand<T>( SQLiteCommand command, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			_VerifyConnection();

			try
			{
				SQLiteDataReader result = command.ExecuteReader();
				if ( result.HasRows == false ) return null;

				List<T> resultContainers = new List<T>();

				while ( result.Read() )
				{
					T container = dataContainerCreator.Create();

					for ( int i = 0; i < result.FieldCount; i++ )
					{
						container[result.GetName(i)] = result.GetValue(i).ToString();
					}
					resultContainers.Add(container);
				}

				return resultContainers;
			}
			catch ( Exception crap )
			{
				m_sqlConnection.Close();
				throw crap;
			}
		}

		private string _ParseOperator( DBOperator op )
		{
			switch(op)
			{
				case DBOperator.BETWEEN:
					return "BETWEEN";
				case DBOperator.EQUALS:
					return "=";
				case DBOperator.GREATER:
					return ">";
				case DBOperator.GREATEROREQUAL:
					return ">=";
				case DBOperator.IN:
					return "IN";
				case DBOperator.LESS:
					return "<";
				case DBOperator.LESSOREQUAL:
					return "<=";
				case DBOperator.LIKE:
					return "LIKE";
				case DBOperator.NOTEQUALS:
					return "!=";
				case DBOperator.OR:
					return "OR";
				default: return "";
			}
		}

		private SQLiteCommand _BuildSelectCommand( string tableName, List<DBPredicate> predicates, string combiner )
		{
			try
			{
				m_sqlConnection.Open();

				SQLiteCommand command = m_sqlConnection.CreateCommand();

				SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

				command.CommandText = $"SELECT * FROM {builder.QuoteIdentifier(tableName)} WHERE";

				for ( int i = 0; i < predicates.Count; i++ )
				{
					command.CommandText += $" {builder.QuoteIdentifier(predicates[i].Column)} {_ParseOperator(predicates[i].Operator)} @value{i}";
					command.Parameters.AddWithValue($"@value{i}", predicates[i].Value);
					if ( i < predicates.Count - 1 ) command.CommandText += $" {combiner}";
				}

				command.CommandText += ";";

				m_sqlConnection.Close();

				return command;
			}
			catch ( Exception crap )
			{
				m_sqlConnection.Close();
				throw crap;
			}
		}

		public int ExecuteNonQuery( string query )
		{
			_VerifyConnection();

			try
			{
				m_sqlConnection.Open();

				int result = 0;

				using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
				{
					command.CommandText = query;
					result = command.ExecuteNonQuery();
				}

				m_sqlConnection.Close();

				return result;
			}
			catch ( Exception crap )
			{
				m_sqlConnection.Close();
				Debug.WriteLine(crap.Message);
				throw crap;
			}
		}

		/// <summary>
		/// Inserts a new row into a table
		/// </summary>
		/// <param name="tableName">table to insert into</param>
		/// <param name="values">a list of rows to insert. each row must have parameters matching the number and order of columns in the table</param>
		/// <returns>the number of rows that were inserted</returns>
		public int Insert<T>( string tableName, T value ) where T : DBDataContainer<T>
		{
			SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

			string query = $"INSERT INTO {builder.QuoteIdentifier(tableName)} VALUES(";

			for(int i = 0; i < value.PropertyCount(); i++)
			{
				query += $"{value[i]}";
				if ( i < value.PropertyCount() - 1 ) query += ", ";
			}

			query += ");";

			return ExecuteNonQuery(query);
		}

		/// <summary>
		/// Inserts multiple rows into a table
		/// </summary>
		/// <param name="tableName">table to insert into</param>
		/// <param name="values">a list of rows to insert. each row must have parameters matching the number and order of columns in the table</param>
		/// <returns>the number of rows that were inserted</returns>
		public int Insert<T>( string tableName, List<T> values ) where T : DBDataContainer<T>
		{
			SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

			string query = $"INSERT INTO {builder.QuoteIdentifier(tableName)} VALUES";

			for ( int a = 0; a < values.Count; a++ )
			{
				query += "(";
				for ( int i = 0; i < values[a].PropertyCount(); i++ )
				{
					query += $"{values[a][i]}";
					if ( i < values[a].PropertyCount() - 1 ) query += ", ";
				}
				query += ")";

				if ( a < values.Count - 1)
				{
					query += ", ";
				}
			}

			query += ";";

			return ExecuteNonQuery(query);
		}

		/// <summary>
		/// Selects all rows and columns from the database
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="tableName">name of the table to select from</param>
		/// <returns>a list of data containers of the specified type containing the results, or null if there were no results</returns>
		public List<T> SelectAll<T>( string tableName, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			_VerifyConnection();

			try
			{
				m_sqlConnection.Open();

				List<T> results = null;

				using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
				{
					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					command.CommandText = $"SELECT * FROM {builder.QuoteIdentifier(tableName)};";

					results = _ExecuteSelectCommand<T>(command, dataContainerCreator);
				}

				m_sqlConnection.Close();

				return results;
			}
			catch ( Exception crap )
			{
				m_sqlConnection.Close();
				throw crap;
			}
		}

		/// <summary>
		/// Selects all rows from a table where all of a list of predicates are true
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="tableName">name of the table to select from</param>
		/// <param name="predicates">list of predicates, all of which must evaluate to true</param>
		/// <returns>a list of data containers, each containing one row of the results, or null if there were no results</returns>
		public List<T> SelectAll<T>( string tableName, List<DBPredicate> predicates, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			_VerifyConnection();

			using ( SQLiteCommand command = _BuildSelectCommand(tableName, predicates, "AND") )
			{
				return _ExecuteSelectCommand<T>(command, dataContainerCreator);
			}
		}

		/// <summary>
		/// Selects all rows from a table where at least one of a list of predicates are true
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="tableName">name of the table to select from</param>
		/// <param name="predicates">list of predicates, one or more of which must evaluate to true</param>
		/// <returns>a list of data containers, each containing one row of the results, or null if there were no results</returns>
		public List<T> SelectAny<T>( string tableName, List<DBPredicate> predicates, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			_VerifyConnection();

			using ( SQLiteCommand command = _BuildSelectCommand(tableName, predicates, "OR") )
			{
				return _ExecuteSelectCommand<T>(command, dataContainerCreator);
			}
		}

		/// <summary>
		/// returns the number of files current indexed within the database
		/// </summary>
		/// <returns></returns>
		public int QueryDatabaseSize()
		{
			_VerifyConnection();

			try
			{
				m_sqlConnection.Open();

				int dbSize = 0;

				using ( SQLiteCommand command = new SQLiteCommand($"SELECT COUNT(path) FROM FileCache", m_sqlConnection) )
				{
					dbSize = Convert.ToInt32(command.ExecuteScalar());
				}

				m_sqlConnection.Close();

				return dbSize;
			}
			catch ( Exception crap )
			{
				m_sqlConnection.Close();
				throw crap;
			}
}

		//public List<DBDataContainer<T>> SelectEqual<T>( string tableName, List<string> values, Dictionary<string, string> whereEqual )
		//{
		//	if ( m_sqlConnection == null ) return null;

		//	using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
		//	{
		//		command.CommandText = $"SELECT * FROM TABLE {tableName} VALUES";
		//		command.ExecuteNonQuery();
		//		SQLiteDataReader result = command.ExecuteReader();
		//		if ( result.HasRows == false ) return null;

		//		Dictionary<string, string> resultDictionary = new Dictionary<string, string>();

		//		while ( result.Read() )
		//		{
		//			for ( int i = 0; i < result.FieldCount; i++ )
		//			{
		//				resultContainer[i]
		//			}
		//			resultContainer[result] =
		//			resultDictionary.Add(result.GetName(), result.GetValue);
		//			matches.Add(new FileInfo(result["tags"].ToString(),
		//										result["path"].ToString(),
		//										result["name"].ToString(),
		//										Int32.Parse(result["access_count"].ToString()),
		//										(FileType)Int32.Parse(result["type"].ToString()),
		//										Int32.Parse(result["favorite"].ToString()).Equals(1),
		//										Int32.Parse(result["hidden"].ToString()).Equals(1)));
		//		}
		//	}

		//	string query = 
		//}

		//public void SelectLike( string tableName, List<string> values, Dictionary<string, string> whereLike )
		//{

		//}

		//public void SelectNotEqual( string tableName, List<string> values, Dictionary<string, string> whereNotEqual )
		//{

		//}

		//private static List<FileInfo> _ExecuteMatchQuery( string query )
		//{
		//	List<FileInfo> matches = new List<FileInfo>();

		//	using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//	{
		//		con.Open();
		//		SQLiteCommand com = new SQLiteCommand(query, con);

		//		SQLiteDataReader result = com.ExecuteReader();
		//		if ( result.HasRows )
		//		{
		//			while ( result.Read() )
		//			{
		//				matches.Add(new FileInfo(result["tags"].ToString(),
		//										 result["path"].ToString(),
		//										 result["name"].ToString(),
		//										 Int32.Parse(result["access_count"].ToString()),
		//										 (FileType)Int32.Parse(result["type"].ToString()),
		//										 Int32.Parse(result["favorite"].ToString()).Equals(1),
		//										 Int32.Parse(result["hidden"].ToString()).Equals(1)));
		//			}
		//		}
		//	}

		//	return matches;
		//}

		//private static List<FileInfo> _ExecuteQueryCommand( SQLiteCommand command )
		//{
		//	List<FileInfo> matches = new List<FileInfo>();

		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			command.Connection = con;

		//			SQLiteDataReader result = command.ExecuteReader();
		//			if ( result.HasRows )
		//			{
		//				while ( result.Read() )
		//				{
		//					matches.Add(new FileInfo(result["tags"].ToString(),
		//											 result["path"].ToString(),
		//											 result["name"].ToString(),
		//											 Int32.Parse(result["access_count"].ToString()),
		//											 (FileType)Int32.Parse(result["type"].ToString()),
		//											 Int32.Parse(result["favorite"].ToString()).Equals(1),
		//											 Int32.Parse(result["hidden"].ToString()).Equals(1)));
		//				}
		//			}
		//		}
		//	}
		//	catch (SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception executing query command: " + e.Message);
		//	}

		//	return matches;
		//}

		//private static List<FileInfo> _ExecuteQueryCommand( List<SQLiteCommand> commands )
		//{
		//	List<FileInfo> matches = new List<FileInfo>();

		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				foreach ( SQLiteCommand command in commands )
		//				{
		//					command.Connection = con;

		//					SQLiteDataReader result = command.ExecuteReader();
		//					while ( result.Read() )
		//					{
		//						matches.Add(new FileInfo(result["tags"].ToString(),
		//												 result["path"].ToString(),
		//												 result["name"].ToString(),
		//												 Int32.Parse(result["access_count"].ToString()),
		//												 (FileType)Int32.Parse(result["type"].ToString()),
		//												 Int32.Parse(result["favorite"].ToString()).Equals(1),
		//												 Int32.Parse(result["hidden"].ToString()).Equals(1)));
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}
		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception executing transacted query command: " + e.Message);
		//	}

		//	return matches;
		//}

		//private static void _ExecuteNonQueryCommand( SQLiteCommand command )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			command.Connection = con;

		//			command.ExecuteNonQuery();
		//		}
		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception executing non-query command: " + e.Message);
		//	}
		//}

		//public static void Insert( FileInfo file )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteCommand com = con.CreateCommand() )
		//			{
		//				//com.CommandText = $"INSERT OR IGNORE INTO FileCache VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)});";
		//				//com.CommandText = $"INSERT INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)}) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.Path}');";
		//				com.CommandText = "INSERT OR REPLACE INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES (@path, @name, @type, @tags, @access_count, @favorite, @hidden);";
		//				com.Parameters.AddWithValue("@path", file.Path);
		//				com.Parameters.AddWithValue("@name", file.Name);
		//				com.Parameters.AddWithValue("@type", file.Type);
		//				com.Parameters.AddWithValue("@tags", String.Join(";", file.Tags));
		//				com.Parameters.AddWithValue("@access_count", file.AccessCount);
		//				com.Parameters.AddWithValue("@favorite", file.IsFavorite);
		//				com.Parameters.AddWithValue("@hidden", file.IsHidden);
		//				com.ExecuteNonQuery();
		//			}
		//		}
		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception on insert: " + e.Message);
		//	}
		//}

		//public static void Insert( List<FileInfo> files )
		//{
		//	if ( files.Count == 1 ) Insert(files[1]);

		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				using ( SQLiteCommand com = con.CreateCommand() )
		//				{
		//					foreach ( var file in files )
		//					{
		//						//com.CommandText = $"INSERT OR IGNORE INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash+1).ToLower()}',1);";
		//						//com.CommandText = $"INSERT OR IGNORE INTO FileCache VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)});";
		//						//com.CommandText = $"INSERT INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)}) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.Path}');";
		//						//com.ExecuteNonQuery();

		//						//com.CommandText = $"INSERT INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash + 1).ToLower()}',1) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.ToLower()}');";
		//						com.CommandText = "INSERT OR REPLACE INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES (@path, @name, @type, @tags, @access_count, @favorite, @hidden);";
		//						com.Parameters.AddWithValue("@path", file.Path);
		//						com.Parameters.AddWithValue("@name", file.Name);
		//						com.Parameters.AddWithValue("@type", file.Type);
		//						com.Parameters.AddWithValue("@tags", String.Join(";", file.Tags));
		//						com.Parameters.AddWithValue("@access_count", file.AccessCount);
		//						com.Parameters.AddWithValue("@favorite", file.IsFavorite);
		//						com.Parameters.AddWithValue("@hidden", file.IsHidden);

		//						com.ExecuteNonQuery();
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}

		//	}catch(SQLiteException e)
		//	{
		//		Console.WriteLine("SQLite Exception on multi insert: " + e.Message);
		//	}
		//}

		//public static void InsertDirectories( List<string> directoryPaths )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				using ( SQLiteCommand com = con.CreateCommand() )
		//				{
		//					foreach ( var dir in directoryPaths )
		//					{
		//						if( dir.Equals(@"c:\users\indeed\desktop\threadpool") )
		//						{
		//							Console.WriteLine("inserting threadpool folder..");
		//						}
		//						int lastSlash = dir.LastIndexOf(@"\");
		//						if ( lastSlash == -1 ) continue;

		//						string name = (lastSlash == dir.Length - 1) ? "" : dir.Substring(lastSlash + 1).ToLower();

		//						com.CommandText = "INSERT OR IGNORE INTO FileCache(path, name, type) VALUES (@path, @name, 0);";
		//						com.Parameters.AddWithValue("@path", dir.ToLower());
		//						com.Parameters.AddWithValue("@name", name);
		//						com.ExecuteNonQuery();

		//						if ( dir.Equals(@"c:\users\indeed\desktop\threadpool") )
		//						{
		//							Console.WriteLine($"inserted. name: {name}");
		//						}
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}

		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception on multi insert: " + e.Message);
		//	}
		//}

		//public static void InsertFiles( List<string> filePaths )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				using ( SQLiteCommand com = con.CreateCommand() )
		//				{
		//					foreach ( var file in filePaths )
		//					{
		//						int type = 1;
		//						int lastSlash = file.LastIndexOf(@"\");
		//						if ( lastSlash == -1 ) continue;

		//						string name = file.Substring(lastSlash + 1).ToLower();

		//						if (lastSlash == file.Length - 1)
		//						{
		//							type = 0;
		//							name = "";
		//						}

		//						//com.CommandText = $"INSERT INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash + 1).ToLower()}',1) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.ToLower()}');";
		//						com.CommandText = "INSERT OR IGNORE INTO FileCache(path, name, type) VALUES (@path, @name, @type);";
		//						com.Parameters.AddWithValue("@path", file.ToLower());
		//						com.Parameters.AddWithValue("@name", name);
		//						com.Parameters.AddWithValue("@type", type);
		//						//com.CommandText = $"INSERT OR IGNORE INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash+1).ToLower()}',1);";
		//						com.ExecuteNonQuery();
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}

		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception on multi insert: " + e.Message);
		//	}
		//}


		//public static void UpdateFiles( List<FileInfo> files )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				using ( SQLiteCommand com = con.CreateCommand() )
		//				{
		//					foreach ( var file in files )
		//					{
		//						string tags = "";
		//						foreach ( string tag in file.Tags )
		//						{
		//							if ( tag.Length > 0 ) tags += tag + ";";
		//						}
		//						com.CommandText = $"UPDATE FileCache SET path='{file.Path}', name='{file.Name}', tags='{tags}', access_count={file.AccessCount}, favorite={(file.IsFavorite ? 1 : 0)}, hidden={(file.IsHidden ? 1 : 0)} WHERE(path='{file.Path}');";
		//						com.ExecuteNonQuery();
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}

		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception on file update: " + e.Message);
		//	}

		//}

		//public static void Remove( List<string> paths )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				using ( SQLiteCommand com = con.CreateCommand() )
		//				{
		//					foreach ( var path in paths )
		//					{
		//						Console.WriteLine($"Removing paths '{path}'");
		//						com.CommandText = $"DELETE FROM FileCache WHERE path='{path.ToLower()}';";
		//						com.ExecuteNonQuery();
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}

		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception deleting file: " + e.Message);
		//	}
		//}

		//public static void Remove( List<FileInfo> files )
		//{
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteTransaction t = con.BeginTransaction() )
		//			{
		//				using ( SQLiteCommand com = con.CreateCommand() )
		//				{
		//					foreach ( var file in files )
		//					{
		//						Console.WriteLine($"Removing file '{file.Path}'");
		//						com.CommandText = $"DELETE FROM FileCache WHERE path='{file.Path.ToLower()}';";
		//						com.ExecuteNonQuery();
		//					}
		//				}

		//				t.Commit();
		//			}
		//		}

		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception deleting file: " + e.Message);
		//	}
		//}

		//public static void RemoveDirectory( string path )
		//{
		//	Console.WriteLine($"Removing directory '{path}'");
		//	try
		//	{
		//		using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//		{
		//			con.Open();

		//			using ( SQLiteCommand com = con.CreateCommand() )
		//			{
		//				com.CommandText = $"DELETE FROM FileCache WHERE path LIKE '{path.ToLower()}%';";
		//				com.ExecuteNonQuery();
		//			}
		//		}
		//	}
		//	catch ( SQLiteException e )
		//	{
		//		Console.WriteLine("SQLite Exception deleting directory: " + e.Message);
		//	}
		//}

		//public static List<FileInfo> Match( string searchTerm, MatchFilters filters = MatchFilters.Any )
		//{
		//	switch(filters)
		//	{
		//		case MatchFilters.Any:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' UNION SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%';");
		//			}
		//		case MatchFilters.Tags | MatchFilters.Files:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=1 UNION SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%' AND type=1;");
		//			}
		//		case MatchFilters.Tags | MatchFilters.Folders:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=0 UNION SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%' AND type=0;");
		//			}
		//		case MatchFilters.Files | MatchFilters.Folders:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%';");
		//			}
		//		case MatchFilters.Tags:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%';");
		//			}
		//		case MatchFilters.Files:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=1;");
		//			}
		//		case MatchFilters.Folders:
		//			{
		//				return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=0;");
		//			}
		//		default:
		//			{
		//				Console.WriteLine("Invalid search filter");
		//				return new List<FileInfo>();
		//			}
		//	}
		//}

		//public static FileInfo FindFile( string path )
		//{
		//	FileInfo foundFile = null;
		//	using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
		//	{
		//		con.Open();
		//		SQLiteCommand com = new SQLiteCommand($"SELECT * FROM FileCache WHERE path='{path.ToLower()}';", con);

		//		SQLiteDataReader result = com.ExecuteReader();
		//		if ( result.HasRows )
		//		{
		//			while ( result.Read() )
		//			{
		//				foundFile = new FileInfo(result["tags"].ToString(),
		//										 result["path"].ToString(),
		//										 result["name"].ToString(),
		//										 Int32.Parse(result["access_count"].ToString()),
		//										 (FileType)Int32.Parse(result["type"].ToString()),
		//										 Int32.Parse(result["favorite"].ToString()).Equals(1),
		//										 Int32.Parse(result["hidden"].ToString()).Equals(1));
		//			}
		//		}
		//	}

		//	return foundFile;
		//}

	}
}
