using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VisualControl.Client.Network;
using VisualControl.Client.Screen;
using VisualControl.Client.Input;
using VisualControl.Shared.Protocol;

namespace VisualControl.Client.UI
{
    /// <summary>
    /// 托盘应用 - 客户端后台静默运行
    /// </summary>
    public class TrayApp : IDisposable
    {
        private readonly ClientConnector _connector;
        private readonly ScreenCapture _screenCapture;
        private readonly string _serverIp;
        private readonly int _serverPort;
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private int _screenWidth, _screenHeight;

        // 文件接收状态
        private string? _receivingFileName;
        private long _receivingFileSize;
        private string? _receivingFileMd5;
        private int _receivingChunkSize;
        private int _receivedChunks;
        private int _totalChunks;
        private FileStream? _receivingStream;

        public TrayApp(string serverIp, int serverPort, bool autoStart)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
            _connector = new ClientConnector(serverIp, serverPort);
            _screenCapture = new ScreenCapture();
            _screenWidth = Screen.PrimaryScreen?.Bounds.Width ?? 1920;
            _screenHeight = Screen.PrimaryScreen?.Bounds.Height ?? 1080;

            InitializeTray();
            SetupMessageHandlers();

            _connector.Connected += () => UpdateIcon("在线", ToolTipIcon.Info);
            _connector.Disconnected += () =>
            {
                UpdateIcon("离线-重连中", ToolTipIcon.Warning);
                _screenCapture.Stop();
            };

            if (autoStart) Connect();
        }

