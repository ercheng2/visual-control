using System.Windows.Forms;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VisualControl.Shared.Crypto;
using VisualControl.Shared.Protocol;

namespace VisualControl.Client.Network
{
    public class ClientConnector : IDisposable
    {
        public bool IsConnected => _client?.Connected == true;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<MessageType, byte[]>? MessageReceived;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _receiveThread, _heartbeatThread;
        private bool _disposed;
        private byte[]? _aesKey;
        private string _serverIp;
        private int _serverPort;

        public ClientConnector(string serverIp, int serverPort) { _serverIp = serverIp; _serverPort = serverPort; }
        public void UpdateServer(string serverIp, int serverPort) { _serverIp = serverIp; _serverPort = serverPort; }
        public void SetAesKey(byte[] key) => _aesKey = key;

        public bool Connect()
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(IPAddress.Parse(_serverIp), _serverPort);
                _stream = _client.GetStream();

                var reg = new RegisterMessage
                {
                    DeviceName = Environment.MachineName,
                    IpAddress = GetLocalIp(),
                    OsVersion = Environment.OSVersion.ToString(),
                    ScreenWidth = SystemInformation.PrimaryMonitorSize.Width,
                    ScreenHeight = SystemInformation.PrimaryMonitorSize.Height
                };
                Send(MessageType.Register, reg.Serialize());

                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                _receiveThread.Start();
                _heartbeatThread = new Thread(HeartbeatLoop) { IsBackground = true };
                _heartbeatThread.Start();
                Connected?.Invoke();
                return true;
            }
            catch { return false; }
        }

        public void ConnectWithRetry(int intervalMs = 5000)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                while (!_disposed)
                {
                    if (!IsConnected) Connect();
                    Thread.Sleep(intervalMs);
                }
            });
        }

        public void Send(MessageType type, byte[] payload)
        {
            try
            {
                if (_stream == null || _client?.Connected != true) return;
                byte[] data = payload;
                if (_aesKey != null && type != MessageType.Heartbeat && type != MessageType.HeartbeatAck && type != MessageType.Register)
                    data = AesHelper.Encrypt(payload, _aesKey);
                var frame = FrameProtocol.Encode(type, data);
                _stream.Write(frame, 0, frame.Length);
                _stream.Flush();
            }
            catch { Disconnect(); }
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
                        if (_aesKey != null && type != MessageType.Heartbeat && type != MessageType.HeartbeatAck && type != MessageType.RegisterAck)
                            data = AesHelper.Decrypt(payload, _aesKey);
                        MessageReceived?.Invoke(type, data);
                    }
                    else break;
                }
            }
            catch { }
            finally { Disconnect(); }
        }

        private void HeartbeatLoop()
        {
            while (!_disposed && _client?.Connected == true)
            {
                try { Send(MessageType.Heartbeat, Array.Empty<byte>()); Thread.Sleep(10000); }
                catch { break; }
            }
        }

        public void Disconnect() { if (_disposed) return; try { _client?.Close(); } catch { } Disconnected?.Invoke(); }

        private string GetLocalIp()
        {
            try { return Dns.GetHostEntry(Dns.GetHostName()).AddressList.First(ip => ip.AddressFamily == AddressFamily.InterNetwork).ToString(); }
            catch { return "127.0.0.1"; }
        }

        public void Dispose() { if (_disposed) return; _disposed = true; Disconnect(); }
    }
}
