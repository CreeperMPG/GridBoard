using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
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
        private static FileStream _lockStream;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        [STAThread] // WPF 必须的 STA 线程
        public static void Main()
        {
            // 1. 单实例检测，必须在最前面
            if (!TryAcquireSingleInstanceLock())
            {
                ActivateMainWindow();
                return; // 直接退出，不进入 WPF 消息循环
            }

            // 2. 创建并运行 WPF 应用
            var app = new App();
            app.InitializeComponent(); // 加载 App.xaml 资源、StartupUri
            app.Run();
        }

        private static bool TryAcquireSingleInstanceLock()
        {
            string lockPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GridBoard",
                "single_instance.lock");

            Directory.CreateDirectory(Path.GetDirectoryName(lockPath));

            try
            {
                _lockStream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None); // 独占打开，原子操作
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public App()
        {
            Exit += (s, e) => _lockStream?.Dispose();
        }

        private static void ActivateMainWindow()
        {
            string windowTitle = "GridBoard"; 
            IntPtr hWnd = FindWindow(null, windowTitle);
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
    }
}
