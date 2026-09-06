using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GridBoard
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool isFirstInstance = GlobalAtomGuard.TryAcquire();
            if (isFirstInstance)
            {
                // 第一个实例，正常启动
                base.OnStartup(e);
                MainWindow = new MainWindow();
                MainWindow.Show();
            }
            else
            {
                ActivateMainWindow();
                Shutdown();
            }
        }

        private void ActivateMainWindow()
        {
            string windowTitle = "GridBoard"; 
            IntPtr hWnd = FindWindow(null, windowTitle);
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            else
            {
                Debug.WriteLine("Main window not found.");
                GlobalAtomGuard.Clean();
                Process.Start(Process.GetCurrentProcess().MainModule.FileName);
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            GlobalAtomGuard.Release();
        }
    }
}
