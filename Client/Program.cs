using System;
using System.Windows.Forms;

namespace VisualControl.Client
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show($"UI异常: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show($"致命异常: {ex?.Message}\n\n{ex?.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool autoStart = Array.IndexOf(args, "--auto") >= 0;
            string serverIp = "127.0.0.1";
            int serverPort = 9600;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--server") serverIp = args[i + 1];
                if (args[i] == "--port") int.TryParse(args[i + 1], out serverPort);
            }

            using var trayApp = new UI.TrayApp(serverIp, serverPort, autoStart);
            Application.Run();
        }
    }
}
