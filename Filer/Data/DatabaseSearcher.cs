using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Common;

namespace Filer
{

	using Debug = System.Diagnostics.Debug;

	public class SearchResult : NotifyPropertyBase
	{
		private static int m_uniqueID = 0;

		private bool m_complete;

		public int ID { get; set; }
		public List<Common.FileInfo> ValidResults { get; set; }
		public List<Common.FileInfo> InvalidResults { get; set; }
		public string SearchTerm { get; set; }
		public bool WildcardSearch { get; set; }

		public bool Complete
		{
			get { return m_complete; }
			set { m_complete = value; OnPropertyChanged(); }
		}

		public SearchResult()
		{
			ID = m_uniqueID++;
			SearchTerm = "";
			ValidResults = new List<Common.FileInfo>();
			InvalidResults = new List<Common.FileInfo>();
			m_complete = false;
			WildcardSearch = false;
		}

		public SearchResult( string searchTerm, List<Common.FileInfo> validResults, List<Common.FileInfo> invalidResults )
		{
			ID = m_uniqueID++;
			SearchTerm = searchTerm;
			ValidResults = validResults;
			InvalidResults = invalidResults;
			m_complete = false;

			if ( SearchTerm.StartsWith("*") ) WildcardSearch = true;
		}
	}

	public class DatabaseSearcher : NotifyPropertyBase
	{
		public event EventHandler<SearchResult> SearchComplete;
		public event EventHandler<SearchResult> SearchStarting;
		public event EventHandler<SearchResult> SearchCancelled;

		private List<SearchFilter> m_availableSearchFilters;

		private DatabaseLink m_database;

		private SearchFilter m_activeSearchFilter;

		private FilerSettings m_settings;

		private System.Timers.Timer m_searchDelayTimer;

		private readonly object m_newSearchLock;
		private readonly object m_databaseLock;

		private string m_searchTerm;

		private int m_activeSearchID;


		#region Public Properties


		public string SearchTerm
		{
			get { return m_searchTerm; }
			set
			{
				m_searchTerm = value;
				OnPropertyChanged();
				_OnSearchTermChanged(m_searchTerm);
			}
		}

		public SearchFilter ActiveSearchFilter
		{
			get { return m_activeSearchFilter; }
			set
			{
				m_activeSearchFilter = value;
				OnPropertyChanged();

				NewSearch(m_searchTerm);
			}
		}

		public List<SearchFilter> AvailableSearchFilters
		{
			get { return m_availableSearchFilters; }
			set { m_availableSearchFilters = value; OnPropertyChanged(); }
		}


		#endregion Public Properties


		public DatabaseSearcher( DatabaseLink database )
		{
			m_database = database;

			m_settings = new FilerSettings();

			m_availableSearchFilters = new List<SearchFilter>();
			m_availableSearchFilters.Add(new SearchFilter(DatabaseAccess.MatchFilters.Any, Properties.Resources.filter_any));
			m_availableSearchFilters.Add(new SearchFilter(DatabaseAccess.MatchFilters.Folders, Properties.Resources.filter_folder));
			m_availableSearchFilters.Add(new SearchFilter(DatabaseAccess.MatchFilters.Files, Properties.Resources.filter_file));
			m_availableSearchFilters.Add(new SearchFilter(DatabaseAccess.MatchFilters.Tags, Properties.Resources.filter_tag));

			m_activeSearchFilter = m_availableSearchFilters[0];
			m_activeSearchID = 0;
			m_searchTerm = "";
			m_newSearchLock = new object();
			m_databaseLock = new object();

			m_searchDelayTimer = new System.Timers.Timer();
			m_searchDelayTimer.Interval = 250;
			m_searchDelayTimer.Elapsed += _SearchDelayTimer_Elapsed;
			m_searchDelayTimer.AutoReset = false;
		}

		private void _SearchDelayTimer_Elapsed( object sender, System.Timers.ElapsedEventArgs e )
		{
			NewSearch(m_searchTerm);
		}

		private void _OnSearchTermChanged( string newSearchTerm )
		{
			m_searchDelayTimer.Stop();
			m_searchDelayTimer.Start();
		}

		/// <summary>
		/// verifies that all files in a list exist
		/// </summary>
		/// <param name="files"></param>
		/// <returns>a list containing all of the invalid files</returns>
		private List<Common.FileInfo> _ValidateFiles( List<Common.FileInfo> files )
		{
			List<Common.FileInfo> invalidFiles = new List<Common.FileInfo>();

			//get the list of excluded file extensions so files can be checked against it
			List<string> excludedExtensions = new List<string>();
			if ( m_settings.ExcludeCertainExtensions == true ) excludedExtensions = m_settings.ExcludedExtensions.Split(';').ToList();

			//make sure all extensions are at least 1 character in length
			for ( int i = excludedExtensions.Count - 1; i >= 0; i-- )
			{
				if ( excludedExtensions[i].Length == 0 ) excludedExtensions.RemoveAt(i);
			}

			for ( int i = files.Count - 1; i >= 0; i-- )
			{
				if ( (files[i].Type == FileType.File && File.Exists(files[i].Path) == false) ||
					(files[i].Type == FileType.Directory && Directory.Exists(files[i].Path) == false) )
				{
					invalidFiles.Add(files[i]);
					files.Remove(files[i]);
					continue;
				}
				foreach ( string extension in excludedExtensions )
				{
					if ( files[i].Path.EndsWith("." + extension) == true )
					{
						invalidFiles.Add(files[i]);
						files.Remove(files[i]);
						break;
					}
				}
			}

			return invalidFiles;
		}

