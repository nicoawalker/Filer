using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Filer.Controls
{
	/// <summary>
	/// Interaction logic for SearchResultControl.xaml
	/// </summary>
	public partial class SearchResultControl : UserControl
	{

		public Common.FileInfo SearchResultContent
		{
			get { return (Common.FileInfo)GetValue(SearchResultContentProperty); }
			set { SetValue(SearchResultContentProperty, value); }
		}

		public static readonly DependencyProperty SearchResultContentProperty =
			DependencyProperty.Register("SearchResultContent", typeof(Common.FileInfo), typeof(SearchResultControl));

		public SearchResultControl()
		{
			InitializeComponent();

			LayoutRoot.DataContext = this;
		}

		private void UserControl_MouseEnter( object sender, MouseEventArgs e )
		{
			AccessCountIndicator.Visibility = Visibility.Visible;
		}

		private void UserControl_MouseLeave( object sender, MouseEventArgs e )
		{
			AccessCountIndicator.Visibility = Visibility.Collapsed;
		}

		private void FavoriteMenuItem_Click( object sender, RoutedEventArgs e )
		{
			SearchResultContent.IsFavorite = !SearchResultContent.IsFavorite;

			/*when changing favorite status the file may move in the listview so
			 * add a flash of green or red to make it easier to see*/
			ColorAnimation colorChangeAnimation = new ColorAnimation();
			colorChangeAnimation.FillBehavior = FillBehavior.Stop;
			colorChangeAnimation.Duration = new Duration(new TimeSpan(0, 0, 1));
			colorChangeAnimation.To = Colors.Transparent;

			if ( SearchResultContent.IsFavorite )
			{
				SearchResultContent.IsHidden = false;
				colorChangeAnimation.From = Color.FromArgb(255, 145, 204, 156); //green
			}
			else
			{
				colorChangeAnimation.From = Color.FromArgb(255, 235, 144, 144); //red
			}

			this.ControlBorder.Background = new SolidColorBrush(Colors.Transparent);
			this.ControlBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorChangeAnimation);
		}

		private void HideMenuItem_Click( object sender, RoutedEventArgs e )
		{
			SearchResultContent.IsHidden = !SearchResultContent.IsHidden;

			/*when changing hidden status the file may move in the listview so
			 * add a flash of green or red to make it easier to see*/
			ColorAnimation colorChangeAnimation = new ColorAnimation();
			colorChangeAnimation.FillBehavior = FillBehavior.Stop;
			colorChangeAnimation.Duration = new Duration(new TimeSpan(0, 0, 1));
			colorChangeAnimation.To = Colors.Transparent;

			if ( SearchResultContent.IsHidden )
			{
				SearchResultContent.IsFavorite = false;
				colorChangeAnimation.From = Color.FromArgb(255, 235, 144, 144); //red
			}
			else
			{
				colorChangeAnimation.From = Color.FromArgb(255, 145, 204, 156); //green
			}

			this.ControlBorder.Background = new SolidColorBrush(Colors.Transparent);
			this.ControlBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorChangeAnimation);
		}
	}
}
