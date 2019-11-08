using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Common;

namespace Filer
{
	public class DatabaseWriter
	{

		DatabaseLink m_database;

		public DatabaseWriter( string databasePath, string connectionStringName )
		{
			m_database = new DatabaseLink(databasePath, connectionStringName);
		}

		public void InsertNew( DBCollisionAction collisionAction, string path )
		{
			string escapedPath = path.Replace("'", "''");

			int lastSlash = path.LastIndexOf(@"\");
			if ( lastSlash == -1 ) return;

			//create a new entry in the user preferences table for this file
			List<DBParameter> userPreferenceParameters = new List<DBParameter>();
			userPreferenceParameters.Add(new DBParameter("path", escapedPath));
			userPreferenceParameters.Add(new DBParameter("tags", ""));
			userPreferenceParameters.Add(new DBParameter("is_favorite", "0"));
			userPreferenceParameters.Add(new DBParameter("is_hidden", "0"));
			m_database.Insert(DBCollisionAction.IGNORE, "UserPreferences", userPreferenceParameters);

			/*insert the path into the database, pulling existing user preferences from the UserPreferences table*/
			List<DBParameter> insertParameters = new List<DBParameter>();
			insertParameters.Add(new DBParameter("path", escapedPath));
			insertParameters.Add(new DBParameter("name", escapedPath.Substring(lastSlash + 1).ToLower()));
			insertParameters.Add(new DBParameter("type", "0"));
			m_database.Insert(DBCollisionAction.IGNORE, "FileCache", insertParameters);
		}

		public void InsertExisting( DBCollisionAction collisionAction, FileInfo file )
		{
			string escapedPath = file.Path.Replace("'", "''");

			int lastSlash = file.Path.LastIndexOf(@"\");
			if ( lastSlash == -1 ) return;

			//create a new entry in the user preferences table for this file
			List<DBParameter> userPreferenceParameters = new List<DBParameter>();
			userPreferenceParameters.Add(new DBParameter("path", escapedPath));
			userPreferenceParameters.Add(new DBParameter("tags", String.Join(",", file.Tags)));
			userPreferenceParameters.Add(new DBParameter("is_favorite", file.IsFavorite.ToString()));
			userPreferenceParameters.Add(new DBParameter("is_hidden", file.IsHidden.ToString()));
			m_database.Insert(DBCollisionAction.REPLACE, "UserPreferences", userPreferenceParameters);

			/*insert the path into the database, pulling existing user preferences from the UserPreferences table*/
			List<DBParameter> insertParameters = new List<DBParameter>();
			insertParameters.Add(new DBParameter("path", escapedPath));
			insertParameters.Add(new DBParameter("name", escapedPath.Substring(lastSlash + 1).ToLower()));
			insertParameters.Add(new DBParameter("type", "0"));
			m_database.Insert(DBCollisionAction.IGNORE, "FileCache", insertParameters);
		}

	}
}
