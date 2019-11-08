using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Common
{
	public class FilerSettings : NotifyPropertyBase
	{
		private readonly object m_lock;

		//how far the top-left corner of the search window is from the top-left corner of the monitor, in percent (0-1)
		private System.Windows.Point m_relativeWindowPos;

		private string m_monitoredDirectories;
		private string m_excludedExtensions; //file extensions to be excluded, separated by a semi-colon
		private string m_sessionLog; //stores debug messages for the duration of each run

		private int m_windowX;
		private int m_windowY;
		private int m_windowWidth;
		private int m_restrictedResultCountValue;
		private int m_maxResultsDisplayed;

		private bool m_firstRun;
		private bool m_startWithWindows;
		private bool m_startMinimized;
		private bool m_allowRepositioning;
		private bool m_followMouse;
		private bool m_rememberPosition;
		private bool m_minimizeOnClose;
		private bool m_minimizeOnFocusLost;
		private bool m_clearOnHide; //clear the search box whenever the search window is minimized?
		private bool m_hideOnSearch; //hide the search window after launching an item?
		private bool m_clearOnSearch; //clear the search box whenever launching an item?
		private bool m_topmost; //keep the main search window topmost?
		private bool m_sortImmediatelyOnFileChange; //sort the displayed files list whenever a file changes?
		private bool m_requireWildcard; //should the wildcard character (*) be required at the front of a search term to include results that don't begin with it?
		private bool m_excludeCertainExtensions; //exclude certain file types from being scanned or searched for?


		#region Properties


		public int RestrictedResultCountValue
		{
			get { return m_restrictedResultCountValue = Int32.Parse(_ReadOrAdd("25")); }
			set
			{
				m_restrictedResultCountValue = value;
				_WriteSetting(m_restrictedResultCountValue.ToString());
				OnPropertyChanged();
			}
		}

		public string MonitoredDirectories
		{
			get
			{
				return m_monitoredDirectories = _ReadOrAdd("");
			}
			set
			{
				m_monitoredDirectories = value;
				_WriteSetting(m_monitoredDirectories);
				OnPropertyChanged();
			}
		}

		public string ExcludedExtensions
		{
			get { return m_excludedExtensions = _ReadOrAdd(""); }
			set
			{
				List<string> extensions = value.Replace(' ', ';').Replace(',', ';').Replace('.', ';').Replace(";;", ";").Split(';').ToList();
				extensions.Sort();
				m_excludedExtensions = String.Join(";", extensions);

				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public string SessionLog
		{
			get { return m_sessionLog = _ReadOrAdd(""); }
			set
			{
				m_sessionLog = value;
				_WriteSetting(m_sessionLog);
				OnPropertyChanged();
			}
		}

		public bool ExcludeCertainExtensions
		{
			get { return m_excludeCertainExtensions = _ReadOrAdd("False").Equals("True"); }
			set
			{
				m_excludeCertainExtensions = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public Point RelativeWindowPosition
		{
			get
			{
				var positionStrings = _ReadOrAdd("0.5,0.33").Split(',');

				return m_relativeWindowPos = new Point(Double.Parse(positionStrings[0]), Double.Parse(positionStrings[1]));
			}
			set
			{
				m_relativeWindowPos = value;
				_WriteSetting(m_relativeWindowPos.X.ToString() + "," + m_relativeWindowPos.Y.ToString());
				OnPropertyChanged();
			}
		}

		public int MaxResultsDisplayed
		{
			get { return m_maxResultsDisplayed = Int32.Parse(_ReadOrAdd("25")); }
			set
			{
				m_maxResultsDisplayed = value;
				_WriteSetting(m_maxResultsDisplayed.ToString());
				OnPropertyChanged();
			}
		}

		public int WindowX
		{
			get { return m_windowX = Int32.Parse(_ReadOrAdd("200")); }
			set
			{
				m_windowX = value;
				_WriteSetting(m_windowX.ToString());
				OnPropertyChanged();
			}
		}

		public int WindowY
		{
			get { return m_windowY = Int32.Parse(_ReadOrAdd("200")); }
			set
			{
				m_windowY = value;
				_WriteSetting(m_windowY.ToString());
				OnPropertyChanged();
			}
		}

		public int WindowWidth
		{
			get { return m_windowWidth = Int32.Parse(_ReadOrAdd("600")); }
			set
			{
				m_windowWidth = value;
				_WriteSetting(m_windowWidth.ToString());
				OnPropertyChanged();
			}
		}

		public bool FirstRun
		{
			get { return m_firstRun = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_firstRun = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool StartWithWindows
		{
			get { return m_startWithWindows = _ReadOrAdd("False").Equals("True"); }
			set
			{
				m_startWithWindows = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool StartMinimized
		{
			get { return m_startMinimized = _ReadOrAdd("False").Equals("True"); }
			set
			{
				m_startMinimized = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool MinimizeOnClose
		{
			get { return m_minimizeOnClose = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_minimizeOnClose = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool AllowRepositioning
		{
			get { return m_allowRepositioning = _ReadOrAdd("False").Equals("True"); }
			set
			{
				m_allowRepositioning = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool RememberPosition
		{
			get { return m_rememberPosition = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_rememberPosition = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool FollowMouse
		{
			get { return m_followMouse = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_followMouse = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool ClearOnSearch
		{
			get { return m_clearOnSearch = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_clearOnSearch = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool HideOnSearch
		{
			get { return m_hideOnSearch = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_hideOnSearch = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}
		
		public bool ClearOnHide
		{
			get { return m_clearOnHide = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_clearOnHide = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool MinimizeOnFocusLost
		{
			get { return m_minimizeOnFocusLost = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_minimizeOnFocusLost = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}
		
		public bool Topmost
		{
			get { return m_topmost = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_topmost = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool SortImmediatelyOnFileChange
		{
			get { return m_sortImmediatelyOnFileChange = _ReadOrAdd("True").Equals("True"); }
			set
			{
				m_sortImmediatelyOnFileChange = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}

		public bool RequireWildcard
		{
			get { return m_requireWildcard = _ReadOrAdd("False").Equals("True"); }
			set
			{
				m_requireWildcard = value;
				_WriteSetting(value);
				OnPropertyChanged();
			}
		}


		#endregion


		public FilerSettings()
		{
			m_lock = new object();
		}

		private void _WriteSetting( bool value, [CallerMemberName]string key = null)
		{
			if ( key == null ) return;

			AppSettingConfigurator.AddUpdateSetting(key, (value == true) ? "True" : "False");
		}

		private void _WriteSetting( string value, [CallerMemberName]string key = null )
		{
			if ( key == null ) return;

			AppSettingConfigurator.AddUpdateSetting(key, value);
		}

		private string _ReadOrAdd( string value, [CallerMemberName]string key = null  )
		{
			if ( key == null ) return "";

			if ( AppSettingConfigurator.SettingExists(key) == false )
			{
				AppSettingConfigurator.AddUpdateSetting(key, value);
				return value;
			}

			return AppSettingConfigurator.ReadSetting(key);
		}

		public void AppendDirectory( string formattedDirectoryString )
		{
			lock ( m_lock )
			{
				if ( formattedDirectoryString.EndsWith("|") == false ) formattedDirectoryString += "|";

				m_monitoredDirectories += formattedDirectoryString;

				//format the directory list into a string and save it in the settings
				AppSettingConfigurator.AddUpdateSetting("MonitoredDirectories", m_monitoredDirectories);
			}
		}

	}
}
