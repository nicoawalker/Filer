using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Common;

namespace Filer
{
	using Debug = System.Diagnostics.Debug;
	using DirectoryListing = Tuple<List<string>, List<string>>;

	public class ScanCompletedEventArgs
	{
		public string Path { get; set; }
		public int ItemCount { get; set; }
	}

	public class ScanProgressEventArgs
	{
		public List<string> FoundDirectories { get; set; }
		public List<string> FoundFiles { get; set; }
		public string Path { get; set; }
	}

	public class ScanStartedEventArgs
	{
		public string Path { get; set; }
	}

	public class DirectoryEventArgs
	{
		public string Path { get; set; }
	}

	public class StatusChangedEventArgs
	{
		public DirectoryMonitor.Status Status { get; set; }
	}

	public class MonitoredFileRenamedArgs
	{
		public string OldPath { get; set; }
		public string NewPath { get; set; }
	}

	public class MonitoredFileCreatedArgs
	{
		public string NewPath { get; set; }
	}

	public class MonitoredFileDeletedArgs
	{
		public string OldPath { get; set; }
	}

	public class DirectoryMonitor : NotifyPropertyBase
	{
		public event EventHandler<ScanCompletedEventArgs> ScanCompleted;
		public event EventHandler<ScanStartedEventArgs> ScanStarted;
		public event EventHandler<ScanProgressEventArgs> ScanProgress;
		public event EventHandler<DirectoryEventArgs> MonitoredDirectoryRemoved;
		public event EventHandler<DirectoryEventArgs> MonitoredDirectoryAdded;
		public event EventHandler<StatusChangedEventArgs> StatusChanged;
		public event EventHandler<MonitoredFileRenamedArgs> MonitoredFileRenamed;
		public event EventHandler<MonitoredFileCreatedArgs> MonitoredFileCreated;
		public event EventHandler<MonitoredFileDeletedArgs> MonitoredFileDeleted;

		public enum Status { Idle, ScanInProgress }

		public class Directory : NotifyPropertyBase
		{
			public class RecursiveModeToggledEventArgs
			{
				public bool Value { get; set; }

				public RecursiveModeToggledEventArgs( bool newValue )
				{
					Value = newValue;
				}
			}

			public enum DirectoryStatus { Active = 0, Scanning = 1, Pending = 2, Locked = 3, Empty = 4 }

			public event EventHandler<RecursiveModeToggledEventArgs> RecursiveModeToggled;

			public FileSystemWatcher Watcher { get; set; }

			string m_path;
			public string Path
			{
				get => m_path;
				set { m_path = value.ToLower(); OnPropertyChanged(); }
			}

			DirectoryStatus m_status;
			public DirectoryStatus Status
			{
				get => m_status;
				set
				{
					/*don't allow changing status to active unless the directory is accessible*/
					if ( m_status == DirectoryStatus.Locked && (value == DirectoryStatus.Active || value == DirectoryStatus.Empty) )
					{
						if ( _RegisterFileSystemWatcher() == false ) return;
					}
					else if ( value == DirectoryStatus.Scanning )
					{//reset the sub item count if a new scan is in progress
						SubItemCount = -1;
					}

					if ( value == DirectoryStatus.Active && m_subItemCount == 0 )
					{
						m_status = DirectoryStatus.Empty;
					}
					else
					{
						m_status = value;
					}

					OnPropertyChanged();
				}
			}

			int m_subItemCount; //how many files and folders are in this directory
			public int SubItemCount
			{
				get { return m_subItemCount; }
				set
				{
					m_subItemCount = value;
					OnPropertyChanged();

					if(m_subItemCount == 0)
					{
						Status = DirectoryStatus.Empty;
					}
				}
			}

			bool m_recursiveSearch;
			public bool RecursiveSearch
			{
				get { return m_recursiveSearch; }
				set
				{
					m_recursiveSearch = value;
					OnPropertyChanged();
					_OnRecursiveModeToggled(new RecursiveModeToggledEventArgs(m_recursiveSearch));
				}
			}

			public Directory( string path )
			{
				RecursiveSearch = true;
				SubItemCount = -1;
				m_path = path.ToLower();
				if ( m_path.Length < 1 ) throw new ArgumentException("Invalid Directory");

				Status = DirectoryStatus.Pending;

				_RegisterFileSystemWatcher();
			}

