using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TagEditor
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow( List<string> startupArgs )
		{
			InitializeComponent();

			this.DataContext = new MainWindowViewModel(startupArgs);
		}

		private void FileList_SelectionChanged( object sender, SelectionChangedEventArgs e )
		{
			MainWindowViewModel vm = this.DataContext as MainWindowViewModel;
			if ( vm == null ) return;

			vm.SelectedFiles = FileList.SelectedItems.Cast<string>().ToList();
		}
	}
}
