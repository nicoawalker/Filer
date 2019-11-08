using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DatabaseAccess
{
	[Flags]
	public enum MatchFilters { Files = 1, Folders = 2, Tags = 4, Any = 7 }
}

namespace Filer
{ 
	public class SearchFilter
	{
		public DatabaseAccess.MatchFilters Type { get; set; }
		public string Label { get; set; }
		public ImageSource Icon { get; set; }

		public SearchFilter( DatabaseAccess.MatchFilters type, Bitmap icon)
		{
			Type = type;
			Label = type.ToString();

			MemoryStream stream = new MemoryStream();
			icon.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
			Icon = BitmapFrame.Create(stream);
		}
	}
}
