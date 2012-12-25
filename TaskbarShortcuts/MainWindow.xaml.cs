using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using System.IO;
using SharedClasses;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace TaskbarShortcuts
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
		}

		private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (Mouse.RightButton == MouseButtonState.Pressed)
				this.DragMove();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			this.UpdateLayout();

			Win32Api.POINT mousePos;
			if (!Win32Api.GetCursorPos(out mousePos))
				return;

			var wa = SystemParameters.WorkArea;

			//double x = wa.Left + (wa.Width - this.Width) / 2;
			//if (x < wa.Left) x = wa.Left;
			double x = mousePos.X - (this.Width / 2);
			if (x < wa.Left) x = wa.Left;
			if (x + this.Width > wa.Right) x = wa.Right - this.Width;
			this.Left = x;

			double y = mousePos.Y - (this.Height / 2);
			if (y < wa.Top) y = wa.Top;
			if (y + this.Height > wa.Bottom) y = wa.Bottom - this.Height;
			this.Top = y;

			listboxApplications.ItemsSource = 
				DuplicatedFromAutoUpdatedMainWindow_GetListOfInstalledApplications()
				.Select(kv => new ApplicationItem(kv.Key, kv.Value));
		}

		private static Dictionary<string, string> DuplicatedFromAutoUpdatedMainWindow_GetListOfInstalledApplications()
		{
			Dictionary<string, string> tmpdict = new Dictionary<string, string>();
			using (var uninstallRootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryInterop.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32)
				.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
			{
				if (null == uninstallRootKey)
					return null;
				var appKeys = uninstallRootKey.GetSubKeyNames().ToArray();
				foreach (string appkeyname in appKeys)
				{
					try
					{
						using (RegistryKey appkey = uninstallRootKey.OpenSubKey(appkeyname))
						{
							object publisherValue = appkey.GetValue("Publisher");
							if (publisherValue == null)
								continue;
							if (!publisherValue.ToString().Trim().Equals(NsisInterop.cDefaultPublisherName))
								continue;
							/*object urlInfoValue = appkey.GetValue("URLInfoAbout");
							if (urlInfoValue == null)
								continue;//The value must exist for URLInfoAbout
							if (!urlInfoValue.ToString().StartsWith(SettingsSimple.HomePcUrls.Instance.AppsPublishingRoot, StringComparison.InvariantCultureIgnoreCase))
								continue;//The URLInfoAbout value must start with our AppsPublishingRoot*/

							//If we reached this point in the foreach loop, this application is one of our own, now make sure the EXE also exists
							object displayIcon = appkey.GetValue("DisplayIcon");
							//TODO: For now we use the DisplayIcon, is this the best way, what if DisplayIcon is different from EXE
							if (displayIcon == null)
								continue;//We need the DisplayIcon value, it contains the full path of the EXE
							if (!File.Exists(displayIcon.ToString()))
								continue;//The application is probably not installed
							//At this point we know the registry entry is our own application and it is actaully installed (file exists)
							string exePath = displayIcon.ToString();
							string appname = Path.GetFileNameWithoutExtension(exePath);
							if (!tmpdict.ContainsKey(appname))
								tmpdict.Add(appname, exePath);
						}
					}
					catch { }
				}
			}
			return tmpdict;
		}

		private void mainItemBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			FrameworkElement fe = sender as FrameworkElement;
			if (fe == null) return;
			ApplicationItem appitem = fe.DataContext as ApplicationItem;
			if (appitem == null) return;
			e.Handled = true;
			Process.Start("explorer", string.Format("/select,\"{0}\"", appitem.ApplicationExePath));
		}
	}

	public class ApplicationItem
	{
		public string ApplicationName { get; private set; }
		public string ApplicationExePath { get; private set; }

		private ImageSource _applicationicon;
		public ImageSource ApplicationIcon
		{
			get { if (_applicationicon == null) _applicationicon = IconsInterop.IconExtractor.Extract(ApplicationExePath, IconsInterop.IconExtractor.IconSize.Large).IconToImageSource(); return _applicationicon; }
		}

		public ApplicationItem(string ApplicationName, string ApplicationExePath)
		{
			this.ApplicationName = ApplicationName;
			this.ApplicationExePath = ApplicationExePath;
		}
	}
}
