using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Common
{

	using DirectoryListing = Tuple<List<string>, List<string>>;

	public class FileSystem
	{

		/// <summary>
		/// recursively searches a directory for all subdirectories and files
		/// </summary>
		/// <param name="directory">path of the directory to begin searching in</param>
		/// <returns>a tuple with [0] all directories and [1] all files</returns>
		public static async Task<DirectoryListing> RecurseDirectoryTreeAsync( string directory )
		{
			return await Task.Run(() =>
			{
				Stack<string> scanQueue = new Stack<string>();

				List<string> directoryList = new List<string>();
				List<string> fileList = new List<string>();

				if ( Directory.Exists(directory) )
				{
					scanQueue.Push(directory);
					directoryList.Add(directory.ToLower());
				}

				while ( scanQueue.Count > 0 )
				{
					string currentDirectory = scanQueue.Pop();

					try
					{
						string[] dirs = Directory.GetDirectories(currentDirectory);

						foreach ( string dir in dirs )
						{
							scanQueue.Push(dir);
							directoryList.Add(dir.ToLower());
						}
					}
					catch ( UnauthorizedAccessException e )
					{
						Console.WriteLine("Can't access directory '" + currentDirectory + "': " + e.Message);
					}
					catch ( DirectoryNotFoundException e )
					{
						Console.WriteLine("Can't access directory '" + currentDirectory + "': " + e.Message);
					}

					try
					{
						string[] files = Directory.GetFiles(currentDirectory);

						foreach ( string file in files )
						{
							fileList.Add(file.ToLower());
						}
					}
					catch ( UnauthorizedAccessException e )
					{
						Console.WriteLine("Can't access file '" + currentDirectory + "': " + e.Message);
					}
					catch ( DirectoryNotFoundException e )
					{
						Console.WriteLine("Can't access file '" + currentDirectory + "': " + e.Message);
					}
				}

				return new DirectoryListing(directoryList, fileList);
			});
		}

		/// <summary>
		/// recursively searches a directory for subdirectories
		/// </summary>
		/// <param name="directory">path of the directory to begin searching in</param>
		/// <returns>a list containing all subdirectories under a directory</returns>
		public static async Task<List<string>> RecurseDirectoriesAsync( string directory )
		{
			return await Task.Run( () =>
			{
				Stack<string> scanQueue = new Stack<string>();
				List<string> directoryList = new List<string>();

				if ( Directory.Exists(directory) == false ) scanQueue.Push(directory);

				while ( scanQueue.Count > 0 )
				{
					string currentDirectory = scanQueue.Pop();

					try
					{
						string[] dirs = Directory.GetDirectories(currentDirectory);

						foreach ( string dir in dirs )
						{
							scanQueue.Push(dir);
							directoryList.Add(dir);
						}
					}
					catch ( UnauthorizedAccessException e )
					{
						return null;
					}
					catch ( DirectoryNotFoundException e )
					{
						return null;
					}
				}

				return directoryList;
			});
		}

		/// <summary>
		/// recursively searches for all files under a directory
		/// </summary>
		/// <param name="directory">path of the directory to begin searching in</param>
		/// <returns>a list containing the paths of all files under the directory</returns>
		public static List<string> RecurseFiles( string directory )
		{
			return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).ToList();
		}

		/// <summary>
		/// creates a shortcut (.lnk) in the user's Startup folder so that Unblind will start with windows
		/// </summary>
		public static void CreateStartupShortcut( string shortcutName, string executablePath, string relativeIconLocation )
		{
			/*check if a shortcut has been created in the user's Startup folder, and create one if it hasn't*/
			string shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\" + shortcutName;
			if ( File.Exists(shortcutPath) == false )
			{
				//code from https://stackoverflow.com/questions/234231/creating-application-shortcut-in-a-directory

				Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); //Windows Script Host Shell Object
				dynamic shell = Activator.CreateInstance(t);
				try
				{
					var lnk = shell.CreateShortcut(shortcutPath);

					lnk.TargetPath = executablePath;
					//lnk.TargetPath = System.AppDomain.CurrentDomain.BaseDirectory + @"\Unblind.exe";
					lnk.IconLocation = AppDomain.CurrentDomain.BaseDirectory + relativeIconLocation;
					//lnk.IconLocation = AppDomain.CurrentDomain.BaseDirectory + @"Resources\unblind.ico";
					lnk.Save();
				}
				catch ( Exception e )
				{
					Marshal.FinalReleaseComObject(shell);
					throw e;
				}
			}
		}

		/// <summary>
		/// removes the shortcut to Unblind in the user's Startup folder so that Unblind will no longer start with windows
		/// </summary>
		public static void DeleteStartupShortcut( string shortcutName )
		{
			if ( File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\" + shortcutName) )
			{
				File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\" + shortcutName);
			}
		}

	}
}