			private void _OnRecursiveModeToggled( RecursiveModeToggledEventArgs e )
			{
				RecursiveModeToggled?.Invoke(this, e);
			}

			private bool _RegisterFileSystemWatcher()
			{
				Watcher = new FileSystemWatcher();
				Watcher.Path = m_path;
				Watcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName;
				Watcher.Filter = ""; //watch all file types
				Watcher.InternalBufferSize = 65536; //maximum buffer size 64KB. this memory cannot be paged.

				try
				{
					Watcher.EnableRaisingEvents = true; //start watching

				}
				catch ( Exception e )
				{
					Status = DirectoryStatus.Locked;
					Debug.WriteLine($"FileWatcher threw exception trying to watch '{m_path}'\n\tException: '{e.Message}'");
					return false;
				}

				return true;
			}
		}

		private class Scan
		{
			public class ScanProgressEventArgs
			{
				/*list of directories and their contents*/
				public List<DirectoryListing> Results { get; set; }

				public string Path { get; set; }

				public ScanProgressEventArgs( string path, List<DirectoryListing> results )
				{
					Path = path;
					Results = results;
				}
			}

			public event EventHandler<ScanProgressEventArgs> ScanProgress;

			private Task m_task;
			public Task Task
			{
				get { return m_task; }
				set { m_task = value; }
			}

			//buffer used to batch ScanProgress events
			List<DirectoryListing> m_scanBuffer;
			int m_scanBufferSize;

			private UnmanagedInterface.FileSystem.CancellationToken m_cancellationToken; //token used to cancel the task early if required

			private Directory m_monitoredDirectory;

			public int ItemCount { get; set; }

			public readonly string path;
			public readonly string device;

			private readonly object m_scanLock;

			public Scan( string p, string d, Directory directory )
			{
				Debug.Assert(directory != null);

				m_scanBufferSize = 500;
				m_task = null;
				m_cancellationToken = new UnmanagedInterface.FileSystem.CancellationToken();
				m_monitoredDirectory = directory;
				path = p;
				device = d;
				m_scanLock = new object();
				ItemCount = 0;

				if ( m_monitoredDirectory.Status != Directory.DirectoryStatus.Scanning ) m_monitoredDirectory.Status = Directory.DirectoryStatus.Pending;
			}

			~Scan()
			{
				Stop();
			}

			private void _OnScanProgress( ScanProgressEventArgs e)
			{
				EventHandler<ScanProgressEventArgs> eventHandler = ScanProgress;
				if ( eventHandler != null )
				{
					eventHandler.Invoke(this, e);
				}
			}

			public void Start()
			{
				this.Task = System.Threading.Tasks.Task.Run(() =>
				{
					//load the file extension exclusion list
					FilerSettings settings = new FilerSettings();
					List<string> excludedExtensions = new List<string>();
					if (settings.ExcludeCertainExtensions == true) excludedExtensions = settings.ExcludedExtensions.Split(';').ToList();
					settings = null;

					//make sure all extensions are at least 1 character in length
					for ( int i = excludedExtensions.Count - 1; i >= 0; i-- )
					{
						if ( excludedExtensions[i].Length == 0 ) excludedExtensions.RemoveAt(i);
					}

					bool recursiveSearch = true;

					m_monitoredDirectory.SubItemCount = 0;
					m_monitoredDirectory.Status = Directory.DirectoryStatus.Scanning;
					recursiveSearch = m_monitoredDirectory.RecursiveSearch;

					string rootDirectory = path;

					Stack<string> directoriesToScan = new Stack<String>();
					directoriesToScan.Push(rootDirectory);

					int itemCount = 0;

					m_scanBuffer = new List<DirectoryListing>(m_scanBufferSize);

					/*recursively scan through all subfiles and folders of the root directory until everything is found, or the scan is cancelled*/
					do
					{
						var results = UnmanagedInterface.FileSystem.GetDirectoryContents(directoriesToScan.Pop());

						if ( results.Item1.Count == 0 && results.Item2.Count == 0 ) continue;

						if ( excludedExtensions.Count > 0 )
						{
							for ( int i = results.Item2.Count - 1; i >= 0; i-- )
							{
								foreach ( string extension in excludedExtensions )
								{
									if ( results.Item2[i].EndsWith("." + extension) == true )
									{
										results.Item2.RemoveAt(i);
										break;
									}
								}
							}
						}

						itemCount += results.Item1.Count + results.Item2.Count;

						foreach ( string directory in results.Item1 )
						{
							directoriesToScan.Push(directory);
						}

						m_monitoredDirectory.SubItemCount += results.Item1.Count + results.Item2.Count;

						/*when the buffer fills up send out a ScanProgress event*/
						m_scanBuffer.Add(results);
						if(m_scanBuffer.Count >= m_scanBufferSize)
						{
							List<DirectoryListing> resultsCopy = new List<DirectoryListing>();
							resultsCopy.AddRange(m_scanBuffer);
							_OnScanProgress(new ScanProgressEventArgs(rootDirectory, resultsCopy));

							m_scanBuffer.Clear();
						}

					} while ( directoriesToScan.Count > 0 && m_cancellationToken.cancel == false && recursiveSearch == true );

					ItemCount = itemCount;
					m_monitoredDirectory.SubItemCount = itemCount;
					m_monitoredDirectory.Status = Directory.DirectoryStatus.Active;

					//send out the last results
					if ( m_scanBuffer.Count > 0 )
					{
						List<DirectoryListing> resultsCopy = new List<DirectoryListing>();
						resultsCopy.AddRange(m_scanBuffer);
						_OnScanProgress(new ScanProgressEventArgs(rootDirectory, resultsCopy));

						m_scanBuffer.Clear();
					}
					
				});
			}

