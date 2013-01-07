using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.Diagnostics;

namespace TaskbarShortcuts
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			System.Windows.Forms.Application.EnableVisualStyles();
			SharedClasses.AutoUpdating.CheckForUpdates_ExceptionHandler();

			//UserMessages.ShowInfoMessage(Environment.CommandLine);
			string[] args = Environment.GetCommandLineArgs();
			if (args.Length > 1)//We are trying to run one of our shortcuts, currently 'args' in format ThisAppExe ShortcutExe ShorcutPossibleArguments
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

			base.OnStartup(e);

			TaskbarShortcuts.MainWindow mw = new MainWindow();
			mw.ShowDialog();
		}
	}
}
