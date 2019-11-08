using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Common
{
	using Debug = System.Diagnostics.Debug;

	public enum FileType { Directory = 0, File = 1 }

	public class FilePropertyChangedEventArgs
	{
		public string PropertyName { get; set; }
		public object NewValue { get; set; }
		public object OldValue { get; set; }
	}

	public class FileInfo : NotifyPropertyBase
	{
		//all files that use the default icons can share the same icons, saving memory
		private static System.Drawing.Bitmap m_defaultFileIcon;
		private static System.Drawing.Bitmap m_defaultFolderIcon;

		public EventHandler<FilePropertyChangedEventArgs> OnChangedEvent;

		List<string> m_tags;
		System.Drawing.Bitmap m_fileIcon;
		string m_path;
		string m_fileName;
		double m_weight;
		int m_accessCount;
		FileType m_fileType;
		bool m_isFavorite;
		bool m_isHidden;


		#region Properties

		public int ID { get; set; }

		public List<string> Tags
		{
			get { return m_tags; }
			set { m_tags = value; OnPropertyChanged(); }
		}

		public System.Drawing.Bitmap FileIcon
		{
			get { return m_fileIcon; }
			set { m_fileIcon = value; OnPropertyChanged(); }
		}

		public string Path
		{
			get { return m_path; }
			set { m_path = value.ToLower(); OnPropertyChanged(); }
		}

		public string Name
		{
			get { return m_fileName; }
			set { m_fileName = value.ToLower(); OnPropertyChanged(); }
		}

		public double Weight
		{
			get { return m_weight; }
			set { m_weight = value; OnPropertyChanged(); }
		}

		public int AccessCount
		{
			get { return m_accessCount; }
			set
			{
				int oldValue = m_accessCount;
				m_accessCount = value;
				OnPropertyChanged();
				_OnChanged(m_accessCount, oldValue);
			}
		}

		public FileType Type
		{
			get { return m_fileType; }
			set { m_fileType = value; OnPropertyChanged(); }
		}

		public bool IsFavorite
		{
			get { return m_isFavorite; }
			set
			{
				bool oldValue = m_isFavorite;
				m_isFavorite = value;
				_OnChanged(m_isFavorite, oldValue);
				OnPropertyChanged();
			}
		}

		public bool IsHidden
		{
			get { return m_isHidden; }
			set
			{
				bool oldValue = m_isHidden;
				m_isHidden = value;
				_OnChanged(m_isHidden, oldValue);
				OnPropertyChanged();
			}
		}

		#endregion


		public FileInfo( RawFileData data, System.Drawing.Bitmap defaultFileIcon, System.Drawing.Bitmap defaultFolderIcon )
		{
			m_defaultFileIcon = defaultFileIcon;
			m_defaultFolderIcon = defaultFolderIcon;

			ID = (data.ID == "NULL") ? -1 : Int32.Parse(data.ID);
			m_path = data.Path.ToLower();
			m_fileName = data.FileName.ToLower();
			m_accessCount = data.AccessCount;
			m_fileType = (FileType)data.FileType;
			m_isFavorite = data.IsFavorite;
			m_isHidden = data.IsHidden;
			m_weight = 0.0;
			m_fileIcon = (m_fileType == FileType.File) ? m_defaultFileIcon : m_defaultFolderIcon;

			m_tags = null;
			if ( m_fileName.Length == 0 ) m_fileName = m_path;
		}

		public FileInfo( int id, string tags, string path, string fileName, int accessCount, FileType fileType, bool isFavorite, bool isHidden, System.Drawing.Bitmap defaultFileIcon, System.Drawing.Bitmap defaultFolderIcon )
		{
			m_defaultFileIcon = defaultFileIcon;
			m_defaultFolderIcon = defaultFolderIcon;

			ID = id;
			m_path = path.ToLower();
			m_fileName = fileName.ToLower();
			m_accessCount = accessCount;
			m_fileType = fileType;
			m_isFavorite = isFavorite;
			m_isHidden = isHidden;
			m_weight = 0.0;
			m_fileIcon = (m_fileType == FileType.File) ? m_defaultFileIcon : m_defaultFolderIcon;

			m_tags = tags.Split(';').ToList();
			if ( m_fileName.Length == 0 ) m_fileName = m_path;
		}

		public FileInfo(int id, List<string> tags, string path, string fileName, int accessCount, FileType fileType, bool isFavorite, bool isHidden, System.Drawing.Bitmap defaultFileIcon, System.Drawing.Bitmap defaultFolderIcon )
		{
			m_defaultFileIcon = defaultFileIcon;
			m_defaultFolderIcon = defaultFolderIcon;

			ID = id;
			m_tags = tags;
			m_path = path.ToLower();
			m_fileName = fileName.ToLower();
			m_accessCount = accessCount;
			m_fileType = fileType;
			m_isFavorite = isFavorite;
			m_isHidden = isHidden;
			m_weight = 0.0;
			m_fileIcon = (m_fileType == FileType.File) ? m_defaultFileIcon : m_defaultFolderIcon;

			if ( m_fileName.Length == 0 ) m_fileName = m_path;
		}

		public async void LoadFileIcon()
		{
			await Task.Run(() =>
			{
				//directories always use the default folder icon
				if ( m_fileType == FileType.Directory )
				{
					FileIcon = m_defaultFolderIcon;
					return;
				}

				if ( File.Exists(m_path) == false ) return;

				//first load the default file icon so there is at least something to display while the actual icon loads
				FileIcon = m_defaultFileIcon;

				try
				{
					Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(m_path);

					if ( icon == null ) return;

					FileIcon = icon.ToBitmap();
				}
				catch ( Exception e )
				{
					Debug.WriteLine($"Failed to extract file icon from file '{m_path}'. Reason: {e.Message}");
				}
			});
		}

		private void _OnChanged( object newValue, object oldValue, [CallerMemberName]string propertyName = null )
		{
			OnChangedEvent?.Invoke(this, new FilePropertyChangedEventArgs { NewValue = newValue, OldValue = oldValue, PropertyName = propertyName });
		}

		/// <summary>
		/// calculates the weight of the result against a given search term
		/// </summary>
		/// <param name="target"></param>
		public void CalculateWeight( string searchTerm )
		{
			m_weight = 0.0;

			if ( m_isHidden ) return;

			m_weight += m_fileName.Contains(searchTerm) ? ((double)searchTerm.Length / (double)m_fileName.Length) * 0.35 : 0.0;
			m_weight += m_isFavorite ? 0.35 : 0.0;
			m_weight += Math.Min(((double)m_accessCount / 15.0), 1.0) * 0.15;
			m_weight += m_fileName.StartsWith(searchTerm) ? 0.05 : 0.0;
			m_weight += m_fileType == FileType.File ? 0.05 : 0.0;
			m_weight += m_fileName.EndsWith(".exe") || m_fileName.EndsWith(".lnk") ? 0.1 : 0.0; //prefer executables/shortcuts over other file types

			if ( m_weight > 1.0 ) m_weight = 1.0;
		}
	}
}