		private void _SearchFiles( SearchResult resultContainer )
		{
			Debug.Assert(resultContainer != null);

			DatabaseAccess.MatchFilters activeFilter = m_activeSearchFilter != null ? m_activeSearchFilter.Type : DatabaseAccess.MatchFilters.Any;

			if ( resultContainer.SearchTerm == null || resultContainer.SearchTerm.Length <= 1 )
			{
				return;
			}

			List<RawFileData> rawResults = new List<RawFileData>();
			List<DBPredicate> searchPredicates = new List<DBPredicate>();
			List<Common.FileInfo> convertedData = new List<Common.FileInfo>();

			try
			{
				switch ( activeFilter )
				{
					case DatabaseAccess.MatchFilters.Any:
						{
							if ( resultContainer.WildcardSearch == true )
							{
								searchPredicates.Add(new DBPredicate("name", DBOperator.LIKE, "%" + resultContainer.SearchTerm + "%"));
								searchPredicates.Add(new DBPredicate("tags", DBOperator.LIKE, "%" + resultContainer.SearchTerm + "%"));
							}
							else
							{
								searchPredicates.Add(new DBPredicate("name", DBOperator.LIKE, resultContainer.SearchTerm + "%"));
								searchPredicates.Add(new DBPredicate("tags", DBOperator.LIKE, resultContainer.SearchTerm + "%"));
							}
							rawResults = m_database.Select<RawFileData>("FileCache", searchPredicates, new RawFileDataCreator(), DBConjunction.OR);
							break;
						}
					case DatabaseAccess.MatchFilters.Files:
						{
							if ( resultContainer.WildcardSearch == true )
							{
								searchPredicates.Add(new DBPredicate("name", DBOperator.LIKE, "%" + resultContainer.SearchTerm + "%"));
							}
							else
							{
								searchPredicates.Add(new DBPredicate("name", DBOperator.LIKE, resultContainer.SearchTerm + "%"));
							}
							searchPredicates.Add(new DBPredicate("type", DBOperator.EQUALS, "1"));

							rawResults = m_database.Select<RawFileData>("FileCache", searchPredicates, new RawFileDataCreator(), DBConjunction.AND);
							break;
						}
					case DatabaseAccess.MatchFilters.Folders:
						{
							if ( resultContainer.WildcardSearch == true )
							{
								searchPredicates.Add(new DBPredicate("name", DBOperator.LIKE, "%" + resultContainer.SearchTerm + "%"));
							}
							else
							{
								searchPredicates.Add(new DBPredicate("name", DBOperator.LIKE, resultContainer.SearchTerm + "%"));
							}
							searchPredicates.Add(new DBPredicate("type", DBOperator.EQUALS, "0"));

							rawResults = m_database.Select<RawFileData>("FileCache", searchPredicates, new RawFileDataCreator(), DBConjunction.AND);
							break;
						}
					case DatabaseAccess.MatchFilters.Tags:
						{
							break;
						}
					default: break;
				}
			}
			catch ( Exception crap )
			{
				m_settings.SessionLog += crap.Message + "\n";
				return;
			}

			if ( rawResults.Count == 0 )
			{
				return;
			}

			//take the retrieved raw data and convert it into the proper form
			for(int i = rawResults.Count - 1; i >= 0; i--)
			{
				Common.FileInfo info = new Common.FileInfo(rawResults[i], Properties.Resources.file, Properties.Resources.folder);
				info.CalculateWeight(resultContainer.SearchTerm);
				convertedData.Add(info);
				rawResults.RemoveAt(i);
			}

			/*sort the results in descending order based on relevancy weight*/
			convertedData.Sort(( a, b ) => -1 * a.Weight.CompareTo(b.Weight));

			//remove all invalid files from the results list and store the results in the container
			resultContainer.InvalidResults = _ValidateFiles(convertedData);
			resultContainer.ValidResults = convertedData;
		}

		private void _OnSearchComplete( SearchResult results )
		{
			SearchComplete?.Invoke(this, results);
		}

		private void _OnSearchStarting( SearchResult results )
		{
			SearchStarting?.Invoke(this, results);
		}

		private void _OnSearchCancelled( SearchResult results )
		{
			SearchCancelled?.Invoke(this, results);
		}

		public async void NewSearch( string searchTerm )
		{
			Debug.Assert(searchTerm != null);

			SearchResult resultContainer = null;

			lock ( m_newSearchLock )
			{
				resultContainer = new SearchResult();
				resultContainer.SearchTerm = searchTerm;
				resultContainer.WildcardSearch = true;

				if ( m_settings.RequireWildcard == true )
				{
					//don't include wildcard character in the search term
					resultContainer.SearchTerm = (searchTerm.StartsWith("*")) ? searchTerm.Substring(1) : searchTerm;
					resultContainer.WildcardSearch = (searchTerm.StartsWith("*")) ? true : false;
				}
				m_activeSearchID = resultContainer.ID;

				_OnSearchStarting(resultContainer);
			}

			await Task.Run(() => _SearchFiles(resultContainer));

			lock ( m_newSearchLock )
			{
				if ( resultContainer.ID != m_activeSearchID )
				{//if a new search has begun since the last was started, cancel the search
					_OnSearchCancelled(resultContainer);
				}
				else
				{
					resultContainer.Complete = true;
					_OnSearchComplete(resultContainer);
				}
			}
		}

		public void Reset()
		{
			SearchTerm = "";
			ActiveSearchFilter = m_availableSearchFilters[0];
		}

	}
}
