using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Common;

namespace Filer
{
	using Debug = System.Diagnostics.Debug;

	public class VisibilityEventArgs
	{
		public bool IsVisible { get; set; }
	}

	public class SettingsWindowEventArgs
	{
		public DirectoryMonitor DirectoryMonitor { get; set; }

		public SettingsWindowEventArgs(DirectoryMonitor directoryMonitor)
		{
			DirectoryMonitor = directoryMonitor;
		}
	}

	public class MainWindowViewModel : ViewModelBase
	{
		public ICommand LaunchItemChosenCommand { get; set; }
		public ICommand EnterPressedCommand { get; set; }
		public ICommand ShowSettingsWindowCommand { get; set; }
		public ICommand ShowMoreResultsCommand { get; set; }

		public event EventHandler<VisibilityEventArgs> VisibilityChangeRequest;
		public event EventHandler<SettingsWindowEventArgs> DisplaySettingsWindowRequest;

		private ObservableCollection<Controls.SearchResultControl> m_limitedResultCollection;

		private DirectoryMonitor m_directoryMonitor;

		private DatabaseLink m_database;

		private DatabaseSearcher m_databaseSearcher;

		private SearchResult m_latestSearchResult;

		private readonly object m_resultLock;

		private string m_latestSearchResultDetails;
		private string m_statusMessage;

		private int m_databaseItemCount;

		private bool m_enableShowMoreButton;



		#region Public Properties



		public FilerSettings Settings { get; set; }

		public bool EnableShowMoreButton
		{
			get { return m_enableShowMoreButton; }
			set { m_enableShowMoreButton = value; OnPropertyChanged(); }
		}

		public ObservableCollection<Controls.SearchResultControl> LimitedResultCollection
		{
			get { return m_limitedResultCollection; }
			set { m_limitedResultCollection = value; OnPropertyChanged(); }
		}

		public SearchResult LatestSearchResult
		{
			get { return m_latestSearchResult; }
			set { m_latestSearchResult = value; OnPropertyChanged(); }
		}

		public string LatestSearchResultDetails
		{
			get { return m_latestSearchResultDetails; }
			set { m_latestSearchResultDetails = value; OnPropertyChanged(); }
		}

		public DirectoryMonitor DirectoryMonitor
		{
			get { return m_directoryMonitor; }
			set { m_directoryMonitor = value; OnPropertyChanged(); }
		}

		public string StatusMessage
		{
			get { return m_statusMessage; }
			set { m_statusMessage = value; OnPropertyChanged(); }
		}

		public DatabaseSearcher DatabaseSearcher
		{
			get { return m_databaseSearcher; }
			set { m_databaseSearcher = value; OnPropertyChanged(); }
		}

		public int DatabaseItemCount
		{
			get { return m_databaseItemCount; }
			set { m_databaseItemCount = value; OnPropertyChanged(); }
		}



		#endregion




		public MainWindowViewModel()
		{
			Settings = new FilerSettings();
			Debug.Assert(Settings != null);

			Settings.SessionLog = $"---Session Debug Log Started At {DateTime.Now.ToString()}---\n";

			LaunchItemChosenCommand = new RelayCommand(_OnLaunchItemChosen);
			EnterPressedCommand = new RelayCommand(_OnEnterPressed);
			ShowSettingsWindowCommand = new RelayCommand(_ShowSettingsWindow);
			ShowMoreResultsCommand = new RelayCommand(_ShowMoreResults);

			_InitializeDatabase();

			m_limitedResultCollection = new ObservableCollection<Controls.SearchResultControl>();

			m_latestSearchResult = null;

			m_resultLock = new object();

			StatusMessage = "Ready";
			m_enableShowMoreButton = false;

			m_directoryMonitor = new DirectoryMonitor();

			m_directoryMonitor.ScanCompleted += _DirectoryMonitor_ScanCompleted;
			m_directoryMonitor.MonitoredDirectoryRemoved += _DirectoryMonitor_MonitoredDirectoryRemoved;
			m_directoryMonitor.StatusChanged += _DirectoryMonitor_StatusChanged;
			m_directoryMonitor.ScanProgress += _DirectoryMonitor_ScanProgress;
			m_directoryMonitor.ScanStarted += _DirectoryMonitor_ScanStarted;
			m_directoryMonitor.MonitoredFileCreated += _DirectoryMonitor_MonitoredFileCreated;
			m_directoryMonitor.MonitoredFileDeleted += _DirectoryMonitor_MonitoredFileDeleted;
			m_directoryMonitor.MonitoredFileRenamed += _DirectoryMonitor_MonitoredFileRenamed;

			if ( Settings.FirstRun )
			{
				//add the user's special folders as a default
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Personal));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
				m_directoryMonitor.AddMonitoredDirectory(Environment.GetFolderPath(Environment.SpecialFolder.System), false); //just want top-level for calc, notepad, etc.