			public void Stop()
			{
				lock ( m_scanLock ) m_cancellationToken.cancel = true;
			}
		}

		ObservableCollection<Directory> m_monitoredDirectories;
		public ObservableCollection<Directory> MonitoredDirectories
		{
			get { return m_monitoredDirectories; }
			set { m_monitoredDirectories = value; OnPropertyChanged(); }
		}

		private Status m_currentStatus;
		public Status CurrentStatus
		{
			get { return m_currentStatus; }
			set
			{
				m_currentStatus = value;
				OnPropertyChanged();

				_OnStatusChanged(new StatusChangedEventArgs { Status = m_currentStatus });
			}
		}

		readonly object m_scanQueueLock;
		readonly object m_scanProgressLock;
		List<Scan> m_scanQueue;

		private int m_activeScanCount;

		private bool m_shutdown;

		private Task SchedulerTask;

		public DirectoryMonitor()
		{
			m_monitoredDirectories = new ObservableCollection<Directory>();
			m_scanQueue = new List<Scan>();
			m_scanQueueLock = new object();
			m_scanProgressLock = new object();
			m_shutdown = false;
			m_activeScanCount = 0;
			CurrentStatus = Status.Idle;

			SchedulerTask = Task.Run(() => _ScanScheduler());
		}

		~DirectoryMonitor()
		{
			//shutdown the scheduler thread
			m_shutdown = true;
			lock ( m_scanQueueLock ) Monitor.Pulse(m_scanQueueLock);
			SchedulerTask.Wait();
		}

		private async void _ScanScheduler()
		{
			List<Task> activeTasks = new List<Task>();
			Dictionary<string, Scan> activeScans = new Dictionary<string, Scan>();

			while ( !m_shutdown )
			{
				_StartAvailableScans(activeScans);
				if ( activeScans.Count > 0 )
				{
					foreach ( var pair in activeScans )
					{
						if ( activeTasks.Find(task => pair.Value.Task == task) != null ) continue;
						activeTasks.Add(pair.Value.Task);
					}

					/*continue for as long as there are scans running*/
					while ( activeTasks.Count > 0 && !m_shutdown )
					{

						/*get the first completed scan, or continue if none complete in a reasonable amount of time*/
						await Task.WhenAny(Task.WhenAny(activeTasks), Task.Delay(1000));
						var finishedTask = activeTasks.FirstOrDefault(task => task.Status == TaskStatus.RanToCompletion);

						if ( activeTasks.Contains(finishedTask) )
						{
							activeTasks.Remove(finishedTask);

							Scan completedScan = null;
							foreach ( var pair in activeScans )
							{
								if ( pair.Value.Task.Id != finishedTask.Id ) continue;

								completedScan = pair.Value;
							}

							Debug.Assert(completedScan != null);

							_FinishScan(completedScan);

							activeScans.Remove(completedScan.device);
						}

						/*start any available pending scans before awaiting the next scan*/
						_StartAvailableScans(activeScans);
						foreach ( var pair in activeScans )
						{
							if ( activeTasks.Find(task => pair.Value.Task == task) != null ) continue;
							activeTasks.Add(pair.Value.Task);
						}
					}
				}

				//if there are no tasks left in the queue, wait for one to be added
				lock ( m_scanQueueLock )
				{
					if ( m_scanQueue.Count > 0 ) continue;

					CurrentStatus = Status.Idle;

					Monitor.Wait(m_scanQueueLock);
				}
			}
		}

