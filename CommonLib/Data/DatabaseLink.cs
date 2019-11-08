using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Common
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

	public enum DBOperator { BETWEEN, IN, LIKE, OR, EQUALS, NOTEQUALS, LESS, GREATER, LESSOREQUAL, GREATEROREQUAL, NONE } //operators for comparing values
	public enum DBConjunction { AND, OR } //operators for joining predicates
	public enum DBCollisionAction { IGNORE, REPLACE }

	public class DBParameter
	{
		public string Value { get; set; }
		public string Column { get; set; }

		public DBParameter( string column, string value )
		{
			Column = column;
			Value = value;
		}
	}

	public class DBPredicate : DBParameter
	{
		public DBOperator Operator { get; set; }

		public DBPredicate( string column, DBOperator op, string value ) : base(column, value)
		{
			Operator = op;
		}
	}

	public class DatabaseLink
	{
		private SQLiteConnection m_sqlConnection;

		private readonly object m_databaseLock;

		private string m_databasePath;

		public DatabaseLink( string databasePath, string connectionStringName )
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

		private void _AppendPredicates( SQLiteCommand command, List<DBPredicate> predicates, string conjunction )
		{
			if ( command == null ) return;

			SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

			int valueIndex = command.Parameters.Count;
			for ( int i = 0; i < predicates.Count; i++ )
			{
				command.CommandText += $"{builder.QuoteIdentifier(predicates[i].Column)} {_OperatorToString(predicates[i].Operator)} @value{valueIndex}";
				command.Parameters.AddWithValue($"@value{valueIndex}", predicates[i].Value);

				if ( i < predicates.Count - 1 ) command.CommandText += $" {conjunction} ";
				valueIndex++;
			}
		}

		/// <summary>
		/// attempts to create a new, read-to-use database file
		/// </summary>
		/// <returns>true if a database already existed or was successfully created, and false otherwise</returns>
		public bool _CreateAndConnectDatabase( string databasePath, string connectionStringName )
		{
			//create a new database if one it doesn't exist already
			if ( File.Exists(databasePath) == false )
			{
				FileStream databaseFile = System.IO.File.Create(databasePath);
				if ( databaseFile != null ) databaseFile.Close();
			}

			//initialize the connection and insert the default tables if required
			m_sqlConnection = new SQLiteConnection(_LoadConnectionString(connectionStringName));

			return true;
		}

		public List<T> _ExecuteSelectCommand<T>( SQLiteCommand command, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			_VerifyConnection();

			List<T> resultContainers = new List<T>();

			SQLiteDataReader result = command.ExecuteReader();
			if ( result.HasRows == false ) return resultContainers;

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

		private static string _LoadConnectionString( string con )
		{
			return System.Configuration.ConfigurationManager.ConnectionStrings[con].ConnectionString;
		}

		private string _OperatorToString( DBOperator op )
		{
			switch ( op )
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

		private void _VerifyConnection()
		{
			if ( m_sqlConnection == null ) throw new InvalidOperationException("Connection object is null");
		}

		/// <summary>
		/// deletes a row from a table
		/// </summary>
		/// <param name="tableName">the name of the table to remove from</param>
		/// <param name="predicates">predicates for the removal of a single row</param>
		/// <returns>the number of rows that were deleted</returns>
		public int Delete( string tableName, DBPredicate predicate )
		{
			lock ( m_databaseLock )
			{
				_VerifyConnection();

				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();
					int result = 0;

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						command.CommandText = $"DELETE FROM { builder.QuoteIdentifier(tableName)} WHERE({builder.QuoteIdentifier(predicate.Column)} {_OperatorToString(predicate.Operator)} @value0);";
						command.Parameters.AddWithValue("@value0", predicate.Value);

						result = command.ExecuteNonQuery();
					}

					m_sqlConnection.Close();

					return result;
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// deletes a row from a table
		/// </summary>
		/// <param name="tableName">the name of the table to remove from</param>
		/// <param name="predicates">predicates for the removal of a single row</param>
		/// <returns>the number of rows that were deleted</returns>
		public int Delete( string tableName, List<DBPredicate> predicates )
		{
			lock ( m_databaseLock )
			{
				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();
					int result = 0;

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						command.CommandText = $"DELETE FROM { builder.QuoteIdentifier(tableName)} WHERE(";
						for ( int i = 0; i < predicates.Count; i++ )
						{
							command.CommandText += $"{predicates[i].Column} {_OperatorToString(predicates[i].Operator)} @value{i}";
							command.Parameters.AddWithValue($"@value{i}", predicates[i].Value);

							if ( i < predicates.Count - 1 ) command.CommandText += " AND ";
						}

						command.CommandText += ");";

						result = command.ExecuteNonQuery();
					}

					m_sqlConnection.Close();

					return result;
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// efficiently deletes multiple rows from a table
		/// </summary>
		/// <param name="tableName">the name of the table to remove from</param>
		/// <param name="predicates">each list contains the predicates for the removal of a single row</param>
		public void DeleteTransacted( string tableName, List<List<DBPredicate>> predicates )
		{
			lock ( m_databaseLock )
			{
				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					using ( SQLiteTransaction t = m_sqlConnection.BeginTransaction() )
					{
						using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
						{
							for ( int a = 0; a < predicates.Count; a++ )
							{
								command.CommandText = $"DELETE FROM { builder.QuoteIdentifier(tableName)} WHERE(";

								for ( int i = 0; i < predicates[a].Count; i++ )
								{
									command.CommandText += $"{predicates[a][i].Column} {_OperatorToString(predicates[a][i].Operator)} @value{i}";
									command.Parameters.AddWithValue($"@value{i}", predicates[a][i].Value);

									if ( i < predicates[a].Count - 1 ) command.CommandText += " AND ";
								}

								command.CommandText += ");";

								command.ExecuteNonQuery();
							}
						}

						t.Commit();
					}

					m_sqlConnection.Close();
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		public int ExecuteNonQuery( string query )
		{
			lock ( m_databaseLock )
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
					throw crap;
				}
			}
		}

		/// <summary>
		/// Inserts a new row into a table
		/// </summary>
		/// <param name="collisionAction">what to do if a row already exists</param>
		/// <param name="tableName">table to insert into</param>
		/// <param name="values">the data for the row to insert. each row must have parameters matching the number and order of columns in the table</param>
		/// <returns>the number of rows that were inserted</returns>
		public int Insert<T>( DBCollisionAction collisionAction, string tableName, T value ) where T : DBDataContainer<T>
		{
			lock ( m_databaseLock )
			{
				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();
					int result = 0;

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						if ( collisionAction == DBCollisionAction.IGNORE )
						{
							command.CommandText = $"INSERT OR IGNORE";
						}
						else
						{
							command.CommandText = $"INSERT OR REPLACE";
						}

						command.CommandText += $" INTO {builder.QuoteIdentifier(tableName)} VALUES("; //NULL will make sqlite generate a primary key automatically

						for ( int i = 0; i < value.PropertyCount(); i++ )
						{
							if ( value[i] == "NULL" )
							{
								command.CommandText += "NULL";
							}
							else
							{
								command.CommandText += $"@value{i}";
								command.Parameters.AddWithValue($"@value{i}", value[i]);
							}

							if ( i < value.PropertyCount() - 1 ) command.CommandText += ", ";
						}

						command.CommandText += ");";

						result = command.ExecuteNonQuery();
					}

					m_sqlConnection.Close();

					return result;
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// Efficiently inserts multiple new rows into a table
		/// </summary>
		/// <param name="collisionAction">what to do if a row already exists</param>
		/// <param name="tableName">table to insert into</param>
		/// <param name="values">a list of rows to insert. each row must have parameters matching the number and order of columns in the table</param>
		public void InsertTransacted<T>( DBCollisionAction collisionAction, string tableName, List<T> values ) where T : DBDataContainer<T>
		{
			lock ( m_databaseLock )
			{
				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					using ( SQLiteTransaction t = m_sqlConnection.BeginTransaction() )
					{
						using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
						{
							for ( int a = 0; a < values.Count; a++ )
							{
								if ( collisionAction == DBCollisionAction.IGNORE )
								{
									command.CommandText = $"INSERT OR IGNORE";
								}
								else
								{
									command.CommandText = $"INSERT OR REPLACE";
								}

								command.CommandText += $" INTO {builder.QuoteIdentifier(tableName)} VALUES("; //NULL will make sqlite generate a primary key automatically

								for ( int i = 0; i < values[a].PropertyCount(); i++ )
								{
									if ( values[a][i] == "NULL" )
									{
										command.CommandText += "NULL";
									}
									else
									{
										command.CommandText += $"@value{i}";
										command.Parameters.AddWithValue($"@value{i}", values[a][i]);
									}

									if ( i < values[a].PropertyCount() - 1 ) command.CommandText += ", ";
								}

								command.CommandText += ");";

								command.ExecuteNonQuery();
							}
						}

						t.Commit();
					}

					m_sqlConnection.Close();
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// inserts a row into a table
		/// </summary>
		/// <param name="collisionAction">what to do if a row already exists</param>
		/// <param name="tableName">table to insert into</param>
		/// <param name="values">a list of column-value pairs to insert</param>
		public void Insert( DBCollisionAction collisionAction, string tableName, List<DBParameter> values )
		{
			lock ( m_databaseLock )
			{
				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						if ( collisionAction == DBCollisionAction.IGNORE )
						{
							command.CommandText = $"INSERT OR IGNORE";
						}
						else
						{
							command.CommandText = $"INSERT OR REPLACE";
						}

						command.CommandText += $" INTO {builder.QuoteIdentifier(tableName)}";

						string queryColumns = "(";
						string queryValues = "(";

						for ( int i = 0; i < values.Count(); i++ )
						{
							queryColumns += values[i].Column;
							queryValues += values[i].Value;

							if ( i < values.Count() - 1 )
							{
								queryColumns += ", ";
								queryValues += ", ";
							}
						}

						queryValues += ")";
						queryColumns += ")";

						command.CommandText += queryColumns + " VALUES" + queryValues + ";";

						command.ExecuteNonQuery();
					}

					m_sqlConnection.Close();
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// Efficiently inserts multiple rows into a table
		/// </summary>
		/// <param name="collisionAction">what to do if a row already exists</param>
		/// <param name="tableName">table to insert into</param>
		/// <param name="values">each list contains the column-value pairs to insert for one row</param>
		public void Insert( DBCollisionAction collisionAction, string tableName, List<List<DBParameter>> values )
		{
			lock ( m_databaseLock )
			{
				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					using ( SQLiteTransaction t = m_sqlConnection.BeginTransaction() )
					{
						using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
						{
							for ( int a = 0; a < values.Count; a++ )
							{
								if ( collisionAction == DBCollisionAction.IGNORE )
								{
									command.CommandText = $"INSERT OR IGNORE";
								}
								else
								{
									command.CommandText = $"INSERT OR REPLACE";
								}

								command.CommandText += $" INTO {builder.QuoteIdentifier(tableName)}";

								string queryColumns = "(";
								string queryValues = "(";

								for ( int i = 0; i < values[a].Count(); i++ )
								{
									queryColumns += values[a][i].Column;
									queryValues += values[a][i].Value;

									if ( i < values[a].Count() - 1 )
									{
										queryColumns += ", ";
										queryValues += ", ";
									}
								}

								queryValues += ")";
								queryColumns += ")";

								command.CommandText += queryColumns + " VALUES" + queryValues + ";";

								command.ExecuteNonQuery();
							}
						}

						t.Commit();
					}

					m_sqlConnection.Close();
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// Selects all rows and columns from the database
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="tableName">name of the table to select from</param>
		/// <returns>a list of data containers of the specified type containing the results, or null if there were no results</returns>
		public List<T> SelectAll<T>( string tableName, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			lock ( m_databaseLock )
			{
				_VerifyConnection();

				try
				{
					m_sqlConnection.Open();

					List<T> results = new List<T>();

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
		}

		/// <summary>
		/// Selects all rows from a table where a single predicate is true
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="tableName">name of the table to select from</param>
		/// <param name="predicates">predicate which must evaluate to true</param>
		/// <returns>the desired row, or null if there were no results</returns>
		public T Select<T>( string tableName, DBPredicate predicate, DBDataContainerCreator<T> dataContainerCreator ) where T : DBDataContainer<T>
		{
			lock ( m_databaseLock )
			{
				_VerifyConnection();

				List<T> queryResults = null;

				try
				{
					m_sqlConnection.Open();

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

						command.CommandText = $"SELECT * FROM {builder.QuoteIdentifier(tableName)} WHERE({builder.QuoteIdentifier(predicate.Column)} {_OperatorToString(predicate.Operator)} @value0);";

						command.Parameters.AddWithValue("@value0", predicate.Value);

						queryResults = _ExecuteSelectCommand<T>(command, dataContainerCreator);
					}
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
				finally
				{
					m_sqlConnection.Close();
				}

				return (queryResults != null && queryResults.Count > 0) ? queryResults[0] : null;
			}
		}

		/// <summary>
		/// Selects all rows from a table where all of a list of predicates are true
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="tableName">name of the table to select from</param>
		/// <param name="predicates">list of predicates, all of which must evaluate to true</param>
		/// <param name="predicateConjunction">how the list of predicates should be joined when performing the query</param>
		/// <returns>a list of data containers, each containing one row of the results, or null if there were no results</returns>
		public List<T> Select<T>( string tableName, List<DBPredicate> predicates, DBDataContainerCreator<T> dataContainerCreator, DBConjunction predicateConjunction ) where T : DBDataContainer<T>
		{
			lock ( m_databaseLock )
			{
				_VerifyConnection();

				List<T> queryResults = new List<T>();

				try
				{
					m_sqlConnection.Open();

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

						command.CommandText = $"SELECT * FROM {builder.QuoteIdentifier(tableName)} WHERE ";

						_AppendPredicates(command, predicates, predicateConjunction.ToString());

						command.CommandText += ";";

						queryResults = _ExecuteSelectCommand<T>(command, dataContainerCreator);
					}
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
				finally
				{
					m_sqlConnection.Close();
				}

				return queryResults;
			}
		}

		/// <summary>
		/// performs a single, simple update operation on a table
		/// </summary>
		/// <param name="tableName">name of the table to perform the updates within</param>
		/// <param name="values">the columns to update and the values to update them to</param>
		/// <param name="predicate">the predicate for the update operation</param>
		public void Update( string tableName, List<DBPredicate> values, DBPredicate predicate )
		{
			lock ( m_databaseLock )
			{
				_VerifyConnection();

				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						command.CommandText = $"UPDATE {builder.QuoteIdentifier(tableName)} SET ";

						_AppendPredicates(command, values, ",");

						command.CommandText += $" WHERE({builder.QuoteIdentifier(predicate.Column)} {_OperatorToString(predicate.Operator)} @predicateValue);";
						command.Parameters.AddWithValue("@predicateValue", predicate.Value);

						command.ExecuteNonQuery();
					}

					m_sqlConnection.Close();
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// performs a single update operation on a table
		/// </summary>
		/// <param name="tableName">name of the table to perform the updates within</param>
		/// <param name="values">the columns to update and the values to update them to</param>
		/// <param name="conjunction">defines how the predicates should be joined in the query</param>
		/// <param name="predicates">the predicates for the update operation, all of which must be true for the operation to succeed</param>
		public void Update( string tableName, List<DBPredicate> values, List<DBPredicate> predicates, DBConjunction conjunction = DBConjunction.AND )
		{
			lock ( m_databaseLock )
			{
				_VerifyConnection();

				try
				{
					m_sqlConnection.Open();

					SQLiteCommandBuilder builder = new SQLiteCommandBuilder();

					using ( SQLiteCommand command = m_sqlConnection.CreateCommand() )
					{
						command.CommandText = $"UPDATE {builder.QuoteIdentifier(tableName)} SET(";

						_AppendPredicates(command, values, ",");

						command.CommandText += ") WHERE(";

						_AppendPredicates(command, predicates, conjunction.ToString());

						command.CommandText += ");";
						Debug.WriteLine("Executing update query: " + command.CommandText);
						command.ExecuteNonQuery();
					}

					m_sqlConnection.Close();
				}
				catch ( Exception crap )
				{
					m_sqlConnection.Close();
					throw crap;
				}
			}
		}

		/// <summary>
		/// returns the number of files current indexed within the database
		/// </summary>
		/// <returns></returns>
		public int QueryDatabaseSize()
		{
			lock ( m_databaseLock )
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
		}
	}
}