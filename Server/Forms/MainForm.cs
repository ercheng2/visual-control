using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VisualControl.Server.Models;
using VisualControl.Server.Network;
using VisualControl.Server.Utils;
using VisualControl.Shared.Protocol;

namespace VisualControl.Server.Forms
{
    public class MainForm : Form
    {
        private ServerListener _server = new ServerListener();
        private FileTransferManager _fileTransfer = new FileTransferManager();

        private ListView _deviceList;
        private ImageList _deviceIcons;
        private SplitContainer _mainSplit;
        private TextBox _logBox;
        private ToolStrip _toolStrip;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ComboBox _groupCombo;
        private ProgressBar _transferProgress;
        private Label _transferLabel;
        private Panel _dropZone;

        public MainForm()
        {
            InitializeComponents();
            SetupEvents();
            StartServer();
        }

        private void InitializeComponents()
        {
            Text = "分布式可视化中控系统 - 控制端";
            Size = new Size(1200, 750);
            MinimumSize = new Size(900, 550);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 245, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            // ===== 工具栏 =====
            _toolStrip = new ToolStrip
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(55, 60, 70),
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(5, 4, 5, 4),
                Renderer = new DarkToolStripRenderer()
            };

            var btnToggle = new ToolStripButton("■ 停止服务", null, OnToggleServer)
            { ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold) };
            var btnRefresh = new ToolStripButton("🔄 刷新", null, (s, e) => RefreshDeviceList())
            { ForeColor = Color.White };
            var btnGroup = new ToolStripButton("📁 分组", null, OnGroupManage)
            { ForeColor = Color.White };
            var btnLock = new ToolStripButton("🔒 锁屏", null, OnRemoteCommand)
            { ForeColor = Color.White, Tag = RemoteCommand.LockScreen };
            var btnRestart = new ToolStripButton("🔁 重启", null, OnRemoteCommand)
            { ForeColor = Color.White, Tag = RemoteCommand.Restart };
            var btnShutdown = new ToolStripButton("⏻ 关机", null, OnRemoteCommand)
            { ForeColor = Color.White, Tag = RemoteCommand.Shutdown };
            var btnViewer = new ToolStripButton("🖥 远程桌面", null, OnOpenViewer)
            { ForeColor = Color.White };

            _toolStrip.Items.AddRange(new ToolStripItem[]
            {
                btnToggle, new ToolStripSeparator(), btnRefresh, btnGroup,
                new ToolStripSeparator(), btnLock, btnRestart, btnShutdown,
                new ToolStripSeparator(), btnViewer
            });

