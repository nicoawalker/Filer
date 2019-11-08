using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Common;

namespace Filer
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private const int GlobalShowHideHotkeyId = 9000;

		private const int VK_SPACE = 0x20;
		private const int MOD_ALT = 0x0001;
		private const int MOD_CONTROL = 0x0002;
		private const int MOD_SHIFT = 0x0004;
		private const int MOD_WIN = 0x0008;

		/*methods used to register global hotkeys for the window*/
		[DllImport("User32.dll")]
		private static extern bool RegisterHotKey( [In] IntPtr hWnd, [In] int id, [In] uint fsModifiers, [In] uint vk );
		[DllImport("User32.dll")]
		private static extern bool UnregisterHotKey( [In] IntPtr hWnd, [In] int id );

	 	FilerSettings m_settings;

		private HwndSource m_sourceHandle;

		private System.Windows.Forms.NotifyIcon m_trayIcon;

		private bool m_close;

		public MainWindow()
		{
			InitializeComponent();

			//override the default tooltip timeout to make tooltips stay open until the user moves the mouse off the control
			ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(Int32.MaxValue));

			m_close = false;

			m_settings = new FilerSettings();

			try
			{
				MainWindowViewModel vm = new MainWindowViewModel();
				vm.VisibilityChangeRequest += _OnVisibilityChangeRequested;
				vm.DisplaySettingsWindowRequest += _OnDisplaySettingsWindowRequested;
				this.DataContext = vm;
			}
			catch ( Exception e )
			{
				Console.WriteLine(e.Message);
				m_close = true;
				this.Close();
			}

			_CreateTrayIcon();

			((INotifyCollectionChanged)SearchResultListView.Items).CollectionChanged += SearchResultListView_CollectionChanged;
		}

		private void _OnDisplaySettingsWindowRequested( object sender, SettingsWindowEventArgs e )
		{
			Views.SettingsWindowView settingsWindow = new Views.SettingsWindowView(e);
			settingsWindow.ShowActivated = true;
			settingsWindow.ShowInTaskbar = false;
			settingsWindow.Topmost = true;
			settingsWindow.Owner = this;
			settingsWindow.ShowDialog();
		}

		private void _OnVisibilityChangeRequested( object sender, VisibilityEventArgs e )
		{
			if ( e.IsVisible )
			{
				_ShowSelf();
			}
			else
			{
				_MinimizeToTray();
			}
		}

		protected override void OnSourceInitialized( EventArgs e )
		{
			this.ShowInTaskbar = false;

			var helper = new WindowInteropHelper(this);
			m_sourceHandle = HwndSource.FromHwnd(helper.Handle);
			m_sourceHandle.AddHook(WndProc);

			_RegisterGlobalHotkey();

			Rect screenArea = UnmanagedInterface.Displays.GetMonitorRectFromPoint(UnmanagedInterface.Displays.CursorPosition());

			//transform "real coordinates" into "wpf coordinates" to account for dpi, etc.
			var t = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
			Point transformedPosition = t.Transform(new Point(screenArea.Width, screenArea.Height));

			//set initial window position based on user settings
			if ( m_settings.RememberPosition == true && m_settings.FirstRun == false )
			{
				this.Top = m_settings.WindowY;
				this.Left = m_settings.WindowX;
			}
			else
			{
				transformedPosition.X = (screenArea.Width * 0.5) + screenArea.X;
				transformedPosition.Y = (screenArea.Height * 0.33) + screenArea.Y;

				//transform "real coordinates" into "wpf coordinates" to account for dpi
				transformedPosition = _DesktopCoordsToWPF(transformedPosition);

				if ( m_settings.FirstRun )
				{//on first run relative position will 0.5, 0.5 so we take into account the width of the window to center it properly
					transformedPosition.X -= this.ActualWidth / 2;
				}

				this.Top = transformedPosition.Y;
				this.Left = transformedPosition.X;

				m_settings.WindowX = (int)this.Left;
				m_settings.WindowY = (int)this.Top;
			}

			//update the relative position based on the current position
			_CalculateWindowRelativePosition();

			if ( m_settings.StartMinimized == true ) _MinimizeToTray();
		}

		/*moves the window to the correct position, based on user settings*/
		private void _SetWindowPosition()
		{
			Point transformedPosition = new Point();
			if ( m_settings.FollowMouse == true )
			{
				/* want the window to appear on whatever monitor the cursor is currently on
				 * also want the window to be in the same relative position on all monitors*/

				//use the saved relative window position to calculate the correct position on the cursor screen
				Rect screenArea = UnmanagedInterface.Displays.GetMonitorRectFromPoint(UnmanagedInterface.Displays.CursorPosition());
				
				transformedPosition.X = (screenArea.Width * m_settings.RelativeWindowPosition.X) + screenArea.X;
				transformedPosition.Y = (screenArea.Height * m_settings.RelativeWindowPosition.Y) + screenArea.Y;

				//transform "real coordinates" into "wpf coordinates"
				transformedPosition = _DesktopCoordsToWPF(transformedPosition);
			}
			else
			{
				transformedPosition.Y = m_settings.WindowY;
				transformedPosition.X = m_settings.WindowX;
			}

			this.Top = transformedPosition.Y;
			this.Left = transformedPosition.X;

			m_settings.WindowX = (int)this.Left;
			m_settings.WindowY = (int)this.Top;
		}

		private void _CalculateWindowRelativePosition()
		{
			//calculate the relative distance from the top left corner of the display that the window is currently on
			Point desktopWindowCoords = _WPFToDesktopCoords(new Point(this.Left, this.Top));
			Rect screenArea = UnmanagedInterface.Displays.GetMonitorRectFromPoint(desktopWindowCoords);
			Point relativePosition = new Point( (desktopWindowCoords.X - screenArea.X) / screenArea.Width, (desktopWindowCoords.Y - screenArea.Y) / screenArea.Height);

			m_settings.RelativeWindowPosition = relativePosition;
		}

		/// <summary>
		/// takes a "wpf coordinate" (scaled point accounting for dpi, etc) and gets the "real" desktop coordinate
		/// </summary>
		private Point _WPFToDesktopCoords( Point wpfCoord )
		{
			var t = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
			return t.Transform(wpfCoord);
		}

		/// <summary>
		/// takes a "real" desktop coordinate and gets the corresponding "wpf coordinate" (scaled point accounting for dpi, etc) 
		/// </summary>
		private Point _DesktopCoordsToWPF( Point desktopCoord )
		{
			var t = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
			return t.Transform(desktopCoord);
		}

		private void _RegisterGlobalHotkey()
		{
			var helper = new WindowInteropHelper(this);
			if (!RegisterHotKey(helper.Handle, GlobalShowHideHotkeyId, MOD_ALT, VK_SPACE))
			{
				Console.WriteLine("Error registering global hotkey!");
			}
		}

		private void _UnregisterGlobalHotkey( int hotkeyId )
		{
			var helper = new WindowInteropHelper(this);
			if ( !UnregisterHotKey(helper.Handle, hotkeyId) )
			{
				Console.WriteLine("Error unregistering global hotkey!");
			}
		}

		private void _OnGlobalHotkeyPressed( int hotkeyId )
		{
			switch(hotkeyId)
			{
				case GlobalShowHideHotkeyId:
					{
						if ( this.Visibility != Visibility.Visible)
						{
							_ShowSelf();
						}
						else
						{
							_MinimizeToTray();
						}
						break;
					}
				default:break;
			}
		}

		private void _CreateTrayIcon()
		{
			if ( m_trayIcon != null ) return;

			//create new NotifyIcon that will function as the tray icon
			m_trayIcon = new System.Windows.Forms.NotifyIcon { Icon = Properties.Resources.filer_logo, Visible = true, Text = "Filer" };
			m_trayIcon.Click += ( object sender, EventArgs args ) =>
			{
				_ShowSelf();
			};

			//create context menu for the tray icon
			System.Windows.Forms.ContextMenu cMenu = new System.Windows.Forms.ContextMenu();
			System.Windows.Forms.MenuItem cMenuItem = new System.Windows.Forms.MenuItem();

			//create menu item to exit the application
			cMenuItem.Index = 0;
			cMenuItem.Text = "E&xit";
			cMenuItem.Click += new EventHandler(( object Sender, EventArgs e ) =>
			{
				m_close = true;
				this.Close();
			});

			cMenu.MenuItems.Add(cMenuItem);

			m_trayIcon.ContextMenu = cMenu;
		}

		private void _RemoveTrayIcon()
		{
			if ( m_trayIcon != null )
			{
				m_trayIcon.Visible = false;
				m_trayIcon.Dispose();
				m_trayIcon = null;
			}
		}

		private void _MinimizeToTray()
		{
			this.Hide();

			if ( m_settings.ClearOnHide )
			{
				SearchBox.Text = "";
			}
		}

		private void _ShowSelf()
		{
			_SetWindowPosition();

			this.Show();
			this.Activate();

			this.Topmost = m_settings.Topmost;

			SearchBox.Focus();
		}

		protected override void OnClosing( CancelEventArgs e )
		{
			//don't allow closing unless it's from the tray icon
			if ( m_close == false && m_settings.MinimizeOnClose == true )
			{
				_MinimizeToTray();
				e.Cancel = true;
				return;
			}

			_UnregisterGlobalHotkey(GlobalShowHideHotkeyId);

			_RemoveTrayIcon();

			base.OnClosing(e);
		}

		private void CloseButton_Click( object sender, RoutedEventArgs e )
		{
			this.Close();
		}

		private void Window_PreviewKeyDown( object sender, KeyEventArgs e )
		{
			if ( this.Visibility != Visibility.Visible )
			{
				e.Handled = true;
				return;
			}

			if (Keyboard.Modifiers == ModifierKeys.Alt )
			{
				e.Handled = true;
				return;
			}

			switch ( e.Key )
			{
				case Key.LeftAlt:
				case Key.RightAlt:
					{
						e.Handled = true;
						break;
					}
				case Key.Tab: /*open the search filter list if it's closed, otherwise cycle through the options*/
					{
						if ( SearchFilterList.Visibility == Visibility.Collapsed )
						{
							SearchFilterList.Visibility = Visibility.Visible;
						}
						else
						{
							if ( SearchFilterList.SelectedIndex == SearchFilterList.Items.Count - 1 )
							{
								SearchFilterList.SelectedIndex = 0;
							}
							else
							{
								SearchFilterList.SelectedIndex++;
							}
						}

						e.Handled = true;

						break;
					}
				case Key.Escape:
					{
						SearchBox.Text = "";

						SearchFilterList.Visibility = Visibility.Collapsed;

						break;
					}
				case Key.Up:
					{
						if ( SearchResultListView.Items.Count > 0 )
						{
							if ( SearchResultListView.SelectedIndex > 0 )
							{
								SearchResultListView.SelectedIndex = SearchResultListView.SelectedIndex - 1;
							}

							SearchResultListView.ScrollIntoView(SearchResultListView.SelectedItem);
						}

						e.Handled = true;
						break;
					}
				case Key.Down:
					{
						if ( SearchResultListView.Items.Count > 0 )
						{
							if ( SearchResultListView.SelectedIndex < SearchResultListView.Items.Count - 1 )
							{
								SearchResultListView.SelectedIndex = SearchResultListView.SelectedIndex + 1;
							}

							SearchResultListView.ScrollIntoView(SearchResultListView.SelectedItem);
						}
						e.Handled = true;
						break;
					}
				default:
					{
						SearchFilterList.Visibility = Visibility.Collapsed;
						break;
					}
			}

			if ( SearchBox.IsFocused == false ) SearchBox.Focus();
		}

		/// <summary>
		/// hook to process messages from the application's main message loop
		/// </summary>
		private IntPtr WndProc( IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled )
		{
			const int WM_DISPLAYCHANGE = 0x007e;
			const int WM_HOTKEY = 0x0312;

			switch ( msg )
			{
				case WM_DISPLAYCHANGE:
					{
						//refresh the tray icon to fix icon blurring issue in windows 10
						_RemoveTrayIcon();
						_CreateTrayIcon();

						break;
					}
				case WM_HOTKEY:
					{
						_OnGlobalHotkeyPressed(wParam.ToInt32());
						handled = true;
						break;
					}
				default: break;
			}

			return IntPtr.Zero;
		}

		/// <summary>
		/// allow the user to drag the window using the title bar
		/// </summary>
		private void TitleBar_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
		{
			if ( m_settings.AllowRepositioning )
			{
				this.DragMove();

				_CalculateWindowRelativePosition();

				//factor in taskbar to fix position problem
				m_settings.WindowX = (int)this.Left;
				m_settings.WindowY = (int)this.Top;
			}
		}

		/// <summary>
		/// when clicking anywhere outside the filter list, hide the filter list
		/// </summary>
		private void InnerWindow_PreviewMouseDown( object sender, MouseButtonEventArgs e )
		{
			SearchFilterList.Visibility = Visibility.Collapsed;
			SearchBox.Focus();
		}

		/// <summary>
		/// allow the "Filers <Press Tag>" label to act as a button and open the filter menu
		/// </summary>
		private void ActiveFilterText_MouseLeftButtonDown( object sender, MouseButtonEventArgs e )
		{
			if ( SearchFilterList.Visibility == Visibility.Collapsed )
			{
				SearchFilterList.Visibility = Visibility.Visible;
			}
			else
			{
				SearchFilterList.Visibility = Visibility.Collapsed;
			}
		}

		/// <summary>
		/// keep the search box focused at all times
		/// </summary>
		private void SearchBox_LostFocus( object sender, RoutedEventArgs e )
		{
			if ( this.Visibility != Visibility.Visible ) return;

			SearchBox.Focus();
		}

		/// <summary>
		/// minimize the window when it loses focus (if enabled in the settings)
		/// </summary>
		private void Window_Deactivated( object sender, EventArgs e )
		{
			if(m_settings.MinimizeOnFocusLost)
			{
				_MinimizeToTray();
			}

			if(Visibility == Visibility.Visible) this.Topmost = m_settings.Topmost;
		}

		/// <summary>
		/// ensure that there is always a selected item as long as there are items in the search results box
		/// </summary>
		private void SearchResultListView_CollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
		{
			if ( SearchResultListView.Items.Count > 0 && SearchResultListView.SelectedIndex == -1 )
			{
				SearchResultListView.SelectedIndex = 0;
			}
		}

		private void MinimizeButton_Click( object sender, RoutedEventArgs e )
		{
			_MinimizeToTray();
		}

		private void Window_Activated( object sender, EventArgs e )
		{
			SearchBox.Focus();
		}
	}
}


//var radioButtonGroup = FilterTab_Any.Parent;
//int filterCount = VisualTreeHelper.GetChildrenCount(radioButtonGroup);

//List<RadioButton> radioButtons = new List<RadioButton>();

//int selectedIndex = -1;
//for ( int i = 0; i < filterCount; i++ )
//{
//	var child = VisualTreeHelper.GetChild(radioButtonGroup, i);

//	RadioButton rb = child as RadioButton;
//	if ( rb == null ) continue;

//	radioButtons.Add(rb);

//	if(rb.IsChecked == true)
//	{
//		selectedIndex = radioButtons.Count - 1;
//	}
//}

////if there is no current selection, default to the any tab
//if(selectedIndex == -1)
//{
//	FilterTab_Any.IsChecked = true;
//}

////disable the current selection and select the next radio button in the list
//radioButtons[selectedIndex].IsChecked = false;
//if (selectedIndex == radioButtons.Count - 1)
//{
//	radioButtons[0].IsChecked = true;
//	selectedIndex = 0;

//}else
//{
//	radioButtons[selectedIndex + 1].IsChecked = true;
//	selectedIndex++;
//}

//if(selectedIndex < 4) ActiveFilterText.Text = $"Filters ({((FilterTab)selectedIndex).ToString()})";