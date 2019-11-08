using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Filer
{

	public class DatabaseAccess
	{
		[Flags]
		public enum MatchFilters { Files = 1, Folders = 2, Tags = 4, Any = 7 }

		private static string _LoadConnectionString( string con = "CacheDB" )
		{
			return System.Configuration.ConfigurationManager.ConnectionStrings[con].ConnectionString;
		}

		private static List<FileInfo> _ExecuteMatchQuery( string query )
		{
			List<FileInfo> matches = new List<FileInfo>();

			using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
			{
				con.Open();
				SQLiteCommand com = new SQLiteCommand(query, con);

				SQLiteDataReader result = com.ExecuteReader();
				if ( result.HasRows )
				{
					while ( result.Read() )
					{
						matches.Add(new FileInfo(result["tags"].ToString(),
												 result["path"].ToString(),
												 result["name"].ToString(),
												 Int32.Parse(result["access_count"].ToString()),
												 (FileType)Int32.Parse(result["type"].ToString()),
												 Int32.Parse(result["favorite"].ToString()).Equals(1),
												 Int32.Parse(result["hidden"].ToString()).Equals(1)));
					}
				}
			}

			return matches;
		}

		private static List<FileInfo> _ExecuteQueryCommand( SQLiteCommand command )
		{
			List<FileInfo> matches = new List<FileInfo>();

			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					command.Connection = con;

					SQLiteDataReader result = command.ExecuteReader();
					if ( result.HasRows )
					{
						while ( result.Read() )
						{
							matches.Add(new FileInfo(result["tags"].ToString(),
													 result["path"].ToString(),
													 result["name"].ToString(),
													 Int32.Parse(result["access_count"].ToString()),
													 (FileType)Int32.Parse(result["type"].ToString()),
													 Int32.Parse(result["favorite"].ToString()).Equals(1),
													 Int32.Parse(result["hidden"].ToString()).Equals(1)));
						}
					}
				}
			}
			catch (SQLiteException e )
			{
				Console.WriteLine("SQLite Exception executing query command: " + e.Message);
			}

			return matches;
		}

		private static List<FileInfo> _ExecuteQueryCommand( List<SQLiteCommand> commands )
		{
			List<FileInfo> matches = new List<FileInfo>();

			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						foreach ( SQLiteCommand command in commands )
						{
							command.Connection = con;

							SQLiteDataReader result = command.ExecuteReader();
							while ( result.Read() )
							{
								matches.Add(new FileInfo(result["tags"].ToString(),
														 result["path"].ToString(),
														 result["name"].ToString(),
														 Int32.Parse(result["access_count"].ToString()),
														 (FileType)Int32.Parse(result["type"].ToString()),
														 Int32.Parse(result["favorite"].ToString()).Equals(1),
														 Int32.Parse(result["hidden"].ToString()).Equals(1)));
							}
						}

						t.Commit();
					}
				}
			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception executing transacted query command: " + e.Message);
			}

			return matches;
		}

		private static void _ExecuteNonQueryCommand( SQLiteCommand command )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					command.Connection = con;

					command.ExecuteNonQuery();
				}
			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception executing non-query command: " + e.Message);
			}
		}

		/// <summary>
		/// attempts to create a new, read-to-use database file
		/// </summary>
		/// <returns>true if a database already existed or was successfully created, and false otherwise</returns>
		public static bool CreateNewDatabaseFromConnectionString()
		{
			if ( File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + @"\cache.db") == true ) return true;

			string createFileTableQuery = @"CREATE TABLE IF NOT EXISTS [FileCache] (
												[path]  TEXT NOT NULL UNIQUE PRIMARY KEY,
												[name]  TEXT NOT NULL,
												[type]  INTEGER NOT NULL,
												[tags]  TEXT NOT NULL DEFAULT '',
												[access_count]  INTEGER NOT NULL DEFAULT 0,
												[favorite]  INTEGER NOT NULL DEFAULT 0,
												[hidden]   INTEGER NOT NULL DEFAULT 0);";

			try
			{
				System.IO.File.Create(System.AppDomain.CurrentDomain.BaseDirectory + @"\cache.db").Close();

				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteCommand com = con.CreateCommand() )
					{
						com.CommandText = createFileTableQuery;
						com.ExecuteNonQuery();
					}
				}

			}
			catch ( Exception e )
			{
				MessageBox.Show(e.Message);
				Console.WriteLine("SQLite Exception creating new database: " + e.Message);
				return false;
			}

			return true;
		}

		public static void Insert( FileInfo file )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteCommand com = con.CreateCommand() )
					{
						//com.CommandText = $"INSERT OR IGNORE INTO FileCache VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)});";
						//com.CommandText = $"INSERT INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)}) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.Path}');";
						com.CommandText = "INSERT OR REPLACE INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES (@path, @name, @type, @tags, @access_count, @favorite, @hidden);";
						com.Parameters.AddWithValue("@path", file.Path);
						com.Parameters.AddWithValue("@name", file.Name);
						com.Parameters.AddWithValue("@type", file.Type);
						com.Parameters.AddWithValue("@tags", String.Join(";", file.Tags));
						com.Parameters.AddWithValue("@access_count", file.AccessCount);
						com.Parameters.AddWithValue("@favorite", file.IsFavorite);
						com.Parameters.AddWithValue("@hidden", file.IsHidden);
						com.ExecuteNonQuery();
					}
				}
			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception on insert: " + e.Message);
			}
		}

		public static void Insert( List<FileInfo> files )
		{
			if ( files.Count == 1 ) Insert(files[1]);

			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						using ( SQLiteCommand com = con.CreateCommand() )
						{
							foreach ( var file in files )
							{
								//com.CommandText = $"INSERT OR IGNORE INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash+1).ToLower()}',1);";
								//com.CommandText = $"INSERT OR IGNORE INTO FileCache VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)});";
								//com.CommandText = $"INSERT INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES('{file.Path}','{file.Name}',{(int)file.Type},'{file.Tags}',{file.AccessCount},{(file.IsFavorite ? 1 : 0)},{(file.IsHidden ? 1 : 0)}) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.Path}');";
								//com.ExecuteNonQuery();

								//com.CommandText = $"INSERT INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash + 1).ToLower()}',1) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.ToLower()}');";
								com.CommandText = "INSERT OR REPLACE INTO FileCache(path, name, type, tags, access_count, favorite, hidden) VALUES (@path, @name, @type, @tags, @access_count, @favorite, @hidden);";
								com.Parameters.AddWithValue("@path", file.Path);
								com.Parameters.AddWithValue("@name", file.Name);
								com.Parameters.AddWithValue("@type", file.Type);
								com.Parameters.AddWithValue("@tags", String.Join(";", file.Tags));
								com.Parameters.AddWithValue("@access_count", file.AccessCount);
								com.Parameters.AddWithValue("@favorite", file.IsFavorite);
								com.Parameters.AddWithValue("@hidden", file.IsHidden);

								com.ExecuteNonQuery();
							}
						}

						t.Commit();
					}
				}

			}catch(SQLiteException e)
			{
				Console.WriteLine("SQLite Exception on multi insert: " + e.Message);
			}
		}

		public static void InsertDirectories( List<string> directoryPaths )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						using ( SQLiteCommand com = con.CreateCommand() )
						{
							foreach ( var dir in directoryPaths )
							{
								if( dir.Equals(@"c:\users\indeed\desktop\threadpool") )
								{
									Console.WriteLine("inserting threadpool folder..");
								}
								int lastSlash = dir.LastIndexOf(@"\");
								if ( lastSlash == -1 ) continue;

								string name = (lastSlash == dir.Length - 1) ? "" : dir.Substring(lastSlash + 1).ToLower();

								com.CommandText = "INSERT OR IGNORE INTO FileCache(path, name, type) VALUES (@path, @name, 0);";
								com.Parameters.AddWithValue("@path", dir.ToLower());
								com.Parameters.AddWithValue("@name", name);
								com.ExecuteNonQuery();

								if ( dir.Equals(@"c:\users\indeed\desktop\threadpool") )
								{
									Console.WriteLine($"inserted. name: {name}");
								}
							}
						}

						t.Commit();
					}
				}

			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception on multi insert: " + e.Message);
			}
		}

		public static void InsertFiles( List<string> filePaths )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						using ( SQLiteCommand com = con.CreateCommand() )
						{
							foreach ( var file in filePaths )
							{
								int type = 1;
								int lastSlash = file.LastIndexOf(@"\");
								if ( lastSlash == -1 ) continue;

								string name = file.Substring(lastSlash + 1).ToLower();

								if (lastSlash == file.Length - 1)
								{
									type = 0;
									name = "";
								}

								//com.CommandText = $"INSERT INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash + 1).ToLower()}',1) WHERE NOT EXISTS(SELECT 1 FROM FileCache WHERE path='{file.ToLower()}');";
								com.CommandText = "INSERT OR IGNORE INTO FileCache(path, name, type) VALUES (@path, @name, @type);";
								com.Parameters.AddWithValue("@path", file.ToLower());
								com.Parameters.AddWithValue("@name", name);
								com.Parameters.AddWithValue("@type", type);
								//com.CommandText = $"INSERT OR IGNORE INTO FileCache(path, name, type) VALUES('{file.ToLower()}','{file.Substring(lastSlash+1).ToLower()}',1);";
								com.ExecuteNonQuery();
							}
						}

						t.Commit();
					}
				}

			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception on multi insert: " + e.Message);
			}
		}

		
		public static void UpdateFiles( List<FileInfo> files )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						using ( SQLiteCommand com = con.CreateCommand() )
						{
							foreach ( var file in files )
							{
								string tags = "";
								foreach ( string tag in file.Tags )
								{
									if ( tag.Length > 0 ) tags += tag + ";";
								}
								com.CommandText = $"UPDATE FileCache SET path='{file.Path}', name='{file.Name}', tags='{tags}', access_count={file.AccessCount}, favorite={(file.IsFavorite ? 1 : 0)}, hidden={(file.IsHidden ? 1 : 0)} WHERE(path='{file.Path}');";
								com.ExecuteNonQuery();
							}
						}

						t.Commit();
					}
				}

			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception on file update: " + e.Message);
			}

		}

		public static void Remove( List<string> paths )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						using ( SQLiteCommand com = con.CreateCommand() )
						{
							foreach ( var path in paths )
							{
								Console.WriteLine($"Removing paths '{path}'");
								com.CommandText = $"DELETE FROM FileCache WHERE path='{path.ToLower()}';";
								com.ExecuteNonQuery();
							}
						}

						t.Commit();
					}
				}

			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception deleting file: " + e.Message);
			}
		}

		public static void Remove( List<FileInfo> files )
		{
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteTransaction t = con.BeginTransaction() )
					{
						using ( SQLiteCommand com = con.CreateCommand() )
						{
							foreach ( var file in files )
							{
								Console.WriteLine($"Removing file '{file.Path}'");
								com.CommandText = $"DELETE FROM FileCache WHERE path='{file.Path.ToLower()}';";
								com.ExecuteNonQuery();
							}
						}

						t.Commit();
					}
				}

			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception deleting file: " + e.Message);
			}
		}

		public static void RemoveDirectory( string path )
		{
			Console.WriteLine($"Removing directory '{path}'");
			try
			{
				using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
				{
					con.Open();

					using ( SQLiteCommand com = con.CreateCommand() )
					{
						com.CommandText = $"DELETE FROM FileCache WHERE path LIKE '{path.ToLower()}%';";
						com.ExecuteNonQuery();
					}
				}
			}
			catch ( SQLiteException e )
			{
				Console.WriteLine("SQLite Exception deleting directory: " + e.Message);
			}
		}

		public static List<FileInfo> Match( string searchTerm, MatchFilters filters = MatchFilters.Any )
		{
			switch(filters)
			{
				case MatchFilters.Any:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' UNION SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%';");
					}
				case MatchFilters.Tags | MatchFilters.Files:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=1 UNION SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%' AND type=1;");
					}
				case MatchFilters.Tags | MatchFilters.Folders:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=0 UNION SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%' AND type=0;");
					}
				case MatchFilters.Files | MatchFilters.Folders:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%';");
					}
				case MatchFilters.Tags:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE tags LIKE '%{searchTerm}%';");
					}
				case MatchFilters.Files:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=1;");
					}
				case MatchFilters.Folders:
					{
						return _ExecuteMatchQuery($"SELECT * FROM FileCache WHERE name LIKE '%{searchTerm}%' AND type=0;");
					}
				default:
					{
						Console.WriteLine("Invalid search filter");
						return new List<FileInfo>();
					}
			}
		}

		public static FileInfo FindFile( string path )
		{
			FileInfo foundFile = null;
			using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
			{
				con.Open();
				SQLiteCommand com = new SQLiteCommand($"SELECT * FROM FileCache WHERE path='{path.ToLower()}';", con);

				SQLiteDataReader result = com.ExecuteReader();
				if ( result.HasRows )
				{
					while ( result.Read() )
					{
						foundFile = new FileInfo(result["tags"].ToString(),
												 result["path"].ToString(),
												 result["name"].ToString(),
												 Int32.Parse(result["access_count"].ToString()),
												 (FileType)Int32.Parse(result["type"].ToString()),
												 Int32.Parse(result["favorite"].ToString()).Equals(1),
												 Int32.Parse(result["hidden"].ToString()).Equals(1));
					}
				}
			}

			return foundFile;
		}

		/// <summary>
		/// returns the number of files current indexed within the database
		/// </summary>
		/// <returns></returns>
		public static int QueryDatabaseSize()
		{
			using ( SQLiteConnection con = new SQLiteConnection(_LoadConnectionString()) )
			{
				con.Open();
				SQLiteCommand com = new SQLiteCommand($"SELECT COUNT(path) FROM FileCache", con);

				return Convert.ToInt32(com.ExecuteScalar());
			}
		}

	}
}