            // ===== 分割面板 =====
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Panel1MinSize = 200,
                Panel2MinSize = 300,
                BackColor = Color.FromArgb(220, 225, 235),
                SplitterWidth = 3
            };

            // ===== 左侧 - 设备列表 =====
            var leftPanel = _mainSplit.Panel1;
            leftPanel.BackColor = Color.White;
            leftPanel.Padding = new Padding(6);

            var listTitle = new Label
            {
                Text = "  设备列表",
                Dock = DockStyle.Top,
                Height = 32,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(50, 55, 65)
            };

            _deviceIcons = new ImageList { ImageSize = new Size(24, 24) };
            _deviceIcons.Images.Add("online", CreateCircleIcon(Color.FromArgb(76, 175, 80)));
            _deviceIcons.Images.Add("offline", CreateCircleIcon(Color.FromArgb(160, 160, 160)));
            _deviceIcons.Images.Add("busy", CreateCircleIcon(Color.FromArgb(255, 152, 0)));

            _deviceList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                GridLines = false,
                SmallImageList = _deviceIcons,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Microsoft YaHei UI", 9f),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            _deviceList.Columns.Add("设备名称", 160);
            _deviceList.Columns.Add("IP地址", 120);
            _deviceList.Columns.Add("状态", 55);
            _deviceList.Columns.Add("分组", 80);

            var groupBar = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Color.White };
            var groupLabel = new Label { Text = "分组:", Left = 8, Top = 6, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9f) };
            _groupCombo = new ComboBox
            {
                Left = 50, Top = 4, Width = 210,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _groupCombo.Items.AddRange(new object[] { "全部", "默认分组" });
            _groupCombo.SelectedIndex = 0;
            groupBar.Controls.AddRange(new Control[] { groupLabel, _groupCombo });

            leftPanel.Controls.AddRange(new Control[] { _deviceList, groupBar, listTitle });

            // ===== 右侧 - 操作区 =====
            var rightPanel = _mainSplit.Panel2;
            rightPanel.BackColor = Color.FromArgb(248, 249, 252);

            // 拖拽区
            _dropZone = new Panel
            {
                Dock = DockStyle.Top,
                Height = 120,
                BackColor = Color.FromArgb(235, 242, 255),
                BorderStyle = BorderStyle.FixedSingle,
                AllowDrop = true
            };
            var dropLabel = new Label
            {
                Text = "⬇  拖拽文件到此处，分发到选中设备",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 11f),
                ForeColor = Color.FromArgb(90, 110, 170)
            };
            _dropZone.Controls.Add(dropLabel);

            // 传输进度
            var progressPanel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.White };
            _transferLabel = new Label { Text = "就绪", Left = 10, Top = 4, AutoSize = true, Font = new Font("Microsoft YaHei UI", 8.5f) };
            _transferProgress = new ProgressBar
            {
                Left = 10, Top = 24, Width = rightPanel.Width - 20,
                Height = 18, Style = ProgressBarStyle.Continuous,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            progressPanel.Controls.AddRange(new Control[] { _transferLabel, _transferProgress });

            // 日志区
            _logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(28, 30, 34),
                ForeColor = Color.FromArgb(180, 210, 180),
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical
            };

            rightPanel.Controls.AddRange(new Control[] { _logBox, progressPanel, _dropZone });

            // ===== 状态栏 =====
            _statusStrip = new StatusStrip { BackColor = Color.FromArgb(55, 60, 70) };
            _statusLabel = new ToolStripStatusLabel
            { ForeColor = Color.FromArgb(200, 210, 230), Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusStrip.Items.Add(_statusLabel);

            // ===== 组装 =====
            Controls.AddRange(new Control[] { _mainSplit, _toolStrip, _statusStrip });
        }

        private void SetupEvents()
        {
            _server.ClientConnected += conn =>
            {
                BeginInvoke((Action)(() =>
                {
                    Log($"✔ 设备上线: {conn.DeviceInfo?.DeviceName} ({conn.DeviceInfo?.IpAddress})");
                    RefreshDeviceList();
                    UpdateStatus();
                }));
            };

            _server.ClientDisconnected += conn =>
            {
                BeginInvoke((Action)(() =>
                {
                    Log($"✘ 设备离线: {conn.DeviceInfo?.DeviceName} ({conn.DeviceInfo?.IpAddress})");
                    RefreshDeviceList();
                    UpdateStatus();
                }));
            };

            _server.MessageReceived += (conn, type, data) =>
            {
                BeginInvoke((Action)(() =>
                {
                    switch (type)
                    {
                        case MessageType.FileProgress:
                            var prog = FileProgressMessage.Deserialize(data);
                            var pct = prog.TotalChunks > 0 ? (int)((double)prog.ReceivedChunks / prog.TotalChunks * 100) : 0;
                            _transferProgress.Value = Math.Min(pct, 100);
                            _transferLabel.Text = $"传输: {prog.FileName} {pct}%";
                            break;

                        case MessageType.FileComplete:
                            var comp = FileCompleteMessage.Deserialize(data);
                            _transferLabel.Text = comp.Success
                                ? $"✓ {comp.FileName} 传输完成"
                                : $"✗ {comp.FileName} 失败: {comp.ErrorMessage}";
                            _transferProgress.Value = comp.Success ? 100 : 0;
                            break;

                        case MessageType.ScreenFrame:
                            var viewer = Application.OpenForms.OfType<ScreenViewerForm>()
                                .FirstOrDefault(f => f.DeviceId == conn.DeviceId);
                            viewer?.OnScreenFrame(data);
                            break;
                    }
                }));
            };

            // 拖拽事件
            _dropZone.DragEnter += (s, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                    e.Effect = DragDropEffects.Copy;
            };

            _dropZone.DragDrop += (s, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return;
                if (_deviceList.SelectedItems.Count == 0)
                {
                    MessageBox.Show("请先在左侧选择目标设备", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0) return;

                var deviceIds = _deviceList.SelectedItems.Cast<ListViewItem>()
                    .Select(i => i.Tag as string).Where(id => id != null).ToList();

                foreach (var file in files)
                {
                    if (!File.Exists(file)) continue;
                    foreach (var devId in deviceIds)
                    {
                        try
                        {
                            _fileTransfer.StartPush(file, devId!, _server);
                            Log($"📤 推送: {Path.GetFileName(file)} → {devId}");
                        }
                        catch (Exception ex)
                        {
                            Log($"✗ 推送失败: {ex.Message}");
                        }
                    }
                }
            };

            // 双击设备打开远程桌面
            _deviceList.DoubleClick += (s, e) =>
            {
                if (_deviceList.SelectedItems.Count == 0) return;
                var devId = _deviceList.SelectedItems[0].Tag as string;
                if (devId != null) OpenScreenViewer(devId);
            };

            // 分组筛选
            _groupCombo.SelectedIndexChanged += (s, e) => RefreshDeviceList();

            // 窗体关闭
            FormClosing += (s, e) => _server.Stop();
            Load += (s, e) =>
            {
                if (_mainSplit.Width > 500)
                    _mainSplit.SplitterDistance = 300;
            };

            // 文件传输进度
            _fileTransfer.ProgressChanged += (task, progress) =>
            {
                BeginInvoke((Action)(() =>
                {
                    _transferProgress.Value = Math.Min((int)progress, 100);
                    _transferLabel.Text = $"发送: {task.FileName} {progress:F0}%";
                }));
            };
        }

        private void StartServer()
        {
            try
            {
                _server.Start();
                Log($"✔ 服务已启动，监听端口 {_server.Port}");
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Log($"✗ 启动失败: {ex.Message}");
                MessageBox.Show($"启动服务失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnToggleServer(object sender, EventArgs e)
        {
            if (_server.IsRunning)
            {
                _server.Stop();
                Log("■ 服务已停止");
            }
            else
            {
                StartServer();
            }
            UpdateStatus();
        }

        private void OnRemoteCommand(object sender, EventArgs e)
        {
            if (_deviceList.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择设备", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var cmd = (RemoteCommand)((ToolStripItem)sender).Tag;
            var msg = new DeviceCommandMessage { Command = cmd };

            foreach (ListViewItem item in _deviceList.SelectedItems)
            {
                var devId = item.Tag as string;
                if (devId != null)
                {
                    _server.SendToDevice(devId, MessageType.DeviceCommand, msg.Serialize());
                    Log($"📡 指令[{cmd}] → {item.Text}");
                }
            }
        }

        private void OnGroupManage(object sender, EventArgs e)
        {
            using var dlg = new Form
            {
                Text = "分组管理",
                Size = new Size(450, 350),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                ColumnCount = 3
            };
            grid.Columns[0].HeaderText = "设备名称";
            grid.Columns[1].HeaderText = "IP地址";
            grid.Columns[2].HeaderText = "分组";

            foreach (var conn in _server.Connections.Values)
            {
                if (conn.DeviceInfo == null) continue;
                grid.Rows.Add(conn.DeviceInfo.DeviceName, conn.DeviceInfo.IpAddress, conn.DeviceInfo.GroupName);
            }

            var okBtn = new Button
            {
                Text = "应用",
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(70, 130, 220),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            okBtn.Click += (s2, e2) =>
            {
                // 更新分组
                for (int i = 0; i < grid.Rows.Count; i++)
                {
                    var devName = grid.Rows[i].Cells[0].Value?.ToString();
                    var group = grid.Rows[i].Cells[2].Value?.ToString() ?? "默认分组";
                    var conn = _server.Connections.Values
                        .FirstOrDefault(c => c.DeviceInfo?.DeviceName == devName);
                    if (conn?.DeviceInfo != null) conn.DeviceInfo.GroupName = group;
                }
                RefreshDeviceList();
                dlg.DialogResult = DialogResult.OK;
            };

            dlg.Controls.AddRange(new Control[] { grid, okBtn });
            dlg.ShowDialog(this);
        }

        private void OnOpenViewer(object sender, EventArgs e)
        {
            if (_deviceList.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择设备", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var devId = _deviceList.SelectedItems[0].Tag as string;
            if (devId != null) OpenScreenViewer(devId);
        }

        private void OpenScreenViewer(string deviceId)
        {
            var existing = Application.OpenForms.OfType<ScreenViewerForm>()
                .FirstOrDefault(f => f.DeviceId == deviceId);
            if (existing != null)
            {
                existing.Activate();
                return;
            }

            var conn = _server.Connections.Values.FirstOrDefault(c => c.DeviceId == deviceId);
            var name = conn?.DeviceInfo?.DeviceName ?? deviceId;

            // 发送屏幕请求
            var req = new ScreenRequestMessage { Start = true, Quality = 85, MaxFps = 15 };
            _server.SendToDevice(deviceId, MessageType.ScreenRequest, req.Serialize());

            var viewer = new ScreenViewerForm(deviceId, name, _server);
            viewer.FormClosing += (s, e) =>
            {
                var stop = new ScreenRequestMessage { Start = false };
                _server.SendToDevice(deviceId, MessageType.ScreenStop, stop.Serialize());
            };
            viewer.Show(this);
        }

        private void RefreshDeviceList()
        {
            _deviceList.BeginUpdate();
            _deviceList.Items.Clear();

            var filter = _groupCombo.SelectedItem as string;
            var connections = _server.Connections.Values.ToList();

            foreach (var conn in connections)
            {
                if (conn.DeviceInfo == null) continue;
                var dev = conn.DeviceInfo;

                if (filter != "全部" && dev.GroupName != filter) continue;

                var iconKey = dev.Status switch
                {
                    DeviceStatus.Online => "online",
                    DeviceStatus.Busy => "busy",
                    _ => "offline"
                };

                var item = new ListViewItem(dev.DeviceName, iconKey);
                item.SubItems.Add(dev.IpAddress);
                item.SubItems.Add(dev.Status.ToString());
                item.SubItems.Add(dev.GroupName);
                item.Tag = dev.DeviceId;

                if (_deviceList.Items.Count % 2 == 1)
                    item.BackColor = Color.FromArgb(245, 247, 255);

                _deviceList.Items.Add(item);
            }

            _deviceList.EndUpdate();
        }

        private void UpdateStatus()
        {
            var total = _server.Connections.Count;
            var online = _server.Connections.Values.Count(c => c.DeviceInfo?.Status == DeviceStatus.Online);
            _statusLabel.Text = $"  服务: {(_server.IsRunning ? "运行中" : "已停止")}  │  在线: {online}  │  连接: {total}  │  端口: {_server.Port}";
        }

        private void Log(string msg)
        {
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        }

        private Bitmap CreateCircleIcon(Color color)
        {
            var bmp = new Bitmap(24, 24);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(new SolidBrush(color), 3, 3, 18, 18);
            g.FillEllipse(new SolidBrush(Color.FromArgb(200, 255, 255, 255)), 8, 7, 8, 8);
            return bmp;
        }
    }

    internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(80, 85, 95), 1);
            if (e.Vertical)
                e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left + 2, 4, e.Item.ContentRectangle.Left + 2, e.Item.ContentRectangle.Bottom - 4);
            else
                base.OnRenderSeparator(e);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Color.FromArgb(55, 60, 70);
        public override Color MenuStripGradientEnd => Color.FromArgb(55, 60, 70);
        public override Color ToolStripGradientBegin => Color.FromArgb(55, 60, 70);
        public override Color ToolStripGradientEnd => Color.FromArgb(55, 60, 70);
        public override Color ToolStripGradientMiddle => Color.FromArgb(55, 60, 70);
        public override Color ButtonSelectedHighlight => Color.FromArgb(75, 80, 95);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(75, 80, 95);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(75, 80, 95);
    }
}
