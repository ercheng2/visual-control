using System;
using System.Windows.Forms;

namespace VisualControl.Client
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool autoStart = Array.IndexOf(args, "--auto") >= 0;
            string serverIp = "127.0.0.1";
            int serverPort = 9600;

            // 解析命令行参数
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
