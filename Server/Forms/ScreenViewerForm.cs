using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using VisualControl.Server.Network;
using VisualControl.Shared.Protocol;

namespace VisualControl.Server.Forms
{
    /// <summary>
    /// 远程桌面查看器
    /// </summary>
    public class ScreenViewerForm : Form
    {
        public string DeviceId { get; }
        private readonly ServerListener _server;
        private PictureBox _screenBox;
        private Bitmap? _fullFrame;
        private readonly object _frameLock = new object();
        private int _screenWidth;
        private int _screenHeight;
        private bool _mouseDown;

        public ScreenViewerForm(string deviceId, string deviceName, ServerListener server)
        {
            DeviceId = deviceId;
            _server = server;

            Text = $"远程桌面 - {deviceName}";
            Size = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(30, 30, 35);
            KeyPreview = true;

            InitializeComponents();
            SetupInputEvents();
        }

        private void InitializeComponents()
        {
            // 工具栏
            var toolbar = new ToolStrip
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(45, 48, 58),
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new ControlToolStripRenderer()
            };

            var btnFit = new ToolStripButton("适应窗口", null, (s, e) => FitScreen())
            { ForeColor = Color.White };
            var btnFull = new ToolStripButton("1:1 原始", null, (s, e) => OriginalSize())
            { ForeColor = Color.White };
            var btnFs = new ToolStripButton("全屏", null, (s, e) => ToggleFullscreen())
            { ForeColor = Color.White };

            toolbar.Items.AddRange(new ToolStripItem[] { btnFit, btnFull, new ToolStripSeparator(), btnFs });

            // 画面容器
            var container = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(20, 20, 25)
            };

            _screenBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 25),
                InitialImage = null,
                ErrorImage = null
            };

            container.Controls.Add(_screenBox);
            Controls.AddRange(new Control[] { container, toolbar });

            // ESC退出全屏
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && FormBorderStyle == FormBorderStyle.None)
                    ToggleFullscreen();
            };
        }

        private void SetupInputEvents()
        {
            // 鼠标事件
            _screenBox.MouseDown += (s, e) =>
            {
                _mouseDown = true;
                SendMouseEvent(e, MouseAction.LeftDown);
            };
            _screenBox.MouseUp += (s, e) =>
            {
                _mouseDown = false;
                SendMouseEvent(e, MouseAction.LeftUp);
            };
            _screenBox.MouseMove += (s, e) =>
            {
                if (_mouseDown) SendMouseEvent(e, MouseAction.Move);
            };
            _screenBox.MouseWheel += (s, e) =>
            {
                var msg = new MouseEventMessage
                {
                    Action = MouseAction.Wheel,
                    X = ScaleX(e.X),
                    Y = ScaleY(e.Y),
                    Delta = e.Delta
                };
                _server.SendToDevice(DeviceId, MessageType.MouseEvent, msg.Serialize());
            };

            // 键盘事件
            KeyDown += (s, e) =>
            {
                var msg = new KeyboardEventMessage
                {
                    Action = KeyboardAction.KeyDown,
                    VirtualKey = (int)e.KeyCode,
                    ScanCode = e.KeyValue
                };
                _server.SendToDevice(DeviceId, MessageType.KeyboardEvent, msg.Serialize());
            };
            KeyUp += (s, e) =>
            {
                var msg = new KeyboardEventMessage
                {
                    Action = KeyboardAction.KeyUp,
                    VirtualKey = (int)e.KeyCode,
                    ScanCode = e.KeyValue
                };
                _server.SendToDevice(DeviceId, MessageType.KeyboardEvent, msg.Serialize());
            };
        }

        private void SendMouseEvent(MouseEventArgs e, MouseAction action)
        {
            var msg = new MouseEventMessage
            {
                Action = action,
                X = ScaleX(e.X),
                Y = ScaleY(e.Y)
            };
            _server.SendToDevice(DeviceId, MessageType.MouseEvent, msg.Serialize());
        }

        /// <summary>
        /// 将PictureBox坐标映射到远程屏幕坐标
        /// </summary>
        private int ScaleX(int x)
        {
            if (_screenWidth <= 0 || _screenBox.Width <= 0) return x;
            return (int)((double)x / _screenBox.Width * _screenWidth);
        }

        private int ScaleY(int y)
        {
            if (_screenHeight <= 0 || _screenBox.Height <= 0) return y;
            return (int)((double)y / _screenBox.Height * _screenHeight);
        }

        /// <summary>
        /// 接收屏幕帧数据
        /// </summary>
        public void OnScreenFrame(byte[] data)
        {
            try
            {
                var frame = ScreenFrameMessage.Deserialize(data);
                using var ms = new MemoryStream(frame.ImageData);
                var partialBmp = new Bitmap(ms);

                lock (_frameLock)
                {
                    if (frame.IsFullFrame || _fullFrame == null)
                    {
                        _fullFrame?.Dispose();
                        _fullFrame = partialBmp;
                        _screenWidth = frame.Width;
                        _screenHeight = frame.Height;
                    }
                    else
                    {
                        // 增量更新：将部分画面绘制到全帧上
                        using var g = Graphics.FromImage(_fullFrame);
                        g.DrawImage(partialBmp, frame.X, frame.Y, frame.Width, frame.Height);
                        partialBmp.Dispose();
                    }

                    var display = (Bitmap)_fullFrame.Clone();
                    BeginInvoke((Action)(() =>
                    {
                        var old = _screenBox.Image;
                        _screenBox.Image = display;
                        old?.Dispose();

                        if (_screenBox.SizeMode == PictureBoxSizeMode.Zoom && _fullFrame != null)
                        {
                            FitScreen();
                        }
                    }));
                }
            }
            catch { }
        }

        private void FitScreen()
        {
            _screenBox.SizeMode = PictureBoxSizeMode.Zoom;
            _screenBox.Dock = DockStyle.Fill;
        }

        private void OriginalSize()
        {
            _screenBox.SizeMode = PictureBoxSizeMode.AutoSize;
            _screenBox.Dock = DockStyle.None;
            if (_fullFrame != null)
                _screenBox.Size = _fullFrame.Size;
        }

        private void ToggleFullscreen()
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _fullFrame?.Dispose();
            _screenBox.Image?.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal class ControlToolStripRenderer : ToolStripProfessionalRenderer
    {
        public ControlToolStripRenderer() : base(new ControlColorTable()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    internal class ControlColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Color.FromArgb(45, 48, 58);
        public override Color ToolStripGradientEnd => Color.FromArgb(45, 48, 58);
        public override Color ToolStripGradientMiddle => Color.FromArgb(45, 48, 58);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(65, 68, 80);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(65, 68, 80);
    }
}
