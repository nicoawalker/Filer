using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Filer
{
	public class FileTypeToColorConverter : MarkupExtension, IValueConverter
	{
		public SolidColorBrush FileBrush { get; set; }
		public SolidColorBrush DirectoryBrush { get; set; }

		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			System.Diagnostics.Debug.Assert(FileBrush != null);
			System.Diagnostics.Debug.Assert(DirectoryBrush != null);

			if(value is Common.FileType)
			{
				switch((Common.FileType)value)
				{
					case Common.FileType.File:
						{
							return FileBrush;
						}
					case Common.FileType.Directory:
						{
							return DirectoryBrush;
						}
					default: return Binding.DoNothing;
				}
			}

			return Binding.DoNothing;
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			return Binding.DoNothing;
		}

		public override object ProvideValue( IServiceProvider serviceProvider )
		{
			return this;
		}
	}
}