        private void InitializeTray()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("连接服务器", null, (s, e) => Connect());
            _contextMenu.Items.Add("断开连接", null, (s, e) => _connector.Disconnect());
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("服务器设置...", null, OnServerSettings);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("退出", null, (s, e) => { Dispose(); Application.Exit(); });

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateTrayIcon(Color.FromArgb(76, 175, 80)),
                Text = "可视化中控客户端",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _notifyIcon.DoubleClick += (s, e) => OnServerSettings(s, e);
        }

        private void SetupMessageHandlers()
        {
            _connector.MessageReceived += (type, data) =>
            {
                switch (type)
                {
                    case MessageType.ScreenRequest:
                        var req = ScreenRequestMessage.Deserialize(data);
                        if (req.Start) _screenCapture.Start(req.Quality, req.MaxFps);
                        else _screenCapture.Stop();
                        break;

                    case MessageType.ScreenStop:
                        _screenCapture.Stop();
                        break;

                    case MessageType.MouseEvent:
                        var mouse = MouseEventMessage.Deserialize(data);
                        InputInjector.InjectMouse(mouse, _screenWidth, _screenHeight);
                        break;

                    case MessageType.KeyboardEvent:
                        var key = KeyboardEventMessage.Deserialize(data);
                        InputInjector.InjectKeyboard(key);
                        break;

                    case MessageType.DeviceCommand:
                        var cmd = DeviceCommandMessage.Deserialize(data);
                        ExecuteRemoteCommand(cmd);
                        break;

                    case MessageType.FilePush:
                        var push = FilePushMessage.Deserialize(data);
                        StartReceivingFile(push);
                        break;

                    case MessageType.FileChunk:
                        var chunk = FileChunkMessage.Deserialize(data);
                        ReceiveFileChunk(chunk);
                        break;

                    case MessageType.FileComplete:
                        var comp = FileCompleteMessage.Deserialize(data);
                        CompleteFileReception(comp);
                        break;
                }
            };

            // 屏幕帧 → 发送给控制端
            _screenCapture.FrameCaptured += frame =>
            {
                _connector.Send(MessageType.ScreenFrame, frame.Serialize());
            };
        }

        private void Connect()
        {
            UpdateIcon("连接中...", ToolTipIcon.Info);
            _connector.ConnectWithRetry();
        }

        private void OnServerSettings(object? sender, EventArgs e)
        {
            using var dlg = new Form
            {
                Text = "服务器设置",
                Size = new Size(350, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false
            };

            var lblIp = new Label { Text = "服务器IP:", Left = 20, Top = 25, AutoSize = true };
            var txtIp = new TextBox { Text = _serverIp, Left = 90, Top = 22, Width = 200 };
            var lblPort = new Label { Text = "端口:", Left = 20, Top = 60, AutoSize = true };
            var txtPort = new TextBox { Text = _serverPort.ToString(), Left = 90, Top = 57, Width = 200 };

            var chkAuto = new CheckBox { Text = "自动连接", Left = 20, Top = 95, AutoSize = true };

            var btnConnect = new Button
            {
                Text = "连接",
                Left = 90, Top = 125, Width = 80,
                BackColor = Color.FromArgb(70, 130, 220),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnConnect.Click += (s2, e2) =>
            {
                var ip = txtIp.Text.Trim();
                var port = int.TryParse(txtPort.Text.Trim(), out var p) ? p : 9600;
                _connector.Disconnect();
                dlg.Close();
                Connect();
            };

            dlg.Controls.AddRange(new Control[] { lblIp, txtIp, lblPort, txtPort, chkAuto, btnConnect });
            dlg.ShowDialog();
        }

        private void ExecuteRemoteCommand(DeviceCommandMessage cmd)
        {
            switch (cmd.Command)
            {
                case RemoteCommand.LockScreen:
                    Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                    break;
                case RemoteCommand.Restart:
                    Process.Start("shutdown.exe", "/r /t 5 /c \"中控系统远程重启\"");
                    break;
                case RemoteCommand.Shutdown:
                    Process.Start("shutdown.exe", "/s /t 5 /c \"中控系统远程关机\"");
                    break;
                case RemoteCommand.OpenFile:
                    if (!string.IsNullOrEmpty(cmd.Parameter) && File.Exists(cmd.Parameter))
                        Process.Start(cmd.Parameter);
                    break;
            }
        }

        #region 文件接收

        private void StartReceivingFile(FilePushMessage push)
        {
            try
            {
                var saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "中控接收");
                Directory.CreateDirectory(saveDir);
                var savePath = Path.Combine(saveDir, push.FileName);

                _receivingFileName = push.FileName;
                _receivingFileSize = push.FileSize;
                _receivingFileMd5 = push.FileMd5;
                _receivingChunkSize = push.ChunkSize;
                _totalChunks = (int)Math.Ceiling((double)push.FileSize / push.ChunkSize);
                _receivedChunks = 0;

                _receivingStream?.Dispose();
                _receivingStream = new FileStream(savePath, FileMode.Create, FileAccess.Write);

                // 发送ACK确认
                var ack = new FileAckMessage { ChunkIndex = -1, Success = true };
                _connector.Send(MessageType.FileAck, ack.Serialize());

                ShowBalloon($"正在接收: {push.FileName}");
            }
            catch (Exception ex)
            {
                var ack = new FileAckMessage { ChunkIndex = -1, Success = false };
                _connector.Send(MessageType.FileAck, ack.Serialize());
            }
        }

        private void ReceiveFileChunk(FileChunkMessage chunk)
        {
            try
            {
                if (_receivingStream == null) return;

                _receivingStream.Write(chunk.Data, 0, chunk.Data.Length);
                _receivedChunks++;

                // 发送进度
                var progress = new FileProgressMessage
                {
                    FileName = _receivingFileName ?? "",
                    ReceivedChunks = _receivedChunks,
                    TotalChunks = _totalChunks,
                    ReceivedBytes = _receivingStream.Position,
                    TotalBytes = _receivingFileSize
                };
                _connector.Send(MessageType.FileProgress, progress.Serialize());

                // 发送ACK
                var ack = new FileAckMessage { ChunkIndex = chunk.ChunkIndex, Success = true };
                _connector.Send(MessageType.FileAck, ack.Serialize());
            }
            catch
            {
                var ack = new FileAckMessage { ChunkIndex = chunk.ChunkIndex, Success = false };
                _connector.Send(MessageType.FileAck, ack.Serialize());
            }
        }

        private void CompleteFileReception(FileCompleteMessage comp)
        {
            _receivingStream?.Dispose();
            _receivingStream = null;

            if (comp.Success)
            {
                ShowBalloon($"文件接收完成: {_receivingFileName}");
                // 自动弹出打开提示
                if (MessageBox.Show($"收到文件: {_receivingFileName}\n是否打开?", "文件接收完成",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    var saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "中控接收");
                    var path = Path.Combine(saveDir, _receivingFileName ?? "");
                    if (File.Exists(path)) Process.Start(path);
                }
            }
            else
            {
                ShowBalloon($"文件接收失败: {_receivingFileName}");
            }

            _receivingFileName = null;
        }

        #endregion

        private void UpdateIcon(string text, ToolTipIcon icon)
        {
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = icon;
            var color = text.Contains("在线") ? Color.FromArgb(76, 175, 80) :
                        text.Contains("离线") ? Color.FromArgb(244, 67, 54) :
                        Color.FromArgb(255, 152, 0);
            _notifyIcon.Icon = CreateTrayIcon(color);
        }

        private void ShowBalloon(string text)
        {
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(3000);
        }

        private Icon CreateTrayIcon(Color color)
        {
            var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(new SolidBrush(color), 4, 4, 24, 24);
            g.FillRectangle(new SolidBrush(Color.White), 12, 10, 8, 12);
            g.FillRectangle(new SolidBrush(Color.White), 10, 12, 12, 8);
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _screenCapture.Dispose();
            _connector.Dispose();
            _receivingStream?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
