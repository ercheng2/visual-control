using VisualControl.Shared.Crypto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VisualControl.Server.Models;
using VisualControl.Shared.Protocol;

namespace VisualControl.Server.Network
{
    /// <summary>
    /// 客户端连接处理
    /// </summary>
    public class ClientConnection : IDisposable
    {
        public string DeviceId { get; private set; } = "";
        public DeviceInfo? DeviceInfo { get; private set; }
        public bool IsConnected => _client?.Connected == true;

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Thread _receiveThread;
        private readonly ServerListener _server;
        private bool _disposed;
        private byte[]? _aesKey;

        public event Action<ClientConnection, MessageType, byte[]>? MessageReceived;
        public event Action<ClientConnection>? Disconnected;

        public ClientConnection(TcpClient client, ServerListener server)
        {
            _client = client;
            _server = server;
            _stream = client.GetStream();
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        }

        public void Start()
        {
            _receiveThread.Start();
        }

        public void SetAesKey(byte[] key)
        {
            _aesKey = key;
        }

        public void SetDeviceInfo(RegisterMessage reg, string deviceId)
        {
            DeviceId = deviceId;
            DeviceInfo = new DeviceInfo
            {
                DeviceId = deviceId,
                DeviceName = reg.DeviceName,
                IpAddress = reg.IpAddress,
                MacAddress = reg.MacAddress,
                OsVersion = reg.OsVersion,
                ScreenWidth = reg.ScreenWidth,
                ScreenHeight = reg.ScreenHeight,
                Status = DeviceStatus.Online,
                ConnectTime = DateTime.Now,
                LastHeartbeat = DateTime.Now
            };
        }

        public void UpdateHeartbeat()
        {
            if (DeviceInfo != null)
                DeviceInfo.LastHeartbeat = DateTime.Now;
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        public void Send(MessageType type, byte[] payload)
        {
            try
            {
                if (!_client.Connected) return;

                // 如果有AES密钥且非心跳消息，加密payload
                byte[] data = payload;
                if (_aesKey != null && type != MessageType.Heartbeat && type != MessageType.HeartbeatAck)
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
                while (!_disposed && _client.Connected)
                {
                    if (FrameProtocol.TryReadFrame(_stream, out MessageType type, out byte[] payload, 30000))
                    {
                        // 解密
                        byte[] data = payload;
                        if (_aesKey != null && type != MessageType.Heartbeat && type != MessageType.HeartbeatAck
                            && type != MessageType.Register)
                        {
                            data = Crypto.AesHelper.Decrypt(payload, _aesKey);
                        }

                        MessageReceived?.Invoke(this, type, data);
                    }
                    else
                    {
                        // 读取失败，连接断开
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

        public void Disconnect()
        {
            if (_disposed) return;
            _disposed = true;
            try { _client.Close(); } catch { }
            Disconnected?.Invoke(this);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
