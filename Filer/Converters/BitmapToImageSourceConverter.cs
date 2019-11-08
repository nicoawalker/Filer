using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Filer
{
	class BitmapToImageSourceConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			System.Drawing.Bitmap bmp = value as System.Drawing.Bitmap;
			if ( bmp == null )
			{
				return Binding.DoNothing;
			}

			try
			{
				MemoryStream stream = new MemoryStream();
				bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
				return BitmapFrame.Create(stream);
			}
			catch ( Exception )
			{
				return Binding.DoNothing;
			}
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			throw new NotImplementedException();
		}
	}
}