		/// <summary>
		/// tests all pending scans against a list of currently running scans. any scan whose device is not currently being scanned is started
		/// </summary>
		private void _StartAvailableScans( Dictionary<string, Scan> existingScans )
		{
			Debug.Assert(existingScans != null);

			List<Scan> availableScans = new List<Scan>();
			lock ( m_scanQueueLock )
			{
				/*grab all scans whose device is not currently being scanned*/
				for ( int i = m_scanQueue.Count - 1; i >= 0; i-- )
				{
					if ( existingScans.ContainsKey(m_scanQueue[i].device) ) continue;

					existingScans.Add(m_scanQueue[i].device, m_scanQueue[i]);
					availableScans.Add(m_scanQueue[i]);
				}
			}

			/*start all the available scans and add them as existing scans*/
			foreach( Scan availableScan in availableScans)
			{
				availableScan.Start();

				m_activeScanCount++;
				CurrentStatus = Status.ScanInProgress;

				_OnScanStarted(new ScanStartedEventArgs { Path = availableScan.path });
			}
		}

		private void _FinishScan( Scan scan )
		{
			Debug.Assert(scan != null);

			lock ( m_scanQueueLock )
			{
				m_scanQueue.Remove(scan);
			}

			m_activeScanCount--;

			if ( m_activeScanCount <= 0 ) m_activeScanCount = 0;

			//notify listeners that the scan has completed
			_OnScanCompleted(new ScanCompletedEventArgs { Path = scan.path, ItemCount = scan.ItemCount });
		}




		#region EventHandlers




		/// <summary>
		/// when a directory's recursive mode is changed, trigger a new scan with the new mode
		/// </summary>
		private void _OnRecursiveModeToggled( object sender, Directory.RecursiveModeToggledEventArgs e )
		{
			Directory directory = sender as Directory;
			if ( directory == null ) return;

			ScanDirectory(directory);
		}

		/// <summary>
		/// dispatcher for started scan events
		/// </summary>
		private void _OnScanStarted( ScanStartedEventArgs e)
		{
			EventHandler<ScanStartedEventArgs> eventHandler = ScanStarted;
			if ( eventHandler != null )
			{
				eventHandler.Invoke(this, e);
			}
		}

		/// <summary>
		/// dispatcher for completed scan events
		/// </summary>
		private void _OnScanCompleted(ScanCompletedEventArgs e)
		{
			EventHandler<ScanCompletedEventArgs> eventHandler = ScanCompleted;
			if(eventHandler != null)
			{
				eventHandler.Invoke(this, e);
			}
		}

		/// <summary>
		/// dispatcher for scan progress events
		/// </summary>
		private async void _OnScanProgress( object sender, Scan.ScanProgressEventArgs e )
		{
			await Task.Run(() =>
			{
				ScanProgressEventArgs translatedArgs = new ScanProgressEventArgs { Path = e.Path };
				translatedArgs.FoundDirectories = new List<string>();
				translatedArgs.FoundFiles = new List<string>();

				foreach(DirectoryListing listing in e.Results)
				{
					translatedArgs.FoundDirectories.AddRange(listing.Item1);
					translatedArgs.FoundFiles.AddRange(listing.Item2);
				}

				e.Results.Clear();

				lock ( m_scanProgressLock )
				{
					EventHandler<ScanProgressEventArgs> eventHandler = ScanProgress;
					if ( eventHandler != null )
					{
						eventHandler.Invoke(this, translatedArgs);
					}
				}
			});
		}

		/// <summary>
		/// dispatcher for status change events
		/// </summary>
		private void _OnStatusChanged( StatusChangedEventArgs e )
		{
			EventHandler<StatusChangedEventArgs> eventHandler = StatusChanged;
			if ( eventHandler != null )
			{
				eventHandler.Invoke(this, e);
			}
		}

