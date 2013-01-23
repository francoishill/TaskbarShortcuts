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
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Reflection;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.WindowsAPICodePack.Shell;

namespace TaskbarShortcuts
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public const string cThisAppName = "TaskbarShortcuts";
		private const double cOpacityIfNotMouseOver = 1.0;//0.4; FOR NOW we keep this 1.0 because this app will only be open while its used (user looking for application), so must never be opague

		private Action<string> actionOnError = err => UserMessages.ShowErrorMessage(err);

		private const string cResourceKeyName_Setting_MaxItemWidth = "cMaxItemWidth";

		private readonly string filepathForApplicationList = SettingsInterop.GetFullFilePathInLocalAppdata("ListOfApplications.txt", cThisAppName);
		private readonly string filePath_Setting_ItemMaxWidth = SettingsInterop.GetFullFilePathInLocalAppdata("ItemMaxWidth.txt", cThisAppName, "Settings");

		public static List<ApplicationItem> preWindowCreatedList = new List<ApplicationItem>();//This list could have been created in App.xaml.cs
		ObservableCollection<ApplicationItem> listOfApps;

		public MainWindow()
		{
			InitializeComponent();
			this.Opacity = cOpacityIfNotMouseOver;
			ApplicationItem.OnErrorHandler = actionOnError;
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			this.UpdateLayout();

			//PositionWindowAtMouseCursor();

			/*listboxApplications.ItemsSource = 
				DuplicatedFromAutoUpdatedMainWindow_GetListOfInstalledApplications()
				.Select(kv => new ApplicationItem(kv.Key, kv.Value));*/
			LoadListOfApplications();

			if (!this.LoadLastWindowPosition(cThisAppName))
				LoadDefaultWindowSize();
			LoadMaxItemWidthFromSettings();
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			this.SaveLastWindowPosition(cThisAppName);
		}

		private void PositionWindowAtMouseCursor()
		{
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
		}

		private void LoadDefaultWindowSize()
		{
			this.Width = 500.0;
			this.Height = 300.0;
		}

		private void LoadMaxItemWidthFromSettings()
		{
			try
			{
				if (!File.Exists(filePath_Setting_ItemMaxWidth))
				{
					menuItemSetting_itemMaxWidth.Text = this.Resources[cResourceKeyName_Setting_MaxItemWidth].ToString();
					return;
				}

				double tmpitemMaxwidth;
				if (!double.TryParse(File.ReadAllText(filePath_Setting_ItemMaxWidth).Trim(), out tmpitemMaxwidth))
					return;
				this.Resources[cResourceKeyName_Setting_MaxItemWidth] = tmpitemMaxwidth;
				menuItemSetting_itemMaxWidth.Text = tmpitemMaxwidth.ToString();
			}
			catch (Exception exc)
			{
				UserMessages.ShowWarningMessage("Unable to load item max width: " + exc.Message);
			}
		}

		private void SaveMaxItemWidthToSettings()
		{
			try
			{
				double currentItemMaxWidth = (double)this.Resources[cResourceKeyName_Setting_MaxItemWidth];
				File.WriteAllText(filePath_Setting_ItemMaxWidth, currentItemMaxWidth.ToString());
			}
			catch (Exception exc)
			{
				UserMessages.ShowWarningMessage("Unable to save item max width to settings: " + exc.Message);
			}
		}

		private bool RightButtonDownWasUsedForDragMove = false;
		private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ChangedButton == MouseButton.Left)
			{
				if (Mouse.RightButton == MouseButtonState.Pressed)
				{
					e.Handled = true;
					RightButtonDownWasUsedForDragMove = true;
					this.DragMove();
				}
			}
			else if (e.ChangedButton == MouseButton.Middle)
			{
				this.Close();
			}
		}

		private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
		{
			if (RightButtonDownWasUsedForDragMove)
			{
				if (Mouse.RightButton == MouseButtonState.Released)
				{
					RightButtonDownWasUsedForDragMove = false;
					e.Handled = true;//We handle MouseRightButtonUp so we don't show the context menu after DragMove
				}
			}
		}

		private void Window_MouseEnter(object sender, MouseEventArgs e)
		{
			this.Opacity = 1.0;
		}

		private void Window_MouseLeave(object sender, MouseEventArgs e)
		{
			this.Opacity = cOpacityIfNotMouseOver;
		}

		private void LoadListOfApplications()
		{
			ObservableCollection<ApplicationItem> tmplist = new ObservableCollection<ApplicationItem>();
			if (File.Exists(filepathForApplicationList))
			{
				var filelines = File.ReadAllLines(filepathForApplicationList)
					.Where(fl => !string.IsNullOrWhiteSpace(fl) && (fl.Split('|').Length == 2 || fl.Split('|').Length == 3));
				foreach (var fl in filelines)
					tmplist.Add(ApplicationItem.CreateFromFileLinePipeDelimited(fl));
			}
			listOfApps = tmplist;
			if (preWindowCreatedList != null && preWindowCreatedList.Count > 0)
			{
				foreach (var preCreateApp in preWindowCreatedList)
					listOfApps.Add(preCreateApp);
				SaveListOfApplications();
			}
			listboxApplications.ItemsSource = listOfApps;

			listOfApps.CollectionChanged += (sn, ev) =>
			{
				if (isInDragDropMode)
					SaveListOfApplications();
			};

			RepopulateAndRefreshJumpList();
		}

		private void SaveListOfApplications()
		{
			File.WriteAllLines(
				filepathForApplicationList,
				listOfApps.Select(app => app.GetFileLineStringPipeDelimited()));
			RepopulateAndRefreshJumpList();
		}

		private void RepopulateAndRefreshJumpList()
		{
			JumpList jumplist = Windows7JumpListsInterop.RepopulateAndRefreshJumpList(
				new List<KeyValuePair<string, IEnumerable<Windows7JumpListsInterop.JumplistItem>>>()
				{
					new KeyValuePair<string,IEnumerable<Windows7JumpListsInterop.JumplistItem>>(
						"App shortcuts",
						listOfApps.Select(app => new Windows7JumpListsInterop.JumplistItem(Environment.ExpandEnvironmentVariables(app.ApplicationExePath), app.ApplicationName, app.ApplicationArguments, app.IsChromeApp ? app.GetChromeAppIconFilepath() : null)))
				});

			jumplist.AddUserTasks(new JumpListSeparator());
			jumplist.AddUserTasks(new JumpListLink(Environment.GetCommandLineArgs()[0], "App from text")
			{
				Arguments = App.UserTasks.Usertask_AppFromText.ToString(),
				IconReference = new IconReference("SHELL32.dll", 70)
			});
			jumplist.AddUserTasks(new JumpListLink(Environment.GetCommandLineArgs()[0], "Chrome app from URL")
			{
				Arguments = App.UserTasks.Usertask_ChromeAppFromUrl.ToString(),
				IconReference = new IconReference(ApplicationItem.GetChromeExePath(), 0)
			});

			jumplist.AddUserTasks(new JumpListLink(Environment.GetCommandLineArgs()[0], "Chrome app (incognito) from URL")
			{
				Arguments = App.UserTasks.Usertask_Chrome_IncognitoAppFromUrl.ToString(),
				IconReference = new IconReference(ApplicationItem.GetChromeExePath(), 4)
			});
			jumplist.Refresh();

			//var _jumpList = JumpList.CreateJumpList();
			//_jumpList.ClearAllUserTasks();

			//JumpListCustomCategory userActionsCategory = new JumpListCustomCategory("App shortcuts");

			//foreach (var app in listOfApps)
			//{
			//    JumpListLink actionPublishQuickAccess = new JumpListLink(Assembly.GetEntryAssembly().Location, appnameWithSpaces);
			//    actionPublishQuickAccess.Arguments = appname;
			//    if (File.Exists(appExePath))
			//        actionPublishQuickAccess.IconReference = new Microsoft.WindowsAPICodePack.Shell.IconReference(appExePath, 0);
			//    userActionsCategory.AddJumpListItems(actionPublishQuickAccess);
			//}

			//int listcnt = 0;
			//foreach (string appname in SettingsSimple.PublishSettings.Instance.ListedApplicationNames.OrderBy(s => s))
			//{
			//    comboBoxProjectName.Items.Add(
			//        new ApplicationToPublish(
			//            appname,
			//            HasPlugins: appname.Equals("QuickAccess", StringComparison.InvariantCultureIgnoreCase),
			//            UpdateRevisionNumber: false,
			//            AutostartWithWindows: appname.Equals(StringComparison.InvariantCultureIgnoreCase,
			//                "ApplicationManager", "MonitorSystem", "QuickAccess", "StartupTodoManager", "TestingMonitorSubversion")
			//            ));

			//    string appnameWithSpaces = appname.InsertSpacesBeforeCamelCase();
			//    string appExePath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\') + string.Format("\\{0}\\{1}.exe", appnameWithSpaces, appname);
			//    JumpListLink actionPublishQuickAccess = new JumpListLink(Assembly.GetEntryAssembly().Location, appnameWithSpaces);
			//    actionPublishQuickAccess.Arguments = appname;
			//    if (File.Exists(appExePath))
			//        actionPublishQuickAccess.IconReference = new Microsoft.WindowsAPICodePack.Shell.IconReference(appExePath, 0);
			//    userActionsCategory.AddJumpListItems(actionPublishQuickAccess);
			//    listcnt++;
			//}
			//if (listcnt > _jumpList.MaxSlotsInList)
			//    OnErrorHandler(
			//        string.Format("The taskbar jumplist has {0} maximum slots but the list is {1}, the extra items will be truncated", _jumpList.MaxSlotsInList, listcnt));

			//_jumpList.AddCustomCategories(userActionsCategory);
			//_jumpList.Refresh();
		}

		/*private static Dictionary<string, string> DuplicatedFromAutoUpdatedMainWindow_GetListOfInstalledApplications()
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
							//object urlInfoValue = appkey.GetValue("URLInfoAbout");
							//if (urlInfoValue == null)
							//    continue;//The value must exist for URLInfoAbout
							//if (!urlInfoValue.ToString().StartsWith(SettingsSimple.HomePcUrls.Instance.AppsPublishingRoot, StringComparison.InvariantCultureIgnoreCase))
							//    continue;//The URLInfoAbout value must start with our AppsPublishingRoot

							//If we reached this point in the foreach loop, this application is one of our own, now make sure the EXE also exists
							object displayIcon = appkey.GetValue("DisplayIcon");
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
		}*/

		private void mainItemBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (Keyboard.Modifiers == ModifierKeys.Control)//Drag the appExe or ChormeAppUrl
			{
				ApplicationItem appitem = WPFHelper.GetFromObjectSender<ApplicationItem>(sender);
				if (appitem == null) return;
				e.Handled = true;

				string textToDrag = appitem.ApplicationExePath;
				if (appitem.IsChromeUrlApp_NotPackaged)
					textToDrag = appitem.GetChromeAppUrlOrId();
				DragDrop.DoDragDrop(listboxApplications, textToDrag, DragDropEffects.Copy);
			}
			else if (Keyboard.Modifiers == ModifierKeys.Alt)
			{//Alt key down reserved for moving order of items (by drag-drop), see PreviewMouseLeftButtonDown of listbox
				return;
			}
			else
			{
				e.Handled = true;
				WPFHelper.DoActionIfObtainedItemFromObjectSender<ApplicationItem>(sender, (appitem) =>
				{
					//Process.Start("explorer", string.Format("/select,\"{0}\"", appitem.ApplicationExePath));
					try
					{
						appitem.RunCommand();
						this.Close();
					}
					catch (Exception exc)
					{
						actionOnError("Error running application: " + exc.Message);
					}
				});
			}
		}

		private void mainItemBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{

		}

		private void labelAbout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			bool origTopmost = this.Topmost;
			this.Topmost = false;
			try
			{
				AboutWindow2.ShowAboutWindow(new System.Collections.ObjectModel.ObservableCollection<DisplayItem>()
				{
					new DisplayItem("Author", "Francois Hill"),
					new DisplayItem("Icon(s) obtained from", null)
				});
			}
			finally
			{
				this.Topmost = origTopmost;
			}
		}

		private void Window_PreviewDragEnter(object sender, DragEventArgs e)
		{
			this.Opacity = 1.0;
		}

		private void Window_PreviewDragLeave(object sender, DragEventArgs e)
		{
			this.Opacity = cOpacityIfNotMouseOver;
		}

		private void Window_PreviewDragOver(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))//We don't check that only ONE FILE is dropped, as we allow multiple and we allow folders too
			{
				e.Handled = true;
				e.Effects = DragDropEffects.Link;
			}
			else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
			{
				e.Handled = true;
				e.Effects = DragDropEffects.Copy;
			}
			else
				e.Effects = DragDropEffects.None;
		}

		private void Window_PreviewDrop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
			{
				string[] filepaths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
				if (filepaths.Length > 0)
				{
					foreach (var f in filepaths)
						listOfApps.Add(new ApplicationItem(Path.GetFileNameWithoutExtension(f), f));
					SaveListOfApplications();
				}
			}
		}

		private void menuitemRemoveFromList_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.DoActionIfObtainedItemFromObjectSender<ApplicationItem>(sender, (appitem) =>
			{
				if (UserMessages.Confirm("Are you sure you want to delete '" + appitem.ApplicationName + "'?"))
				{
					listOfApps.Remove(appitem);
					SaveListOfApplications();
				}
			});
		}

		private void menuitemOpenInExplorer_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.DoActionIfObtainedItemFromObjectSender<ApplicationItem>(sender, (appitem) =>
			{
				appitem.OpenInExplorer();
			});
		}

		private void menuitemCopyToClipboardExecutablePath_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.DoActionIfObtainedItemFromObjectSender<ApplicationItem>(sender, (appitem) =>
			{
				appitem.CopyExecutablePathToClipboard();
			});
		}

		private void menuitemCopyToClipboardChromeAppUrl_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.DoActionIfObtainedItemFromObjectSender<ApplicationItem>(sender, (appitem) =>
			{
				appitem.CopyChromeAppUrlToClipboard();
			});
		}

		private void menuitemAppFromClipboardText(object sender, RoutedEventArgs e)
		{
			ApplicationItem appitem = ApplicationItem.CreateFromClipboardText(actionOnError);
			if (appitem == null)
				return;

			listOfApps.Add(appitem);
			SaveListOfApplications();
		}

		private void menuitemChromeAppFromClipboardUrl(object sender, RoutedEventArgs e)
		{
			ApplicationItem appitem = ApplicationItem.CreateChromeAppFromClipboardUrl(actionOnError, false);
			if (appitem == null)
				return;

			listOfApps.Add(appitem);
			SaveListOfApplications();
		}

		private void menuitemChromeIncognitoAppFromClipboardUrl(object sender, RoutedEventArgs e)
		{
			ApplicationItem appitem = ApplicationItem.CreateChromeAppFromClipboardUrl(actionOnError, true);
			if (appitem == null)
				return;

			listOfApps.Add(appitem);
			SaveListOfApplications();
		}

		private void menuitemExit_Click(object sender, RoutedEventArgs e)
		{
			this.Close();
		}

		private void menuItemSetting_itemMaxWidth_LostFocus(object sender, RoutedEventArgs e)
		{
			try
			{
				double newSetting = double.Parse(menuItemSetting_itemMaxWidth.Text);
				double oldSetting = (double)this.Resources[cResourceKeyName_Setting_MaxItemWidth];
				if (!newSetting.Equals(oldSetting))
				{
					this.Resources[cResourceKeyName_Setting_MaxItemWidth] = newSetting;
					SaveMaxItemWidthToSettings();
				}
			}
			catch (Exception exc)
			{
				UserMessages.ShowErrorMessage("Unable to set new item width: " + exc.Message);
				menuItemSetting_itemMaxWidth.Text = this.Resources[cResourceKeyName_Setting_MaxItemWidth].ToString();
			}
		}

		private KeyValuePair<ApplicationItem, string>? focusedAppValueOnFocus;
		private void textboxApplicationName_GotFocus(object sender, RoutedEventArgs e)
		{
			ApplicationItem appitem = WPFHelper.GetFromObjectSender<ApplicationItem>(sender);
			if (appitem == null) return;
			focusedAppValueOnFocus = new KeyValuePair<ApplicationItem, string>(appitem, appitem.ApplicationName);
		}

		private void textboxApplicationName_LostFocus(object sender, RoutedEventArgs e)
		{
			ApplicationItem appitem = WPFHelper.GetFromObjectSender<ApplicationItem>(sender);
			if (appitem == null) return;
			if (focusedAppValueOnFocus.HasValue
				&& focusedAppValueOnFocus.Value.Key == appitem
				&& focusedAppValueOnFocus.Value.Value != appitem.ApplicationName)//The ApplicationName was changed by user
				SaveListOfApplications();
			focusedAppValueOnFocus = null;
		}

		private bool isInDragDropMode = false;
		private void listboxApplications_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			GongSolutions.Wpf.DragDrop.DragDrop.SetIsDragSource(listboxApplications, false);
			GongSolutions.Wpf.DragDrop.DragDrop.SetIsDropTarget(listboxApplications, false);
			if (Keyboard.Modifiers == ModifierKeys.Alt)
			{
				GongSolutions.Wpf.DragDrop.DragDrop.SetIsDragSource(listboxApplications, true);
				GongSolutions.Wpf.DragDrop.DragDrop.SetIsDropTarget(listboxApplications, true);
				isInDragDropMode = true;
				//e.Handled = true; DO NO Handle, this is left to GongSolutions to handle
			}
			/*ListBox parent = listboxApplications;//sender as ListBox;
			ApplicationItem data = GetObjectDataFromPoint(parent, e.GetPosition(parent)) as ApplicationItem;
			if (data != null)
			{
				DragDrop.DoDragDrop(parent, data, DragDropEffects.Move);
				e.Handled = true;
			}
			ApplicationItem appitem = appItemFromObjectSender(sender);
			if (appitem == null) return;
			e.Handled = true;
			DragDrop.DoDragDrop(listboxApplications, appitem, DragDropEffects.Move);*/
		}

		private void listboxApplications_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			isInDragDropMode = true;
		}

		/*private void listboxApplications_Drop(object sender, DragEventArgs e)
		{
			ListBox parent = sender as ListBox;
			ApplicationItem data = e.Data.GetData(typeof(ApplicationItem)) as ApplicationItem;
			ApplicationItem objectToPlaceBefore = GetObjectDataFromPoint(parent, e.GetPosition(parent)) as ApplicationItem;
			if (data != null && objectToPlaceBefore != null)
			{
				int index = listOfApps.IndexOf(objectToPlaceBefore);
				listOfApps.Remove(data);
				listOfApps.Insert(index, data);
				parent.SelectedItem = data;
				SaveListOfApplications();
			}
		}

		private static object GetObjectDataFromPoint(ListBox source, Point point)
		{
			UIElement element = source.InputHitTest(point) as UIElement;
			if (element != null)
			{
				object data = DependencyProperty.UnsetValue;
				while (data == DependencyProperty.UnsetValue)
				{
					data = source.ItemContainerGenerator.ItemFromContainer(element);
					if (data == DependencyProperty.UnsetValue)
						element = VisualTreeHelper.GetParent(element) as UIElement;
					if (element == source)
						return null;
				}
				if (data != DependencyProperty.UnsetValue)
					return data;
			}

			return null;
		}

		private void listboxApplications_DragOver(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(typeof(ApplicationItem)))
			{
				e.Effects = DragDropEffects.Move;
				e.Handled = true;
			}
		}*/
	}

	public class ApplicationItem
	{
		private static Action<string> _onerrorhandler = null;
		public static Action<string> OnErrorHandler
		{
			get
			{
				if (_onerrorhandler == null)
				{
					_onerrorhandler = delegate { };
					UserMessages.ShowWarningMessage("No error handler assigned, all errors will not be displayed");
				}
				return _onerrorhandler;
			}
			set
			{
				_onerrorhandler = value;
			}
		}

		public string ApplicationName { get; set; }
		public string ApplicationExePath { get; private set; }
		public string ApplicationArguments { get; private set; }

		private ImageSource _applicationicon;
		public ImageSource ApplicationIcon
		{
			get { if (_applicationicon == null) _applicationicon = IconsInterop.GetIconFromFilePath(Environment.ExpandEnvironmentVariables(ApplicationExePath)); return _applicationicon; }
		}

		public ApplicationItem(string ApplicationName, string ApplicationExePath, string ApplicationArguments = null, string IconFilePath = null)
		{
			this.ApplicationName = ApplicationName;
			this.ApplicationExePath = ApplicationExePath;
			this.ApplicationArguments = ApplicationArguments;
			if (IconFilePath != null)
				_applicationicon = IconsInterop.GetIconFromFilePath(IconFilePath);

			if (IsChromeApp)
				EnsureFaviconExistsAndIsUsed();
		}

		public bool IsChromePackagedAppUrl
		{
			get
			{
				return
					this.HasArguments()
					&& Environment.ExpandEnvironmentVariables(this.ApplicationExePath).Equals(GetChromeExePath(), StringComparison.InvariantCultureIgnoreCase)
					&& this.ApplicationArguments.IndexOf("--app-id=", StringComparison.InvariantCultureIgnoreCase) != -1;
			}
		}
		public bool IsChromeUrlApp_NotPackaged
		{
			get
			{
				return
					this.HasArguments()
					&& Environment.ExpandEnvironmentVariables(this.ApplicationExePath).Equals(GetChromeExePath(), StringComparison.InvariantCultureIgnoreCase)
					&& this.ApplicationArguments.IndexOf("--app=", StringComparison.InvariantCultureIgnoreCase) != -1;
			}
		}
		public bool IsChromeApp { get { return IsChromePackagedAppUrl || IsChromeUrlApp_NotPackaged; } }

		public static ApplicationItem CreateFromFileLinePipeDelimited(string fileLine)
		{
			string[] pipeDelimited = fileLine.Split('|');
			return new ApplicationItem(
				pipeDelimited[0],
				pipeDelimited[1],
				pipeDelimited.Length > 2 ? pipeDelimited[2] : null);
		}

		public string GetFileLineStringPipeDelimited()
		{
			return
				this.ApplicationName
				+ "|" + this.ApplicationExePath
				+ (this.HasArguments() ? "|" + this.ApplicationArguments : "");
		}

		public bool HasArguments()
		{
			return this.ApplicationArguments != null;
		}

		public static ApplicationItem CreateFromClipboardText(Action<string> onError)
		{
			if (onError == null) onError = delegate { };

			if (!Clipboard.ContainsText())
			{
				onError("Clipboard does not contain text.");
				return null;
			}

			string textWithPossibleAppPath = Clipboard.GetText();
			string command_orError, arguments;
			if (!CommandlineArgumentsInterop.GetCommandAndArgumentsFromString(textWithPossibleAppPath, out command_orError, out arguments))
			{
				onError("Unable to obtain app from Clipboard Text:" + Environment.NewLine + command_orError);
				return null;
			}
			else
			{
				var newApp = new ApplicationItem(
					Path.GetFileNameWithoutExtension(Environment.ExpandEnvironmentVariables(command_orError)),
					command_orError,
					arguments);
				if (newApp.IsChromeApp)
				{
					if (!newApp.IsChromePackagedAppUrl)
					{
						string tmpUri = newApp.GetChromeAppUrlFromArguments();
						if (!tmpUri.StartsWith("file:///", StringComparison.InvariantCultureIgnoreCase))
							newApp.ApplicationName = new Uri(tmpUri).Host;
						else
							newApp.ApplicationName =
								Path.GetFileName(tmpUri.Substring("file:///".Length)
								.TrimEnd('/', '\\')
								.Replace('/', '\\'));
					}
					else
					{
						//newApp.ApplicationName
						string appId = newApp.GetChromeAppIdFromArguments();
						string iconFilepath = GetIconFilepathForChromeAppId(appId);
						if (iconFilepath != null)
							newApp.ApplicationName = Path.GetFileNameWithoutExtension(iconFilepath);
					}
				}
				return newApp;
			}
		}

		public static string GetChromeExePath()
		{
			return RegistryInterop.GetAppPathFromRegistry("chrome.exe");
		}
		public static ApplicationItem CreateChromeAppFromClipboardUrl(Action<string> onError, bool incognito = false)
		{
			if (onError == null) onError = delegate { };

			if (!Clipboard.ContainsText())
			{
				onError("Clipboard does not contain text.");
				return null;
			}

			string textWithPossibleUrl = Clipboard.GetText();
			if (!WebInterop.IsValidUri(textWithPossibleUrl))
			{
				onError("Invalid uri (according to the regex validator), uri: " + textWithPossibleUrl);
				return null;
			}
			else
			{
				string chromeExePath = GetChromeExePath();
				if (chromeExePath == null)
				{
					onError("Unable to find path for chrome.exe from the registry.");
					return null;
				}
				else
				{
					Uri uri = new Uri(textWithPossibleUrl);
					return new ApplicationItem(
						uri.Host,
						chromeExePath,
						(incognito ? "--incognito " : "") + "--app=" + textWithPossibleUrl,
						GetOrDownloadFaviconReturnFilepath(textWithPossibleUrl));
				}
			}
		}

		private static string GetFaviconLocalFilepathFromUrl(string favIconUrl)
		{
			return SettingsInterop.GetFullFilePathInLocalAppdata(
					EncodeAndDecodeInterop.EncodeStringHex(favIconUrl, OnErrorHandler) + ".ico",
					TaskbarShortcuts.MainWindow.cThisAppName,
					"Favicons");
		}

		private static string GetOrDownloadFaviconReturnFilepath(string websiteFullUrl)
		{
			if (websiteFullUrl.StartsWith("file:///", StringComparison.InvariantCultureIgnoreCase))
			{
				string fileOrDirPath =
					websiteFullUrl.Substring("file:///".Length)
					.TrimEnd('/', '\\')
					.Replace('/', '\\');
				if (Directory.Exists(fileOrDirPath))
				{
					//First see if we find a favicon.ico in the local directory
					string localFaviconInDirPath = Path.Combine(fileOrDirPath, "favicon.ico");
					if (File.Exists(localFaviconInDirPath))
						return localFaviconInDirPath;
				}

				string shell32DllPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SHELL32.dll");
				return shell32DllPath + ",3";
			}

			string faviconUrl = WebInterop.GetFaviconUrlFromFullUrl(websiteFullUrl);
			if (faviconUrl == null)
			{
				OnErrorHandler("Could not obtain favicon url from webUrl = " + websiteFullUrl);
				return null;
			}

			string saveIconPath = GetFaviconLocalFilepathFromUrl(faviconUrl);
			if (File.Exists(saveIconPath))
				return saveIconPath;

			//Stopwatch sw = Stopwatch.StartNew();

			//We do not monitor progress for this as it should be very quick
			string tmpFilepath = Path.GetTempFileName();
			try
			{
				new WebClient().DownloadFile(faviconUrl, tmpFilepath);
				File.Move(tmpFilepath, saveIconPath);
				return saveIconPath;
			}
			catch (WebException webexc)
			{
				HttpWebResponse httpresp = webexc.Response as HttpWebResponse;
				if (httpresp == null)//We cannot obtain StatusCode
				{
					OnErrorHandler("Failed to download favicon, unknown http status: " + webexc.Message);
					return null;
				}
				if (httpresp.StatusCode != HttpStatusCode.NotFound)
				{
					OnErrorHandler("Failed to download favicon, unhandled http status code: " + httpresp.StatusCode);
					return null;
				}
				//We did not find the favicon.ico file online, now try downloading the webpage and extract href="..." inside <link href="..." rel="shorcut icon"/>
				string tmperr;
				CookieContainer cookieJar = new CookieContainer();
				string faviconUrlExtractedInsideLinkShortcutIcon = WebInterop.GetFaviconUrlFromWebpageShortcutIconLink(websiteFullUrl, out tmperr, cookieJar);
				if (faviconUrlExtractedInsideLinkShortcutIcon == null)
				{
					OnErrorHandler(tmperr);
					return null;
				}
				else
				{//We not have the url to the favicon (extracted from <link href="http://url/to/favicon123.ico" rel="shortcut icon"
					try
					{
						HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(faviconUrlExtractedInsideLinkShortcutIcon);
						req.CookieContainer = cookieJar;
						var resp = req.GetResponse();
						using (FileStream tmpfileStream = new FileStream(tmpFilepath, FileMode.Create))
						{
							byte[] buffer = new byte[1024];
							int bytesread;
							while ((bytesread = resp.GetResponseStream().Read(buffer, 0, buffer.Length)) > 0)
								tmpfileStream.Write(buffer, 0, bytesread);
						}
						File.Move(tmpFilepath, saveIconPath);
						return saveIconPath;
					}
					catch (Exception exc)
					{
						OnErrorHandler("Failed to download favicon (extracted its url from webpage): " + exc.Message);
						return null;
					}
				}
			}
			catch (Exception exc)
			{
				OnErrorHandler("Failed to download favicon: " + exc.Message);
				return null;
			}

			//sw.Stop();
			//UserMessages.ShowInfoMessage("Elapsed seconds = " + sw.Elapsed.TotalSeconds + ".");
		}

		private static string GetIconFilepathForChromeAppId(string chromeAppId)
		{
			string expectedIconDirectory = Path.Combine(
				Path.GetDirectoryName(Path.GetDirectoryName(GetChromeExePath())),
				@"User Data\Default\Web Applications",
				"_crx_" + chromeAppId);
			if (!Directory.Exists(expectedIconDirectory))
			{
				OnErrorHandler("Could not find icon for Chrome App Id as directory does not exist: " + expectedIconDirectory);
				return null;
			}

			var allIconsInDir = Directory.GetFiles(expectedIconDirectory, "*.ico").ToArray();
			if (allIconsInDir.Length == 0)
			{
				OnErrorHandler("No icon found for Chrome App Id = " + chromeAppId + ", searched in folder = " + expectedIconDirectory);
				return null;
			}
			else
			{
				if (allIconsInDir.Length > 1)
					OnErrorHandler("More than one icon file found, using first, directory: " + expectedIconDirectory);
				return allIconsInDir[0];
			}
		}

		private string GetChromeAppIdFromArguments()
		{
			return Regex.Match(this.ApplicationArguments, @"(?<=\-\-app\-id\=)[^ ]+").ToString();
		}
		private string GetChromeAppUrlFromArguments()
		{
			return Regex.Match(this.ApplicationArguments, @"(?<=\-\-app\=)[^ ]+").ToString();
		}
		public string GetChromeAppUrlOrId()
		{
			if (this.IsChromeUrlApp_NotPackaged)
				return this.GetChromeAppUrlFromArguments();
			else if (this.IsChromePackagedAppUrl)
				return this.GetChromeAppIdFromArguments();
			else
				return null;
		}
		public string GetChromeAppIconFilepath()
		{
			if (this.IsChromeUrlApp_NotPackaged)
			{
				string iconFilepath = GetOrDownloadFaviconReturnFilepath(GetChromeAppUrlOrId());
				return iconFilepath;
			}
			else if (this.IsChromePackagedAppUrl)
			{
				string appId = GetChromeAppUrlOrId();
				string iconFilepath = GetIconFilepathForChromeAppId(appId);
				return iconFilepath;
			}
			return null;
		}
		private void EnsureFaviconExistsAndIsUsed()
		{
			string iconFilepath = GetChromeAppIconFilepath();
			if (iconFilepath != null)
				_applicationicon = IconsInterop.GetIconFromFilePath(iconFilepath);
		}

		public void RunCommand()
		{
			Console.WriteLine("RunCommand()");
			string expandedEnvironmentVariablesExePath = Environment.ExpandEnvironmentVariables(this.ApplicationExePath);
			if (Directory.Exists(expandedEnvironmentVariablesExePath))
				Process.Start("explorer", expandedEnvironmentVariablesExePath);
			else if (this.HasArguments())
				Process.Start(expandedEnvironmentVariablesExePath, this.ApplicationArguments);
			else
				Process.Start(expandedEnvironmentVariablesExePath);
		}

		public void OpenInExplorer()
		{
			string fileToSelectInExplorer = this.ApplicationExePath;
			//if (this.IsChromeApp)
			//    fileToSelectInExplorer = this.ApplicationArguments;
			Process.Start("explorer", "/select,\"" + fileToSelectInExplorer + "\"");
		}

		public void CopyExecutablePathToClipboard()
		{
			Clipboard.SetText(this.ApplicationExePath);
		}

		public void CopyChromeAppUrlToClipboard()
		{
			if (!this.IsChromeApp)
				return;
			Clipboard.SetText(this.GetChromeAppUrlOrId());
		}
	}
}
