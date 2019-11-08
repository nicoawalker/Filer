using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

using Common;

namespace TagEditor
{

	using Debug = System.Diagnostics.Debug;

	public class MainWindowViewModel : ViewModelBase
	{

		ICollectionView m_fileListView;
		ObservableCollection<string> m_fileList;
		List<string> m_selectedFiles;

		ICollectionView m_availableTagListView;
		ICollectionView m_availableTagGroupListView;
		ObservableCollection<Tag> m_availableTagList;

		ICollectionView m_appliedTagListView;
		ICollectionView m_appliedTagGroupListView;
		ObservableCollection<Tag> m_appliedTagList;

		DatabaseLink m_database;

		string m_rootDirectory;


		#region Public Properties


		public ICollectionView AvailableTagListView
		{
			get { return m_availableTagListView; }
			set { m_availableTagListView = value; OnPropertyChanged(); }
		}

		public ICollectionView AppliedTagListView
		{
			get { return m_appliedTagListView; }
			set { m_appliedTagListView = value; OnPropertyChanged(); }
		}

		public ICollectionView AvailableTagGroupListView
		{
			get { return m_availableTagGroupListView; }
			set { m_availableTagGroupListView = value; OnPropertyChanged(); }
		}

		public ICollectionView AppliedTagGroupListView
		{
			get { return m_appliedTagGroupListView; }
			set { m_appliedTagGroupListView = value; OnPropertyChanged(); }
		}

		public ICollectionView FileListView
		{
			get { return m_fileListView; }
			set { m_fileListView = value; OnPropertyChanged(); }
		}

		public List<string> SelectedFiles
		{
			get { return m_selectedFiles; }
			set
			{
				m_selectedFiles = value;
				_OnSelectedFilesChanged();
			}
		}

		public string RootDirectory
		{
			get { return m_rootDirectory; }
			set { m_rootDirectory = value; OnPropertyChanged(); }
		}


		#endregion Public Properties


		public MainWindowViewModel( List<string> startupArgs )
		{
			
			m_fileList = new ObservableCollection<string>();
			m_appliedTagList = new ObservableCollection<Tag>();
			m_availableTagList = new ObservableCollection<Tag>();
			SelectedFiles = new List<string>();

			m_fileListView = CollectionViewSource.GetDefaultView(m_fileList);
			m_fileListView.SortDescriptions.Add(new SortDescription("", ListSortDirection.Ascending));

			//views for the individual tags
			m_availableTagListView = CollectionViewSource.GetDefaultView(m_availableTagList);
			m_availableTagListView.SortDescriptions.Add(new SortDescription("Label", ListSortDirection.Ascending));

			m_appliedTagListView = CollectionViewSource.GetDefaultView(m_appliedTagList);
			m_appliedTagListView.SortDescriptions.Add(new SortDescription("Label", ListSortDirection.Ascending));

			//views for each tag group
			m_availableTagGroupListView = CollectionViewSource.GetDefaultView(m_availableTagList);
			m_availableTagGroupListView.SortDescriptions.Add(new SortDescription("Label", ListSortDirection.Ascending));

			m_appliedTagGroupListView = CollectionViewSource.GetDefaultView(m_appliedTagList);
			m_appliedTagGroupListView.SortDescriptions.Add(new SortDescription("Label", ListSortDirection.Ascending));

			_InitializeDatabase();

			_LoadAllTags();

			/* parse the input files. if all files are in the same directory, get all files in the directory
			 * and select the initial input files. otherwise, just load the input files*/
			string directory = "";
			bool singleDirectory = true;
			//first figure out whether all files are in the same folder
			for( int i = startupArgs.Count - 1; i >= 0; i-- )
			{
				//only want to consider files, remove all directories
				if(Directory.Exists(startupArgs[i]) == true)
				{
					startupArgs.RemoveAt(i);
					continue;
				}

				string convertedInput = startupArgs[i].ToLower().Replace(@"/", @"\");
				int index = convertedInput.LastIndexOf(@"\");
				if ( index == -1 ) continue;

				string currentDirectory = convertedInput.Substring(0, index + 1);

				if ( directory.Equals("") )
				{
					directory = currentDirectory;
				}
				else
				{
					if(directory.Equals(currentDirectory) == false)
					{
						singleDirectory = false;
					}
				}
			}

			if ( startupArgs.Count != 0 && directory.Equals("") == false )
			{
				if ( singleDirectory )
				{//file list should contain all files in the directory, with the input files selected
					RootDirectory = directory;

				}
				else
				{//file list should only contain the input files
					RootDirectory = "";
					m_fileList = new ObservableCollection<string>(startupArgs);
				}
			}
		}

		private void _InitializeDatabase()
		{
			m_database = new DatabaseLink(System.AppDomain.CurrentDomain.BaseDirectory + @"tags.db", "TagDB");

			//ensure that the tags and filetags tables exist in the database
			m_database.ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS [Tags] ([id]  INTEGER NOT NULL PRIMARY KEY UNIQUE, [label]  TEXT NOT NULL UNIQUE, [color]  TEXT NOT NULL UNIQUE, [children]  TEXT NOT NULL DEFAULT '');");
			m_database.ExecuteNonQuery(@"CREATE INDEX IF NOT EXISTS [Label_Index] on [Tags](label);");
			m_database.ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS [FileTags] ([path]  TEXT NOT NULL PRIMARY KEY UNIQUE, [tags]  TEXT NOT NULL UNIQUE);");
		}

		/// <summary>
		/// loads all tags from the database and places them in the available tags list
		/// </summary>
		private void _LoadAllTags()
		{
			try
			{
				List<Tag> dbResults = m_database.SelectAll<Tag>("Tags", new TagCreator());
				foreach ( Tag tag in dbResults )
				{
					m_availableTagList.Add(tag);
				}
			}
			catch ( Exception crap )
			{
				Debug.WriteLine("Failed to load tags: " + crap.Message);
			}
		}

		/// <summary>
		/// loads all of the tags that the input files have in common and places them in the applied tags list
		/// </summary>
		private void _LoadCommonTags( List<string> files )
		{
			/*first use the file paths to retrieve a list of FileTags that contain all of the tags for each file path*/
			List<DBPredicate> predicates = new List<DBPredicate>();
			foreach ( string filePath in files )
			{
				predicates.Add(new DBPredicate("path", DBOperator.EQUALS, filePath));
			}
			List<FileTags> dbResults = m_database.Select<FileTags>("FileTags", predicates, new FileTagsCreator(), DBConjunction.AND);

			//now get all of the tags that are common between all the FileTags
			List<int> commonTags = new List<int>();
		}

		private void _OnSelectedFilesChanged()
		{
			_LoadCommonTags(m_selectedFiles);
		}

	}
}
