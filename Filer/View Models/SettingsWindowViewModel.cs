using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Common;

namespace Filer
{

	using Debug = System.Diagnostics.Debug;

	public class SettingsWindowViewModel : ViewModelBase
	{
		//name of the shortcut that will be placed in the user's Startup folder (if StartWithWindows is enabled)
		private const string m_shortcutName = "filer.lnk";

		public ICommand AddDirectoryCommand { get; set; }
		public ICommand RemoveDirectoryCommand { get; set; }
		public ICommand ScanDirectoriesCommand { get; set; }
		public ICommand ToggleStartWithWindowsCommand { get; set; }

		DirectoryMonitor m_directoryMonitor;

		public FilerSettings Settings { get; set; }

		private List<int> m_restrictedResultCountList;

		private string m_cacheSizeLabel;

		private bool m_recursiveChanged;
		private bool m_allowRepositioning;



		#region Properties



		public List<int> RestrictedResultCountList
		{
			get { return m_restrictedResultCountList; }
			set { m_restrictedResultCountList = value; OnPropertyChanged(); }
		}

		public bool AllowRepositioning
		{
			get { return m_allowRepositioning; }
			set
			{
				m_allowRepositioning = value;
				OnPropertyChanged();
				Settings.AllowRepositioning = value;
			}
		}

		public DirectoryMonitor DirectoryMonitor
		{
			get { return m_directoryMonitor; }
			set { m_directoryMonitor = value; OnPropertyChanged(); }
		}

		public string CacheSizeLabel
		{
			get { return m_cacheSizeLabel; }
			set { m_cacheSizeLabel = value; OnPropertyChanged(); }
		}



		#endregion




		public SettingsWindowViewModel( SettingsWindowEventArgs args )
		{
			Settings = new FilerSettings();
			AllowRepositioning = Settings.AllowRepositioning;
			DirectoryMonitor = args.DirectoryMonitor;
			RestrictedResultCountList = new List<int> { 10, 25, 50, 100, 200 };

			m_directoryMonitor.ScanCompleted += _DirectoryMonitor_ScanCompleted;

			AddDirectoryCommand = new RelayCommand(_AddDirectory);
			RemoveDirectoryCommand = new RelayCommand(_RemoveDirectory);
			ScanDirectoriesCommand = new RelayCommand(_ScanDirectories);
			ToggleStartWithWindowsCommand = new RelayCommand(_ToggleStartWithWindows);

			m_recursiveChanged = false;

			m_cacheSizeLabel = "";

			_LoadCacheSize();
		}

		private void _DirectoryMonitor_ScanCompleted( object sender, ScanCompletedEventArgs e )
		{
			_LoadCacheSize();
		}

		~SettingsWindowViewModel()
		{
			//if any directory had it's recursive mode changed, update the directory listing in the settings
			if ( m_recursiveChanged )
			{
				Settings.MonitoredDirectories = m_directoryMonitor.GetFormattedStringFromDirectoryList();
			}
		}

		private void _LoadCacheSize()
		{
			CacheSizeLabel = "Cache Size: ...";

			if ( File.Exists(System.AppDomain.CurrentDomain.BaseDirectory + @"\cache.db") )
			{
				long size = new System.IO.FileInfo(System.AppDomain.CurrentDomain.BaseDirectory + @"\cache.db").Length;

				CacheSizeLabel = "Cache Size: " + ((size > 1000000) ? (size / 1000000).ToString() + "MB" : (size / 1000).ToString() + "KB");
			}
		}

		private void _ToggleRecursive( object obj )
		{
			//set flag indicating that one or more directory's recursive status changed
			m_recursiveChanged = true;
		}

		private void _RemoveDirectory( object obj )
		{
			if ( m_directoryMonitor == null ) return;

			if(obj is IList<object>)
			{
				var selectedItems = obj as IList<object>;
				if ( selectedItems == null ) return;

				for(int i = selectedItems.Count - 1; i >= 0; i--)
				{
					if(selectedItems[i] is Filer.DirectoryMonitor.Directory)
					{
						Filer.DirectoryMonitor.Directory directory = selectedItems[i] as Filer.DirectoryMonitor.Directory;
						if(directory == null) return;

						m_directoryMonitor.RemoveMonitoredDirectory(directory.Path);
					}
				}
			}
		}

		private void _ScanDirectories( object obj )
		{
			if ( m_directoryMonitor == null ) return;

			if ( obj is IList<object> )
			{
				var selectedItems = obj as IList<object>;
				if ( selectedItems == null ) return;

				for ( int i = selectedItems.Count - 1; i >= 0; i-- )
				{
					if ( selectedItems[i] is Filer.DirectoryMonitor.Directory )
					{
						Filer.DirectoryMonitor.Directory directory = selectedItems[i] as Filer.DirectoryMonitor.Directory;
						if ( directory == null ) return;

						m_directoryMonitor.ScanDirectory(directory);
					}
				}
			}
		}

		private void _AddDirectory( object obj )
		{
			if ( m_directoryMonitor == null ) return;

			CommonOpenFileDialog directoryPicker = new CommonOpenFileDialog();
			directoryPicker.IsFolderPicker = true;
			directoryPicker.ShowHiddenItems = true;
			directoryPicker.ShowPlacesList = true;
			directoryPicker.Title = "Choose one or more directories";
			directoryPicker.Multiselect = true;

			CommonFileDialogResult result = directoryPicker.ShowDialog();
			if ( result != CommonFileDialogResult.Ok ) return;

			List<string> chosenDirectories = directoryPicker.FileNames.ToList();
			foreach(string dir in chosenDirectories)
			{
				m_directoryMonitor.AddMonitoredDirectory(dir, true, true);
			}

			Settings.MonitoredDirectories = m_directoryMonitor.GetFormattedStringFromDirectoryList();
		}

		private void _ToggleStartWithWindows( object obj )
		{
			try
			{
				if(Settings.StartWithWindows == false)
				{
					FileSystem.DeleteStartupShortcut(m_shortcutName);

				}else
				{
					FileSystem.CreateStartupShortcut(m_shortcutName, System.AppDomain.CurrentDomain.BaseDirectory + @"\filer.exe", @"Resources\Images\Light\filer.ico");
				}

			}catch(Exception crap)
			{
				Settings.SessionLog += crap.Message + "\n";
				MessageBox.Show("Could not create a shortcut in your startup folder. Please try again.\n\nError: " + crap.Message, "Oh no!", MessageBoxButton.OK, MessageBoxImage.Error);
				Settings.StartWithWindows = false;
			}
		}
	}
}
