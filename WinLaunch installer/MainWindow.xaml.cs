using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml;

namespace WinLaunch_installer
{
    public class Installer
    {
        public static void CreateUninstaller(Assembly assembly, string installDir, string name)
        {
            string location = Assembly.GetEntryAssembly().Location;
            string str1 = Path.Combine(installDir, "Setup.exe");
            string destFileName = str1;
            System.IO.File.Copy(location, destFileName, true);
            Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
            using (RegistryKey registryKey1 = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", true))
            {
                if (registryKey1 == null)
                    throw new Exception("Uninstall registry key not found.");
                try
                {
                    RegistryKey registryKey2 = (RegistryKey)null;
                    try
                    {
                        registryKey2 = registryKey1.OpenSubKey(name, true) ?? registryKey1.CreateSubKey(name);
                        if (registryKey2 == null)
                            throw new Exception(string.Format("Unable to create uninstaller '{0}\\{1}'", (object)str1, (object)name));
                        
                        string str2 = "\"" + str1.Replace("/", "\\\\") + "\"";
                        registryKey2.SetValue("DisplayName", (object)name);
                        registryKey2.SetValue("UninstallString", (object)str2);
                    }
                    finally
                    {
                        registryKey2?.Close();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("An error occurred writing uninstall information to the registry.  The service is fully installed but can only be uninstalled manually through the command line.", ex);
                }
            }
        }


        public static string GetDownloadURL()
        {
            string s = (string)null;
            try
            {
                string address = "http://bit.ly/WinlaunchDownload";
                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36");
                    string xml = webClient.DownloadString(address);
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(xml);
                    s = xmlDocument.GetElementsByTagName("url")[0].InnerText;
                    string innerText = xmlDocument.GetElementsByTagName("signature")[0].InnerText;
                    byte[] bytes = Encoding.Unicode.GetBytes(s);
                    byte[] signature = Convert.FromBase64String(innerText);
                    string xmlString = "<RSAKeyValue><Modulus>nPnBFiUsgdANJct8U9CgFLMh0ygdBw8PiZ7G9eBn1K5g9CMlLAaIccRMXP+jl5OZ4fRs22DfiYhMYqkcF+pry31cP3osKlTx0/WsFVonuUfvm4urfM9KT8+nZwJ+37kHcq1f6MHdmb4dbS57XFWiBFWFmPRKccpkIgiXjgrh5JzBBvBS7Ig88M7eUTo/laX6etmMwAodIzPCDswILaoWLhu3QVKmO81Hci5EtREmjcnS9TWMJ6Czdh3/Z1fEAPJiQB2wTxj/CpyH7B+pS0Y/qA/4AqYgH/eTbnk7JHkmhkBSyPcA4Xy9yJrljhws/v9zWcARtSDSz3BEr+QPGnoPEQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
                    RSACryptoServiceProvider cryptoServiceProvider = new RSACryptoServiceProvider();
                    cryptoServiceProvider.FromXmlString(xmlString);
                    if (!cryptoServiceProvider.VerifyData(bytes, (object)CryptoConfig.MapNameToOID("SHA512"), signature))
                        return (string)null;
                }
            }
            catch (Exception ex)
            {
                return (string)null;
            }
            return s;
        }

        public static void Unzip(string zipPath, string folder)
        {
            ZipStorer zipStorer = ZipStorer.Open(zipPath, FileAccess.Read);
            foreach (ZipStorer.ZipFileEntry _zfe in zipStorer.ReadCentralDir())
            {
                string directoryName = Path.GetDirectoryName(Path.Combine(folder, _zfe.FilenameInZip));
                if (!Directory.Exists(directoryName))
                    Directory.CreateDirectory(directoryName);
                zipStorer.ExtractFile(_zfe, Path.Combine(folder, _zfe.FilenameInZip));
            }
            zipStorer.Close();
        }

        public static void SetDirectoryPermission(string dir)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(dir);
            SecurityIdentifier identity = new SecurityIdentifier(WellKnownSidType.WorldSid, (SecurityIdentifier)null);
            DirectorySecurity accessControl = directoryInfo.GetAccessControl();
            accessControl.AddAccessRule(new FileSystemAccessRule((IdentityReference)identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directoryInfo.SetAccessControl(accessControl);
        }

        public static void CreateFileShortcut(string file, string directory)
        {
            string shortcutAddress = Path.Combine(directory, Path.GetFileNameWithoutExtension(file) + ".lnk");

            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); //Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                var lnk = shell.CreateShortcut(shortcutAddress);
                try
                {
                    lnk.TargetPath = file;
                    lnk.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(lnk);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public partial class MainWindow : Window
    {
        private const uint MF_BYCOMMAND = 0;
        private const uint MF_GRAYED = 1;
        private const uint MF_ENABLED = 0;
        private const uint SC_CLOSE = 61536;
        private const int WM_SHOWWINDOW = 24;
        private bool install = true;
        private bool removeSettings;
        private string name = "WinLaunch";
        private string runApp = "WinLaunch.exe";
        private string temppath;
        private string installDir;
        private bool finishInstall;
        private bool errorInstall;
        private WebClient webClient = new WebClient();

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (!(PresentationSource.FromVisual((Visual)this) is HwndSource hwndSource))
                return;
            hwndSource.AddHook(new HwndSourceHook(this.hwndSourceHook));
        }

        private IntPtr hwndSourceHook(
          IntPtr hwnd,
          int msg,
          IntPtr wParam,
          IntPtr lParam,
          ref bool handled)
        {
            if (msg == 24)
            {
                IntPtr systemMenu = MainWindow.GetSystemMenu(hwnd, false);
                if (systemMenu != IntPtr.Zero)
                    MainWindow.EnableMenuItem(systemMenu, 61536U, 1U);
            }
            return IntPtr.Zero;
        }

        private void BeginInstallation()
        {
            this.SetHomeDirectory();
            this.temppath = Path.GetTempFileName();
            this.tbStatus.Text = "Starting download...";
            string downloadUrl = Installer.GetDownloadURL();
            if (downloadUrl == null)
                this.ErrorInstall("An error occured while retrieving the download url\r\nplease check your internet connection and try again.", "Close");
            else
                this.StartDownload(downloadUrl, this.temppath);
        }

        

        private void SetHomeDirectory() => Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));


        private void StartDownload(string url, string path) => this.webClient.DownloadFileAsync(new Uri(url), path);

        

        void webClient_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                ErrorInstall("An error occured while downloading the required files" + "\r\n" +
                            "please check your internet connection and try again.", "Close");

                return;
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Stopping WinLaunch...";
                    }));

                    Process[] p = Process.GetProcessesByName("WinLaunch");

                    if (p.Length > 0)
                    {
                        foreach (Process instance in p)
                        {
                            try
                            {
                                instance.Kill();
                            }
                            catch (Exception ex)
                            {
                                this.Dispatcher.Invoke(new Action(() =>
                                {
                                    ErrorInstall("Could not stop WinLaunch.exe!", "Close");
                                }));

                                return;
                            }
                        }
                    }

                    Thread.Sleep(500);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        pbProgress.IsIndeterminate = true;
                        tbStatus.Text = "Installing files...";
                    }));

                    //unzip to install directory
                    Installer.Unzip(temppath, installDir);
                    Thread.Sleep(1000);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Setting directory permissions...";
                    }));

                    //set directory permission
                    Installer.SetDirectoryPermission(installDir);
                    Thread.Sleep(500);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Cleaning up...";
                    }));

                    //delete temp file 
                    File.Delete(temppath);
                    Thread.Sleep(500);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Create shortcut";
                    }));

                    //create shortcut on desktop 
                    string WinLaunchStarterPath = System.IO.Path.Combine(installDir, "WinLaunch Starter.exe");
                    string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    Installer.CreateFileShortcut(WinLaunchStarterPath, desktopDir);
                    Thread.Sleep(500);

                    //set autostart
                    Autostart.SetAutoStart(this.name, System.IO.Path.Combine(installDir, runApp), " -hide");

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Create uninstaller";
                    }));

                    Assembly assembly = this.GetType().Assembly;
                    Installer.CreateUninstaller(assembly, this.installDir, this.name);
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ErrorInstall("An error occured during installation" + "\r\n" + "Error: " + ex.Message, "Close");
                    }));

                    return;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    tbStatus.Text = "Installation completed";
                    btnOk.Visibility = System.Windows.Visibility.Visible;
                    btnOk.Content = "Start";
                    finishInstall = true;

                    pbProgress.IsIndeterminate = false;
                    pbProgress.Value = 100.0;
                }));
            });
        }

        private void ErrorInstall(string message, string buttonText)
        {
            errorInstall = true;

            tbStatus.Text = message;
            pbProgress.Foreground = Brushes.Red;

            finishInstall = true;
            btnOk.Visibility = System.Windows.Visibility.Visible;
            btnOk.Content = buttonText;
        }

        void webClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            pbProgress.Value = e.ProgressPercentage;
            tbStatus.Text = String.Format("Downloading latest version ({0}%)", e.ProgressPercentage);
        }

        #region Uninstaller
        void RemoveUninstaller()
        {
            using (RegistryKey parent = Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true))
            {
                parent.DeleteSubKeyTree(name, false);
            }
        }

        private void RemoveInstallDirectory()
        {
            //Remove Directory
            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, true);
                Thread.Sleep(500);
            }
        }

        void BeginUninstallation()
        {
            pbProgress.IsIndeterminate = true;

            if (MessageBox.Show("Do you want to remove the settings and item configuration as well?", "", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                removeSettings = true;
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Stopping WinLaunch...";
                    }));

                    Process[] p = Process.GetProcessesByName("WinLaunch");

                    if (p.Length > 0)
                    {
                        foreach (Process instance in p)
                        {
                            try
                            {
                                instance.Kill();
                            }
                            catch (Exception ex)
                            {
                                this.Dispatcher.Invoke(new Action(() =>
                                {
                                    ErrorInstall("Could not stop WinLaunch.exe!", "Close");
                                }));

                                return;
                            }
                        }
                    }

                    Thread.Sleep(500);


                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Removing files...";
                    }));

                    while (true)
                    {
                        try
                        {
                            RemoveInstallDirectory();
                            break;
                        }
                        catch { }

                        Thread.Sleep(500);
                    }

                    //Remove shortcuts
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Removing shortcuts...";
                    }));

                    string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string lnk = Path.Combine(desktopDir, "WinLaunch Starter.lnk");

                    if (File.Exists(lnk))
                    {
                        File.Delete(lnk);
                    }

                    Thread.Sleep(500);

                    if (removeSettings)
                    {
                        //Remove settings
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            tbStatus.Text = "Removing settings...";
                        }));

                        string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        string WinLaunchAppdata = Path.Combine(appDataDir, "WinLaunch");

                        if (Directory.Exists(WinLaunchAppdata))
                        {
                            Directory.Delete(WinLaunchAppdata, true);
                            Thread.Sleep(500);
                        }
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Removing uninstaller...";
                    }));

                    try
                    {
                        RemoveUninstaller();
                        Thread.Sleep(500);
                    }
                    catch { }

                    //Done
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        tbStatus.Text = "Done";

                        btnOk.Visibility = System.Windows.Visibility.Visible;
                        btnOk.Content = "Finish";
                        finishInstall = true;

                        pbProgress.IsIndeterminate = false;
                        pbProgress.Value = 100.0;
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ErrorInstall("An error occured during uninstallation" + "\r\n" + "Error: " + ex.Message, "Close");
                    }));
                }
            });
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            webClient.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36");
            webClient.DownloadProgressChanged += webClient_DownloadProgressChanged;
            webClient.DownloadFileCompleted += webClient_DownloadFileCompleted;

            //check if WinLaunch is already installed
            installDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), name);
            string WinLaunchExe = System.IO.Path.Combine(installDir, runApp);

            if (File.Exists(WinLaunchExe))
            {
                try
                {
                    Thread.Sleep(500);

                    //we are an uninstaller
                    //check to make sure we arent blocking the directory
                    string currentDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

                    if (currentDirectory == installDir)
                    {
                        //copy ourself to the temp directory and restart
                        string installerLocation = System.IO.Path.GetTempFileName();

                        //remove dummy file
                        File.Delete(installerLocation);

                        File.Copy(System.Reflection.Assembly.GetEntryAssembly().Location, installerLocation);

                        Process process = new Process();
                        process.StartInfo.FileName = installerLocation;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.WorkingDirectory = Path.GetDirectoryName(installerLocation);
                        process.Start();

                        Close();
                        Environment.Exit(0);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Something went horribly wrong!\n" + ex.Message);
                    Close();
                    return;
                }


                //WinLaunch installed
                tbStatus.Text = "Select the operation you wish to perform";

                btnRepair.Visibility = System.Windows.Visibility.Visible;
                btnRepair.Content = "Repair";
                btnOk.Content = "Uninstall";
                btnCancel.Content = "Cancel";
                
                install = false;
            }
            else
            {
                //installer
                tbStatus.Text = "Welcome to the WinLaunch installer\npress 'Install' to continue with the installation";

                btnRepair.Visibility = System.Windows.Visibility.Collapsed;
                btnOk.Content = "Install";
                btnCancel.Content = "Cancel";
            }
        }

        private void btnRepair_Click(object sender, RoutedEventArgs e)
        {
            install = true;

            btnOk.Visibility = System.Windows.Visibility.Collapsed;
            btnRepair.Visibility = System.Windows.Visibility.Collapsed;
            btnCancel.Visibility = System.Windows.Visibility.Collapsed;

            BeginInstallation();
        }


        void btnOk_Click(object sender, RoutedEventArgs e)
        {
            if (finishInstall)
            {
                if (install && !errorInstall)
                {
                    try
                    {
                        //use explorer to launch app without elevation
                        Process.Start("explorer", System.IO.Path.Combine(installDir, runApp));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not start WinLaunch");
                    }
                }

                this.Close();
            }
            else
            {
                btnOk.Visibility = System.Windows.Visibility.Collapsed;
                btnRepair.Visibility = System.Windows.Visibility.Collapsed;
                btnCancel.Visibility = System.Windows.Visibility.Collapsed;

                if (install)
                {
                    BeginInstallation();
                }
                else
                {
                    BeginUninstallation();
                }
            }
        }

        void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Do you really want to cancel?", "Cancel", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                this.Close();
            }
        }
    }
}