		/// <summary>
		/// dispatcher for directory removal events
		/// </summary>
		private void _OnMonitoredDirectoryRemoved( DirectoryEventArgs e)
		{
			EventHandler<DirectoryEventArgs> eventHandler = MonitoredDirectoryRemoved;
			if(eventHandler != null)
			{
				eventHandler.Invoke(this, e);
			}
		}

		/// <summary>
		/// dispatcher for directory add events
		/// </summary>
		private void _OnMonitoredDirectoryAdded( DirectoryEventArgs e )
		{
			EventHandler<DirectoryEventArgs> eventHandler = MonitoredDirectoryAdded;
			if ( eventHandler != null )
			{
				eventHandler.Invoke(this, e);
			}
		}

		private void _OnWatchedFile_Renamed( object sender, RenamedEventArgs e )
		{
			Debug.WriteLine("Monitored file rename detected!");
			string newPath = e.FullPath.ToLower();
			string oldPath = e.OldFullPath.ToLower();

			/*if the file has been renamed or moved to another monitored directory, just update the file path and name*/
			foreach ( Directory dir in m_monitoredDirectories )
			{
				if ( newPath.Contains(dir.Path) )
				{
					MonitoredFileRenamed?.Invoke(this, new MonitoredFileRenamedArgs { NewPath = newPath, OldPath = oldPath });
					return;
				}
			}

			//file has moved to a directory that isn't being monitored, so delete it
			MonitoredFileDeleted?.Invoke(this, new MonitoredFileDeletedArgs { OldPath = oldPath });
		}

		private void _OnWatchedFile_Changed( object sender, FileSystemEventArgs e )
		{
			switch(e.ChangeType)
			{
				case WatcherChangeTypes.Deleted:
					{
						Console.WriteLine($"Watched file deleted: {e.FullPath}");
						MonitoredFileDeleted?.Invoke(this, new MonitoredFileDeletedArgs { OldPath = e.FullPath });
						//DatabaseAccess.Remove(new List<string> { e.FullPath });
						break;
					}
				case WatcherChangeTypes.Created:
					{
						Console.WriteLine($"File created in watched directory: {e.FullPath}");
						MonitoredFileCreated?.Invoke(this, new MonitoredFileCreatedArgs { NewPath = e.FullPath });
						//DatabaseAccess.InsertFiles(new List<string> { e.FullPath });
						break;
					}
				default:break;
			}
		}



		#endregion