				Settings.MonitoredDirectories = m_directoryMonitor.GetFormattedStringFromDirectoryList();

				m_directoryMonitor.ScanMonitoredDirectories();
			}
			else
			{
				var extractedDirectories = DirectoryMonitor.GetDirectoryListFromFormattedString(Settings.MonitoredDirectories);
				foreach ( DirectoryMonitor.Directory directory in extractedDirectories )
				{
					m_directoryMonitor.AddMonitoredDirectory(directory);
				}
			}

			try
			{
				DatabaseItemCount = m_database.QueryDatabaseSize();
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}

		~MainWindowViewModel()
		{
			Settings.FirstRun = false;
			Settings.MonitoredDirectories = m_directoryMonitor.GetFormattedStringFromDirectoryList();
		}



		#region Directory Monitor Listeners



		private void _DirectoryMonitor_MonitoredFileRenamed( object sender, MonitoredFileRenamedArgs e )
		{
			try
			{
				//first find the file in the database
				var rawResult = m_database.Select<RawFileData>("FileCache", new DBPredicate("path", DBOperator.EQUALS, e.OldPath), new RawFileDataCreator());
				if ( rawResult == null )
				{
					//if it doesn't exist already, add it now
					int fileType = 0;
					int lastSlash = e.NewPath.LastIndexOf(@"\");
					if ( lastSlash == -1 ) return;

					if ( File.Exists(e.NewPath) ) fileType = 1;
					m_database.Insert<RawFileData>(DBCollisionAction.IGNORE, "FileCache", new RawFileData(-1, e.NewPath, e.NewPath.Substring(lastSlash + 1).ToLower(), fileType, null, 0, false, false));
				}
				else
				{
					//if the file does exist in the database, update the old record

					int lastSlash = e.NewPath.LastIndexOf(@"\");
					if ( lastSlash == -1 ) return;

					List<DBPredicate> updateValues = new List<DBPredicate>();
					updateValues.Add(new DBPredicate("path", DBOperator.EQUALS, e.NewPath));
					updateValues.Add(new DBPredicate("name", DBOperator.EQUALS, e.NewPath.Substring(lastSlash + 1).ToLower()));
					m_database.Update("FileCache", updateValues, new DBPredicate("path", DBOperator.EQUALS, e.OldPath));
				}
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}

		private void _DirectoryMonitor_MonitoredFileDeleted( object sender, MonitoredFileDeletedArgs e )
		{
			try
			{
				m_database.Delete("FileCache", new DBPredicate("path", DBOperator.EQUALS, e.OldPath));
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}

		private void _DirectoryMonitor_MonitoredFileCreated( object sender, MonitoredFileCreatedArgs e )
		{
			int fileType = 0;
			int lastSlash = e.NewPath.LastIndexOf(@"\");
			if ( lastSlash == -1 ) return;

			if ( File.Exists(e.NewPath) ) fileType = 1;

			try
			{
				m_database.Insert<RawFileData>(DBCollisionAction.IGNORE, "FileCache", new RawFileData(-1, e.NewPath, e.NewPath.Substring(lastSlash + 1).ToLower(), fileType, null, 0, false, false));
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}

		private void _DirectoryMonitor_ScanProgress( object sender, ScanProgressEventArgs e )
		{
			List<RawFileData> newFileData = new List<RawFileData>();

			foreach ( string file in e.FoundFiles )
			{
				int lastSlash = file.LastIndexOf(@"\");
				if ( lastSlash == -1 ) continue;

				newFileData.Add(new RawFileData(-1, file, file.Substring(lastSlash + 1).ToLower(), 1, null, 0, false, false));
			}

			foreach ( string directory in e.FoundDirectories )
			{
				int lastSlash = directory.LastIndexOf(@"\");
				if ( lastSlash == -1 ) continue;

				newFileData.Add(new RawFileData(-1, directory, directory.Substring(lastSlash + 1).ToLower(), 0, null, 0, false, false));
			}

			try
			{
				m_database.InsertTransacted<RawFileData>(DBCollisionAction.IGNORE, "FileCache", newFileData);
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}

		private void _DirectoryMonitor_StatusChanged( object sender, StatusChangedEventArgs e )
		{
			if ( e == null ) return;

			switch ( e.Status )
			{
				case DirectoryMonitor.Status.Idle:
					{
						if ( m_statusMessage.Equals("Searching For Files...") ) StatusMessage = "Ready";
						break;
					}
				case DirectoryMonitor.Status.ScanInProgress:
					{
						StatusMessage = "Searching For Files...";
						break;
					}
			}
		}

		private void _DirectoryMonitor_MonitoredDirectoryRemoved( object sender, DirectoryEventArgs e )
		{
			if ( e == null ) return;

			try
			{
				m_database.Delete("FileCache", new List<DBPredicate> { new DBPredicate("path", DBOperator.LIKE, e.Path + "%") });
				DatabaseItemCount = m_database.QueryDatabaseSize();
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}

			Settings.MonitoredDirectories = m_directoryMonitor.GetFormattedStringFromDirectoryList();
		}

		private void _DirectoryMonitor_ScanCompleted( object sender, ScanCompletedEventArgs e )
		{
			try
			{
				DatabaseItemCount = m_database.QueryDatabaseSize();
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}

		private void _DirectoryMonitor_ScanStarted( object sender, ScanStartedEventArgs e )
		{
			int lastSlash = e.Path.LastIndexOf(@"\");
			if ( lastSlash == -1 ) return;

			try
			{
				/*when starting a new scan, clear the database of all entries from the directory, except for those that have tags or 
				 * that the user has favorited (these will be removed during normal searching if they don't exist anymore)*/
				m_database.Delete("FileCache", new List<DBPredicate> { new DBPredicate("path", DBOperator.LIKE, e.Path + "%"), new DBPredicate("favorite", DBOperator.EQUALS, "0"), new DBPredicate("tags", DBOperator.EQUALS, "") });

				List<DBParameter> userPreferenceParameters = new List<DBParameter>();
				userPreferenceParameters.Add(new DBParameter("path", e.Path.Replace("'", "''")));
				userPreferenceParameters.Add(new DBParameter("is_favorite", "0"));
				userPreferenceParameters.Add(new DBParameter("is_hidden", "0"));
				m_database.Insert(DBCollisionAction.IGNORE, "UserPreferences", userPreferenceParameters);

				/*insert the path into the database, pulling existing user preferences from the UserPreferences table if they exist*/
				List<DBParameter> insertParameters = new List<DBParameter>();
				insertParameters.Add(new DBParameter("path", e.Path.Replace("'", "''")));
				insertParameters.Add(new DBParameter("name", e.Path.Replace("'", "''").Substring(lastSlash + 1).ToLower()));
				insertParameters.Add(new DBParameter("type", "0"));
				insertParameters.Add(new DBParameter("tags", ""));
				insertParameters.Add(new DBParameter("favorite", "(SELECT is_favorite FROM UserPreferences WHERE path='" + e.Path.Replace("'", "''") + "')"));
				insertParameters.Add(new DBParameter("hidden", "(SELECT is_hidden FROM UserPreferences WHERE path='" + e.Path.Replace("'", "''") + "')"));
				m_database.Insert(DBCollisionAction.IGNORE, "FileCache", insertParameters);

				m_database.Insert<RawFileData>(DBCollisionAction.IGNORE, "FileCache", new RawFileData(-1, e.Path, e.Path.Substring(lastSlash + 1).ToLower(), 0, null, 0, false, false));
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}



		#endregion



		private void _InitializeDatabase()
		{
			try
			{
				m_database = new DatabaseLink(System.AppDomain.CurrentDomain.BaseDirectory + @"\cache.db", "CacheDB");

				//ensure that the database has the necessary tables and indexes
				m_database.ExecuteNonQuery(@"CREATE TABLE IF NOT EXISTS [FileCache] ([id] INTEGER NOT NULL PRIMARY KEY UNIQUE, [path]  TEXT NOT NULL UNIQUE, [name]  TEXT NOT NULL, [type]  INTEGER NOT NULL, [tags]  TEXT NOT NULL DEFAULT '', [access_count]  INTEGER NOT NULL DEFAULT 0, [favorite]  INTEGER NOT NULL DEFAULT 0, [hidden]   INTEGER NOT NULL DEFAULT 0);");
				m_database.ExecuteNonQuery(@"CREATE INDEX IF NOT EXISTS [name_index] ON [FileCache] (name);");
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}

			m_databaseSearcher = new DatabaseSearcher(m_database);
			m_databaseSearcher.SearchComplete += _DatabaseSearcher_SearchComplete;
			m_databaseSearcher.SearchStarting += _DatabaseSearcher_SearchStarting;
			m_databaseSearcher.SearchCancelled += _DatabaseSearcher_SearchCancelled;
		}

		private void _ShowSettingsWindow( object obj )
		{
			if ( m_directoryMonitor == null ) return;

			DisplaySettingsWindowRequest?.Invoke(this, new SettingsWindowEventArgs(m_directoryMonitor));
		}

		private void _OnLaunchItemChosen( object selectedItem )
		{
			if ( selectedItem == null ) return;

			Filer.Controls.SearchResultControl control = (Filer.Controls.SearchResultControl)selectedItem;
			if ( control == null || control.SearchResultContent == null ) return;

			if ( _RunFile(control.SearchResultContent.Path) )
			{
				control.SearchResultContent.AccessCount += 1;
			}

			if ( Settings.ClearOnSearch == true )
			{
				m_databaseSearcher.Reset();
				lock ( m_resultLock )
				{
					_ClearSearch();
				}
			}

			if ( Settings.HideOnSearch == true )
			{
				_RequestVisibilityChange(false);
			}

			Console.WriteLine(control.SearchResultContent.Path);
		}

		private void _OnEnterPressed( object selectedItem )
		{
			_OnLaunchItemChosen(selectedItem);
		}

		/// <summary>
		/// adds more results to the displays results list when the user clicks the 'show more' button
		/// </summary>
		private void _ShowMoreResults( object obj )
		{
			lock ( m_resultLock )
			{
				if ( m_limitedResultCollection.Count == 0 || LatestSearchResult == null || m_limitedResultCollection.Count >= LatestSearchResult.ValidResults.Count ||
					m_limitedResultCollection.Count >= 2000 ) return;

				//add up to 100 items to the list, but if there are 10 or less results remaining after that, add them as well
				int maxItemsAfterAdditions = m_limitedResultCollection.Count + Settings.MaxResultsDisplayed;
				if ( maxItemsAfterAdditions + 10 >= LatestSearchResult.ValidResults.Count ) maxItemsAfterAdditions = LatestSearchResult.ValidResults.Count;

				//cap loaded search results at 2000 for memory and performance purposes
				if ( maxItemsAfterAdditions > 2000 ) maxItemsAfterAdditions = 2000;

				//need to be on the UI thread since we will be creating controls
				Application.Current.Dispatcher.Invoke((Action)delegate
				{
				//increase the weight of the existing elements by a tiny amount so that the sort order isn't disturbed
				//by new elements with the same weight (causing items to shift around for no apparent reason)
				foreach ( var control in m_limitedResultCollection )
					{
						control.SearchResultContent.Weight += 0.0000002;
					}

					for ( int i = m_limitedResultCollection.Count; i < LatestSearchResult.ValidResults.Count && i < maxItemsAfterAdditions; i++ )
					{
						Controls.SearchResultControl newResult = new Controls.SearchResultControl();
						newResult.SearchResultContent = LatestSearchResult.ValidResults[i];

						newResult.PreviewMouseLeftButtonUp += SearchResult_MouseLeftButtonUp;

					//if a file is modified we want to know about it so we can re-sort the list if necessary and write the changes to the DB
					LatestSearchResult.ValidResults[i].OnChangedEvent -= _OnFileChanged;
						LatestSearchResult.ValidResults[i].OnChangedEvent += _OnFileChanged;

					//load the icon for the new file
					LatestSearchResult.ValidResults[i].LoadFileIcon();

						m_limitedResultCollection.Add(newResult);
					}

				//since sorting is only performed on the elements loaded into the list, need to sort the new and old elements into place
				var sortedResults = m_limitedResultCollection.OrderByDescending(x => x.SearchResultContent.Weight).ToList();
					for ( int i = 0; i < sortedResults.Count; i++ )
					{
						m_limitedResultCollection.Move(m_limitedResultCollection.IndexOf(sortedResults[i]), i);
					}

					if ( LatestSearchResult.ValidResults.Count > m_limitedResultCollection.Count )
					{
						LatestSearchResultDetails = $"Showing top {String.Format("{0:n0}", m_limitedResultCollection.Count)} of {String.Format("{0:n0}", LatestSearchResult.ValidResults.Count)} results";
						EnableShowMoreButton = true;
					}
					else
					{
						LatestSearchResultDetails = $"Showing {String.Format("{0:n0}", LatestSearchResult.ValidResults.Count)} of {String.Format("{0:n0}", LatestSearchResult.ValidResults.Count)} results";
						EnableShowMoreButton = false;
					}
				});
			}
		}

		/// <summary>
		/// handler for when a search result is clicked
		/// </summary>
		/// <param name="sender">the SearchResultControl that was clicked</param>
		private void SearchResult_MouseLeftButtonUp( object sender, MouseButtonEventArgs e )
		{
			_OnLaunchItemChosen(sender);
		}

		private bool _RunFile( string path )
		{
			try
			{
				System.Diagnostics.Process.Start(path);

			} catch ( Exception e )
			{
				Debug.WriteLine("Error running file or directory: " + e.Message);
				return false;
			}

			return true;
		}

		private void _ClearSearch()
		{
			LatestSearchResult = null;
			LatestSearchResultDetails = "";
			EnableShowMoreButton = false;

			try
			{
				m_limitedResultCollection.Clear();
			}
			catch ( Exception ex )
			{
				Debug.WriteLine("_ClearSearch " + ex.Message);
			}
		}


		#region DatabaseSearcher Event Listeners


		private void _DatabaseSearcher_SearchStarting( object sender, SearchResult e )
		{
			lock ( m_resultLock )
			{
				Application.Current.Dispatcher.Invoke(() => _ClearSearch());

				if ( e != null && e.SearchTerm.Length > 0 )
				{
					LatestSearchResult = e;
				}
			}
		}

		private void _DatabaseSearcher_SearchCancelled( object sender, SearchResult e )
		{
			lock ( m_resultLock )
			{
				if ( LatestSearchResult != null && e.ID == LatestSearchResult.ID )
				{
					Application.Current.Dispatcher.Invoke(() => _ClearSearch());
				}
			}
		}

		private void _DatabaseSearcher_SearchComplete( object sender, SearchResult e )
		{
			lock ( m_resultLock )
			{
				if ( LatestSearchResult == null || e.ID != LatestSearchResult.ID ) return;

				EnableShowMoreButton = false;

				int numResultsToDisplay = Math.Min(Settings.MaxResultsDisplayed, e.ValidResults.Count);

				//need to be on the UI thread since we will be creating controls
				Application.Current.Dispatcher.Invoke((Action)delegate
				{
					m_limitedResultCollection.Clear();

					for ( int i = 0; i < numResultsToDisplay; i++ )
					{
						Controls.SearchResultControl newResult = new Controls.SearchResultControl();
						newResult.SearchResultContent = e.ValidResults[i];

						newResult.PreviewMouseLeftButtonUp += SearchResult_MouseLeftButtonUp;

						//if a file is modified we want to know about it so we can re-sort the list if necessary and write the changes to the DB
						e.ValidResults[i].OnChangedEvent -= _OnFileChanged;
						e.ValidResults[i].OnChangedEvent += _OnFileChanged;

						//load the file's icon
						e.ValidResults[i].LoadFileIcon();

						m_limitedResultCollection.Add(newResult);
					}

					if ( m_limitedResultCollection.Count < e.ValidResults.Count ) EnableShowMoreButton = true;

					if ( m_limitedResultCollection.Count > 0 )
					{
						if ( e.ValidResults.Count > m_limitedResultCollection.Count )
						{
							LatestSearchResultDetails = $"Showing top {String.Format("{0:n0}", Settings.MaxResultsDisplayed)} of {String.Format("{0:n0}", e.ValidResults.Count)} results";

						}
						else
						{
							LatestSearchResultDetails = $"Showing {String.Format("{0:n0}", e.ValidResults.Count)} of {String.Format("{0:n0}", e.ValidResults.Count)} results";

						}
					}
					else
					{
						LatestSearchResultDetails = (e.SearchTerm.Length == 0) ? "" : "Showing 0 of 0 results";
					}
				});
			}

			//remove any invalid results from the database so they don't appear in more searches
			List<List<DBPredicate>> removalPredicates = new List<List<DBPredicate>>();

			for ( int i = 0; i < e.InvalidResults.Count; i++ )
			{
				List<DBPredicate> filePredicates = new List<DBPredicate>();
				filePredicates.Add(new DBPredicate("path", DBOperator.EQUALS, e.InvalidResults[i].Path));
				removalPredicates.Add(filePredicates);
			}

			try
			{
				m_database.DeleteTransacted("FileCache", removalPredicates);
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}
		}


		#endregion DatabaseSearcher Event Listeners


		private void _OnFileChanged( object sender, FilePropertyChangedEventArgs e )
		{
			Common.FileInfo source = sender as Common.FileInfo;
			if ( source == null ) return;

			try
			{
				//update the file record in the database
				List<DBPredicate> updateValues = new List<DBPredicate>();
				updateValues.Add(new DBPredicate("access_count", DBOperator.EQUALS, source.AccessCount.ToString()));
				updateValues.Add(new DBPredicate("favorite", DBOperator.EQUALS, source.IsFavorite ? "1" : "0"));
				updateValues.Add(new DBPredicate("hidden", DBOperator.EQUALS, source.IsHidden ? "1" : "0"));
				m_database.Update("FileCache", updateValues, new DBPredicate("id", DBOperator.EQUALS, source.ID.ToString()));
			}
			catch ( Exception crap )
			{
				Settings.SessionLog += crap.Message + "\n";
				Debug.WriteLine(crap.Message);
			}

			if ( LatestSearchResult == null ) return;
			
			/*re-calculate the weight of the file and apply the changes by re-sorting the search result list*/
			double initialWeight = source.Weight;
			source.CalculateWeight(LatestSearchResult.SearchTerm);
			if ( initialWeight != source.Weight && Settings.SortImmediatelyOnFileChange && m_limitedResultCollection.Count > 1 )
			{
				try
				{
					//if the first element was promoted, or the last element demoted no sorting is neccessary
					if ( (initialWeight < source.Weight && m_limitedResultCollection[0].SearchResultContent == source) ||
						 (initialWeight > source.Weight && m_limitedResultCollection[m_limitedResultCollection.Count - 1].SearchResultContent == source) ) return;

					int newIndex = -1;
					int oldIndex = -1;
					for ( int i = 0; i < m_limitedResultCollection.Count; i++ )
					{
						if ( m_limitedResultCollection[i].SearchResultContent == source )
						{
							oldIndex = i;
							if ( newIndex != -1 ) break;
						}
						else if ( newIndex == -1 )
						{
							if ( (m_limitedResultCollection[i].SearchResultContent.Weight <= source.Weight) )
							{
								newIndex = i;
								if ( oldIndex != -1 ) break;
							}
						}
					}
					
					if ( oldIndex == -1 ) return;

					if ( newIndex == -1 ) newIndex = m_limitedResultCollection.Count - 1;

					//handle case where first item was demoted but is still the largest element (normally it would be swapped with the second element)
					if ( (oldIndex != 0 || newIndex != 1) || (m_limitedResultCollection[newIndex].SearchResultContent.Weight >= m_limitedResultCollection[oldIndex].SearchResultContent.Weight) )
					{
						m_limitedResultCollection.Move(oldIndex, newIndex);
					}
				}
				catch ( Exception ex )
				{
					Debug.WriteLine(ex.Message);
				}
			}
		}

		/// <summary>
		/// request that the listener window change its visibility
		/// </summary>
		private void _RequestVisibilityChange( bool isVisible )
		{
			VisibilityChangeRequest?.Invoke(this, new VisibilityEventArgs { IsVisible = isVisible });
		}

	}
}