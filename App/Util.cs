using Microsoft.Win32;
using PingLogger.Models;
using PingLogger.Workers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace PingLogger
{
	public static class Util
	{
		static Controls.SplashScreen splashScreen;

		public static string FileBasePath 
		{
			get
			{
				if (AppIsClickOnce)
				{
					var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + Path.DirectorySeparatorChar + "PingLogger" + Path.DirectorySeparatorChar;
					if (!Directory.Exists(appDataDir))
					{
						Directory.CreateDirectory(appDataDir);
					}
					return appDataDir;
				}
				else
				{
					var appExePath = Environment.CurrentDirectory;
					if (!appExePath.EndsWith(Path.DirectorySeparatorChar))
					{
						appExePath += Path.DirectorySeparatorChar;
					}
					return appExePath;
				}
			}
		}

		public static bool AppIsClickOnce
		{
			get
			{
				if (File.Exists(AppContext.BaseDirectory + "Launcher.exe") && File.Exists(AppContext.BaseDirectory + "Launcher.manifest"))
				{
					return true;
				}
				else
				{
					return false;
				}
			}
		}

		public static async Task CheckForUpdates()
		{

			if (Config.AppWasUpdated)
			{
				Logger.Info("Application was updated last time it ran, cleaning up.");
				if (File.Exists("./PingLogger-old.exe"))
				{
					File.Delete("./PingLogger-old.exe");
				}
				if (Config.LastTempDir != string.Empty && Directory.Exists(Config.LastTempDir))
				{
					File.Delete(Config.LastTempDir + "/PingLogger-Setup.msi");
					Directory.Delete(Config.LastTempDir);
					Config.LastTempDir = string.Empty;
				}
				Config.AppWasUpdated = false;
			}
			else
			{
				if (Config.UpdateLastChecked.Date >= DateTime.Today)
				{
					Logger.Info("Application already checked for update today, skipping.");
					return;
				}
				splashScreen = new Controls.SplashScreen();
				splashScreen.Show();
				splashScreen.dlProgress.IsIndeterminate = true;
				splashScreen.dlProgress.Value = 1;
				var localVersion = Assembly.GetExecutingAssembly().GetName().Version;

				try
				{
					var httpClient = new WebClient();
					bool downloadComplete = false;
					httpClient.DownloadFileCompleted += (o, i) => { downloadComplete = true; };

					string azureURL = "https://pinglogger.lexdysia.com";

					await httpClient.DownloadFileTaskAsync($"{azureURL}/latest.json", $"./latest.json");

					while (!downloadComplete) { await Task.Delay(100); }
					var latestJson = File.ReadAllText("./latest.json");
					var remoteVersion = JsonSerializer.Deserialize<SerializableVersion>(latestJson);
					File.Delete("./latest.json");

					Logger.Info($"Remote version is {remoteVersion}, currently running {localVersion}");
					if (remoteVersion > localVersion)
					{
						Logger.Info("Remote contains a newer version");
						if (Controls.UpdatePromptDialog.Show())
						{
							if (Config.IsInstalled)
							{
								Config.LastTempDir = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\Temp\\{RandomString(8)}";
								Directory.CreateDirectory(Config.LastTempDir);
								Logger.Info($"Creating temporary path {Config.LastTempDir}");
								Logger.Info($"Downloading newest installer to {Config.LastTempDir}\\PingLogger-Setup.msi");
								var downloadURL = $"{azureURL}/{remoteVersion.Major}{remoteVersion.Minor}{remoteVersion.Build}/win/install/PingLogger-Setup.msi";
								Logger.Info($"Downloading from {downloadURL}");
								using var downloader = new HttpClientDownloadWithProgress(downloadURL, Config.LastTempDir + "\\PingLogger-Setup.msi");
								splashScreen.mainLabel.Text = $"Downloading PingLogger setup v{remoteVersion}";
								downloader.ProgressChanged += Downloader_ProgressChanged;
								await downloader.StartDownload();
								Config.AppWasUpdated = true;
								Logger.Info("Uninstalling current version.");
								Process.Start(new ProcessStartInfo
								{
									FileName = $"{Config.LastTempDir}/PingLogger-Setup.msi",
									UseShellExecute = true,
									Arguments = "/SILENT /CLOSEAPPLICATIONS"
								});

								Logger.Info("Installer started, closing.");
								Environment.Exit(0);
							}
							else
							{
								Logger.Info("Renamed PingLogger.exe to PingLogger-old.exe");
								File.Move("./PingLogger.exe", "./PingLogger-old.exe");
								Logger.Info("Downloading new PingLogger.exe");
								if (remoteVersion is not null)
								{
									string[] fileList = { "libHarfBuzzSharp.dll", "libSkiaSharp.dll", "PingLogger.exe" };
									splashScreen.mainLabel.Text = $"Downloading PingLogger v{remoteVersion}";
									foreach (var file in fileList)
									{
										var downloadUrl = $"{azureURL}/{remoteVersion.Major}{remoteVersion.Minor}{remoteVersion.Build}/win/sf/{file}";
										Logger.Info($"Downloading from {downloadUrl}");
										using var downloader = new HttpClientDownloadWithProgress(downloadUrl, $"./{file}");
										downloader.ProgressChanged += Downloader_ProgressChanged;
										await downloader.StartDownload();
									}
								}

								Config.AppWasUpdated = true;

								Process.Start(new ProcessStartInfo
								{
									FileName = "./PingLogger.exe"
								});

								Logger.Info("Starting new version of PingLogger");
								Environment.Exit(0);
							}
						}
					}
				}
				catch (HttpRequestException ex)
				{
					Logger.Error("Unable to auto update: " + ex.Message);
					return;
				}
			}
			Config.UpdateLastChecked = DateTime.Now;
			return;
		}

		private static void Downloader_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
		{
			if (progressPercentage.HasValue && totalFileSize.HasValue)
			{
				splashScreen.dlProgress.Maximum = 100;
				splashScreen.dlProgress.Value = progressPercentage.Value;
				splashScreen.dlProgress.IsIndeterminate = false;
			}
		}

		public static void CloseSplashScreen()
		{
			try
			{
				splashScreen?.Close();
				splashScreen = null;
			}
			catch (NullReferenceException)
			{
				Logger.Debug("splashScreen was null.");
			}
		}

		/// <summary>
		/// Generates a random string with the specified length.
		/// </summary>
		/// <param name="length">Number of characters in the string</param>
		/// <returns>Random string of letters and numbers</returns>
		public static string RandomString(int length)
		{
			Random random = new Random();
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			return new string(Enumerable.Repeat(chars, length)
			  .Select(s => s[random.Next(s.Length)]).ToArray());
		}

		public static bool IsLightTheme 
		{
			get
			{
				if (Config.Theme == Theme.Auto)
				{
					int lightTheme = Convert.ToInt32(Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 1));
					if (lightTheme == 1)
						return true;
					else
						return false;
				}
				else
				{
					return Config.Theme == Theme.Light;
				}
			}
		}

		public static void SetTheme()
		{
			if (IsLightTheme)
			{
				App.Current.Resources.MergedDictionaries[0].Source = new Uri("/Themes/LightTheme.xaml", UriKind.Relative);
			}
			else
			{
				App.Current.Resources.MergedDictionaries[0].Source = new Uri("/Themes/DarkTheme.xaml", UriKind.Relative);
			}
		}
	}
}