		/// <summary>
		/// adds a request to scan a directory. if a scan on the same drive/device is already in progress
		/// the new scan will be queued until after all pending scans on that device have completed
		/// </summary>
		/// <param name="path">path of the directory to scan</param>
		public void ScanDirectory( string path )
		{
			path = path.ToLower().Replace("/", @"\");

			int deviceNameIndex = path.IndexOf(@"\");
			if ( deviceNameIndex == -1 ) return;

			Directory matchingDirectory = m_monitoredDirectories.FirstOrDefault(directory => directory.Path.Equals(path));

			Scan newScan = new Scan(path, path.Substring(0, deviceNameIndex), matchingDirectory);
			newScan.ScanProgress += _OnScanProgress;

			lock ( m_scanQueueLock )
			{
				m_scanQueue.Add(newScan);

				Monitor.Pulse(m_scanQueueLock);
			}
		}

		/// <summary>
		/// adds a request to scan a directory. if a scan on the same drive/device is already in progress
		/// the new scan will be queued until after all pending scans on that device have completed
		/// </summary>
		/// <param name="path">directory to scan</param>
		public void ScanDirectory( Directory directory )
		{
			if ( directory == null ) return;

			int deviceNameIndex = directory.Path.IndexOf(@"\");
			if ( deviceNameIndex == -1 ) return;

			Scan newScan = new Scan(directory.Path, directory.Path.Substring(0, deviceNameIndex), directory);
			newScan.ScanProgress += _OnScanProgress;

			lock ( m_scanQueueLock )
			{
				m_scanQueue.Add(newScan);

				Monitor.Pulse(m_scanQueueLock);
			}
		}

		public void ScanMonitoredDirectories()
		{
			Console.WriteLine("SCAN MON");
			foreach ( Directory dir in m_monitoredDirectories )
			{
				int deviceNameIndex = dir.Path.IndexOf(@"\");
				if ( deviceNameIndex == -1 ) return;

				Scan newScan = new Scan(dir.Path, dir.Path.Substring(0, deviceNameIndex), dir);
				newScan.ScanProgress += _OnScanProgress;

				lock ( m_scanQueueLock )
				{
					m_scanQueue.Add(newScan);
				}
			}

			lock ( m_scanQueueLock )
			{
				Monitor.Pulse(m_scanQueueLock);
			}
		}

		public Directory AddMonitoredDirectory( string path, bool recursive = true, bool scanDirectory = false )
		{
			if ( path.Length < 2 ) return null;

			path = path.ToLower();

			//test whether the new path is a sub-directory of an already monitored directory. add '\' to prevent false matches on similarly-named directories
			Directory existingDirectory = m_monitoredDirectories.FirstOrDefault(dir => (path + @"\").Contains(dir.Path + @"\") == true && dir.RecursiveSearch == true);
			if ( existingDirectory != null )
			{
				Console.WriteLine("Duplicate directory '" + path + "' ignored.");
				return existingDirectory;
 			}

			//test whether any monitored directories are subdirectories of the new directory, and if so remove them
			if ( recursive )
			{
				List<Directory> duplicateDirectories = new List<Directory>(m_monitoredDirectories.Where(directory => (directory.Path + @"\").Contains(path + @"\")));
				RemoveMonitoredDirectories(duplicateDirectories);
			}

			Directory newDirectory = new Directory(path);
			newDirectory.RecursiveSearch = recursive;
			newDirectory.RecursiveModeToggled += _OnRecursiveModeToggled;

			if(newDirectory.Watcher != null)
			{
				newDirectory.Watcher.Created += _OnWatchedFile_Changed;
				newDirectory.Watcher.Deleted += _OnWatchedFile_Changed;
				newDirectory.Watcher.Renamed += _OnWatchedFile_Renamed; //watch for file renames (including cut/paste and move operations)
			}

			MonitoredDirectories.Add(newDirectory);

			_OnMonitoredDirectoryAdded(new DirectoryEventArgs { Path = path });

			if ( scanDirectory )
			{
				ScanDirectory(path);
			}

			return newDirectory;
		}

		public void AddMonitoredDirectory( Directory directory )
		{
			if ( directory == null ) return;

			directory.Path = directory.Path.ToLower();

			//test whether the new directory is a sub-directory of an already monitored directory or already exists. add '\' to prevent false matches on similarly-named directories
			if ( m_monitoredDirectories.Any(dir => (directory.Path + @"\").Contains(dir.Path + @"\") == true && dir.RecursiveSearch == true) )
			{
				Console.WriteLine("Duplicate directory '" + directory.Path + "' ignored.");
				return;
			}

			//test whether any monitored directories are subdirectories of the new directory, and if so remove them
			if ( directory.RecursiveSearch == true )
			{
				List<Directory> duplicateDirectories = new List<Directory>(m_monitoredDirectories.Where(dir => (dir.Path + @"\").Contains(directory.Path + @"\")));
				RemoveMonitoredDirectories(duplicateDirectories);
			}

			directory.RecursiveModeToggled += _OnRecursiveModeToggled;

			if ( directory.Watcher != null )
			{
				directory.Watcher.Created += _OnWatchedFile_Changed;
				directory.Watcher.Deleted += _OnWatchedFile_Changed;
				directory.Watcher.Renamed += _OnWatchedFile_Renamed; //watch for file renames (including cut/paste and move operations)
			}

			MonitoredDirectories.Add(directory);

			_OnMonitoredDirectoryAdded(new DirectoryEventArgs { Path = directory.Path });

			if ( directory.Status != Directory.DirectoryStatus.Active && directory.Status != Directory.DirectoryStatus.Empty )
			{
				ScanDirectory(directory.Path);
			}
		}

		public void RemoveMonitoredDirectory( string path )
		{
			path = path.ToLower();

			MonitoredDirectories = new ObservableCollection<Directory>(m_monitoredDirectories.Where(directory => directory.Path.Equals(path) == false));

			/*if scans for the directory were queued, remove them*/
			lock ( m_scanQueueLock )
			{
				for(int i = m_scanQueue.Count - 1; i >= 0; i-- )
				{
					if ( m_scanQueue[i].path.Equals(path) == false )
					{
						continue;
					}
					
					m_scanQueue[i].Stop();
					m_scanQueue.RemoveAt(i);
				}

				Monitor.Pulse(m_scanQueueLock);
			}

			_OnMonitoredDirectoryRemoved(new DirectoryEventArgs { Path = path });
		}

		public void RemoveMonitoredDirectories( List<string> paths )
		{
			if ( paths == null || paths.Count == 0 ) return;

			lock ( m_scanQueueLock )
			{
				foreach ( string path in paths )
				{
					MonitoredDirectories = new ObservableCollection<Directory>(m_monitoredDirectories.Where(directory => directory.Path.Equals(path) == false));
					/*if scans for the directory were queued, remove them*/
					for ( int i = m_scanQueue.Count - 1; i >= 0; i-- )
					{
						if ( m_scanQueue[i].path.Equals(path) == false ) continue;

						m_scanQueue[i].Stop();
						m_scanQueue.RemoveAt(i);
					}

					_OnMonitoredDirectoryRemoved(new DirectoryEventArgs { Path = path });
				}

				Monitor.Pulse(m_scanQueueLock);
			}
		}

		public void RemoveMonitoredDirectories( List<Directory> directories )
		{
			if ( directories == null || directories.Count == 0 ) return;

			lock ( m_scanQueueLock )
			{
				foreach ( Directory dir in directories )
				{
					MonitoredDirectories = new ObservableCollection<Directory>(m_monitoredDirectories.Where(directory => directory.Path.Equals(dir.Path) == false));
					/*if scans for the directory were queued, remove them*/
					for ( int i = m_scanQueue.Count - 1; i >= 0; i-- )
					{
						if ( m_scanQueue[i].path.Equals(dir.Path) == false ) continue;

						m_scanQueue[i].Stop();
						m_scanQueue.RemoveAt(i);
					}

					_OnMonitoredDirectoryRemoved(new DirectoryEventArgs { Path = dir.Path });
				}

				Monitor.Pulse(m_scanQueueLock);
			}
		}

		/// <summary>
		/// Converts a string formatted to be saved in the settings to a list of directories
		/// </summary>
		public static List<DirectoryMonitor.Directory> GetDirectoryListFromFormattedString( string directoriesString )
		{
			List<string> splitString = directoriesString.Split('|').ToList();
			List<DirectoryMonitor.Directory> extractedDirectories = new List<DirectoryMonitor.Directory>();

			for ( int i = 0; i < splitString.Count && i + 3 < splitString.Count; i += 4 )
			{
				DirectoryMonitor.Directory extractedDirectory = new DirectoryMonitor.Directory(splitString[i]);
				extractedDirectory.RecursiveSearch = splitString[i + 1].Equals("True") ? true : false;
				extractedDirectory.SubItemCount = Int32.Parse(splitString[i + 2]);
				extractedDirectory.Status = splitString[i + 3].Equals("True") ? Directory.DirectoryStatus.Scanning : Directory.DirectoryStatus.Active;
				extractedDirectories.Add(extractedDirectory);
			}

			return extractedDirectories;
		}

		/// <summary>
		/// converts all current directories into a formatted string suitable to be saved in the settings
		/// </summary>
		public string GetFormattedStringFromDirectoryList()
		{
			string formattedString = "";
			foreach ( DirectoryMonitor.Directory dir in m_monitoredDirectories )
			{
				formattedString += dir.Path + "|" + dir.RecursiveSearch.ToString() + "|" + dir.SubItemCount.ToString() + "|" + ((dir.Status != Directory.DirectoryStatus.Active) ? "True" : "False") + "|";
			}

			return formattedString;
		}

		/// <summary>
		/// converts a list of directories into a formatted string suitable to be saved in the settings
		/// </summary>
		public static string GetFormattedStringFromDirectoryList( List<DirectoryMonitor.Directory> directories )
		{
			string formattedString = "";
			foreach ( DirectoryMonitor.Directory dir in directories )
			{
				formattedString += dir.Path + "|" + dir.RecursiveSearch.ToString() + "|" + dir.SubItemCount.ToString() + "|" + ((dir.Status != Directory.DirectoryStatus.Active) ? "True" : "False") + "|";
			}

			return formattedString;
		}

	}
}
