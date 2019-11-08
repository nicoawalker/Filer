using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Filer
{
	/// <summary>
	/// Converts any integer into a string, with a comma between the thousands, millions, etc.
	/// </summary>
	public class IntToFormattedStringConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			return String.Format("{0:n0}", (int)value);
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			throw new NotImplementedException();
		}
	}
}
