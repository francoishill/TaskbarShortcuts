using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using SharedClasses;

namespace TaskbarShortcuts
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public enum UserTasks { Usertask_AppFromText, Usertask_ChromeAppFromUrl, Usertask_Chrome_IncognitoAppFromUrl };//These UserTasks are added to the Windows 7 JumpList

		protected override void OnStartup(StartupEventArgs e)
		{
			System.Windows.Forms.Application.EnableVisualStyles();
			SharedClasses.AutoUpdating.CheckForUpdates_ExceptionHandler();

			//int a = 0;
			//var b = a / a;

			//UserMessages.ShowInfoMessage(Environment.CommandLine);
			string[] args = Environment.GetCommandLineArgs();
			if (args.Length > 1)//We are trying to run one of our shortcuts, currently 'args' in format ThisAppExe ShortcutExe ShorcutPossibleArguments
			{
				bool hasUsertaskArgument = false;
				bool showWindowAfterUsertask = false;

				foreach (UserTasks ut in Enum.GetValues(typeof(UserTasks)))
					if (args[1].Equals(ut.ToString(), StringComparison.InvariantCultureIgnoreCase))
					{
						hasUsertaskArgument = true;

						ApplicationItem app = null;
						switch (ut)
						{
							case UserTasks.Usertask_AppFromText:
								app = ApplicationItem.CreateFromClipboardText(err => UserMessages.ShowErrorMessage(err));
								if (app != null)
								{
									if (TaskbarShortcuts.MainWindow.preWindowCreatedList == null)
										TaskbarShortcuts.MainWindow.preWindowCreatedList = new List<ApplicationItem>();
									TaskbarShortcuts.MainWindow.preWindowCreatedList.Add(app);
									showWindowAfterUsertask = true;
								}
								break;
							case UserTasks.Usertask_ChromeAppFromUrl:
								app = ApplicationItem.CreateChromeAppFromClipboardUrl(err => UserMessages.ShowErrorMessage(err), false);
								if (app != null)
								{
									if (TaskbarShortcuts.MainWindow.preWindowCreatedList == null)
										TaskbarShortcuts.MainWindow.preWindowCreatedList = new List<ApplicationItem>();
									TaskbarShortcuts.MainWindow.preWindowCreatedList.Add(app);
									showWindowAfterUsertask = true;
								}
								break;
							case UserTasks.Usertask_Chrome_IncognitoAppFromUrl:
								app = ApplicationItem.CreateChromeAppFromClipboardUrl(err => UserMessages.ShowErrorMessage(err), true);
								if (app != null)
								{
									if (TaskbarShortcuts.MainWindow.preWindowCreatedList == null)
										TaskbarShortcuts.MainWindow.preWindowCreatedList = new List<ApplicationItem>();
									TaskbarShortcuts.MainWindow.preWindowCreatedList.Add(app);
									showWindowAfterUsertask = true;
								}
								break;
							default:
								UserMessages.ShowInfoMessage("Unsupported user task: " + ut);
								break;
						}
					}

				if (hasUsertaskArgument && !showWindowAfterUsertask)
				{
					Application.Current.Shutdown(0);
					return;
				}
				else if (!hasUsertaskArgument)
				{
					string exepath = args[1];
					string shortcutArgsCombined = null;
					if (args.Length > 2)//We have arguments for our shorcut
					{
						shortcutArgsCombined = "";
						for (int i = 2; i < args.Length; i++)
							shortcutArgsCombined +=
								(shortcutArgsCombined.Length > 0 ? " " : "")
								+ "\"" + args[i].Trim('"', '\'') + "\"";
					}

					if (shortcutArgsCombined == null)
						Process.Start(exepath);
					else
						Process.Start(exepath, shortcutArgsCombined);

					Application.Current.Shutdown(0);
					return;
				}
			}

			base.OnStartup(e);

			TaskbarShortcuts.MainWindow mw = new MainWindow();
			mw.ShowDialog();
		}
	}
}
