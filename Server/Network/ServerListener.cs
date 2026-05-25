using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VisualControl.Server.Models;
using VisualControl.Shared.Crypto;
using VisualControl.Shared.Protocol;

namespace VisualControl.Server.Network
{
    public class ServerListener : IDisposable
    {
        public const int DEFAULT_PORT = 9600;
        public event Action<ClientConnection>? ClientConnected;
        public event Action<ClientConnection>? ClientDisconnected;
        public event Action<ClientConnection, MessageType, byte[]>? MessageReceived;

        private TcpListener? _listener;
        private Thread? _acceptThread;
        private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
        private bool _disposed;
        private int _deviceCounter;

        public int Port { get; private set; } = DEFAULT_PORT;
        public bool IsRunning { get; private set; }
        public Dictionary<string, ClientConnection> Connections => new Dictionary<string, ClientConnection>(_connections);

        public void Start(int port = DEFAULT_PORT)
        {
            if (IsRunning) return;
            Port = port;
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            IsRunning = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            _acceptThread.Start();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _listener?.Stop();
            foreach (var conn in _connections.Values) conn.Dispose();
            _connections.Clear();
        }

        private void AcceptLoop()
        {
            try
            {
                while (IsRunning && !_disposed)
                {
                    var client = _listener!.AcceptTcpClient();
                    var connection = new ClientConnection(client, this);
                    var deviceId = $"DEV_{Interlocked.Increment(ref _deviceCounter):D4}";
                    connection.SetAesKey(AesHelper.GenerateKey());

                    connection.MessageReceived += (conn, type, data) =>
                    {
                        if (type == MessageType.Register)
                        {
                            var reg = RegisterMessage.Deserialize(data);
                            conn.SetDeviceInfo(reg, deviceId);
                            _connections[deviceId] = conn;
                            var ack = new RegisterAckMessage { Success = true, ServerVersion = "1.0", HeartbeatInterval = 10, Permission = PermissionLevel.Admin };
                            conn.Send(MessageType.RegisterAck, ack.Serialize());
                            ClientConnected?.Invoke(conn);
                        }
                        else if (type == MessageType.Heartbeat)
                        {
                            conn.UpdateHeartbeat();
                            conn.Send(MessageType.HeartbeatAck, Array.Empty<byte>());
                        }
                        else
                        {
                            MessageReceived?.Invoke(conn, type, data);
                        }
                    };
                    connection.Disconnected += conn =>
                    {
                        _connections.TryRemove(conn.DeviceId, out _);
                        ClientDisconnected?.Invoke(conn);
                    };
                    connection.Start();
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        public void SendToDevice(string deviceId, MessageType type, byte[] payload)
        {
            if (_connections.TryGetValue(deviceId, out var conn)) conn.Send(type, payload);
        }

        public void Broadcast(MessageType type, byte[] payload)
        {
            foreach (var conn in _connections.Values)
            { try { conn.Send(type, payload); } catch { } }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
