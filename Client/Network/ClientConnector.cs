using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VisualControl.Shared.Protocol;

namespace VisualControl.Client.Network
{
    /// <summary>
    /// 客户端TCP连接器 - 连接到控制端
    /// </summary>
    public class ClientConnector : IDisposable
    {
        public bool IsConnected => _client?.Connected == true;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<MessageType, byte[]>? MessageReceived;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _receiveThread;
        private Thread? _heartbeatThread;
        private bool _disposed;
        private byte[]? _aesKey;
        private readonly string _serverIp;
        private readonly int _serverPort;

        public ClientConnector(string serverIp, int serverPort)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
        }

        public void SetAesKey(byte[] key) => _aesKey = key;

        /// <summary>
        /// 连接到服务端
        /// </summary>
        public bool Connect()
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(IPAddress.Parse(_serverIp), _serverPort);
                _stream = _client.GetStream();

                // 发送注册消息
                var reg = new RegisterMessage
                {
                    DeviceName = Environment.MachineName,
                    IpAddress = GetLocalIp(),
                    MacAddress = "",
                    OsVersion = Environment.OSVersion.ToString(),
                    ScreenWidth = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920,
                    ScreenHeight = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080
                };
                Send(MessageType.Register, reg.Serialize());

                // 启动接收线程
                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                _receiveThread.Start();

                // 启动心跳线程
                _heartbeatThread = new Thread(HeartbeatLoop) { IsBackground = true };
                _heartbeatThread.Start();

                Connected?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 自动重连
        /// </summary>
        public void ConnectWithRetry(int intervalMs = 5000)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (!_disposed)
                {
                    if (!IsConnected)
                    {
                        if (Connect())
                        {
                            // 注册后等待确认
                            Thread.Sleep(1000);
                        }
                    }
                    Thread.Sleep(intervalMs);
                }
            });
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        public void Send(MessageType type, byte[] payload)
        {
            try
            {
                if (_stream == null || !_client?.Connected == true) return;

                byte[] data = payload;
                if (_aesKey != null && type != MessageType.Heartbeat && type != MessageType.HeartbeatAck
                    && type != MessageType.Register)
                {
                    data = Crypto.AesHelper.Encrypt(payload, _aesKey);
                }

                var frame = FrameProtocol.Encode(type, data);
                _stream.Write(frame, 0, frame.Length);
                _stream.Flush();
            }
            catch
            {
                Disconnect();
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                while (!_disposed && _client?.Connected == true && _stream != null)
                {
                    if (FrameProtocol.TryReadFrame(_stream, out MessageType type, out byte[] payload, 30000))
                    {
                        byte[] data = payload;
                        if (_aesKey != null && type != MessageType.Heartbeat && type != MessageType.HeartbeatAck
                            && type != MessageType.RegisterAck)
                        {
                            data = Crypto.AesHelper.Decrypt(payload, _aesKey);
                        }

                        // 处理注册确认，提取AES密钥
                        if (type == MessageType.RegisterAck)
                        {
                            // 连接已确认
                        }

                        MessageReceived?.Invoke(type, data);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch
            {
                // 连接异常
            }
            finally
            {
                Disconnect();
            }
        }

        private void HeartbeatLoop()
        {
            while (!_disposed && _client?.Connected == true)
            {
                try
                {
                    Send(MessageType.Heartbeat, Array.Empty<byte>());
                    Thread.Sleep(10000); // 10秒心跳
                }
                catch
                {
                    break;
                }
            }
        }

        public void Disconnect()
        {
            if (_disposed) return;
            try { _client?.Close(); } catch { }
            Disconnected?.Invoke();
        }

        private string GetLocalIp()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }
}
