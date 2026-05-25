using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace VisualControl.Shared.Protocol
{
    #region 连接管理消息

    public class RegisterMessage
    {
        public string DeviceName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static RegisterMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<RegisterMessage>(Encoding.UTF8.GetString(data))
                   ?? new RegisterMessage();
        }
    }

    public class RegisterAckMessage
    {
        public bool Success { get; set; }
        public string ServerVersion { get; set; } = "1.0";
        public int HeartbeatInterval { get; set; } = 10; // 秒
        public PermissionLevel Permission { get; set; } = PermissionLevel.Operator;

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static RegisterAckMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<RegisterAckMessage>(Encoding.UTF8.GetString(data))
                   ?? new RegisterAckMessage();
        }
    }

    #endregion

    #region 设备管理消息

    public class DeviceInfoMessage
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public DeviceStatus Status { get; set; }
        public string GroupName { get; set; } = "";
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static DeviceInfoMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<DeviceInfoMessage>(Encoding.UTF8.GetString(data))
                   ?? new DeviceInfoMessage();
        }
    }

    public class DeviceCommandMessage
    {
        public RemoteCommand Command { get; set; }
        public string Parameter { get; set; } = "";

        public byte[] Serialize() => new[] { (byte)Command }.Concat(Encoding.UTF8.GetBytes(Parameter)).ToArray();

        public static DeviceCommandMessage Deserialize(byte[] data)
        {
            if (data.Length == 0) return new DeviceCommandMessage();
            return new DeviceCommandMessage
            {
                Command = (RemoteCommand)data[0],
                Parameter = data.Length > 1 ? Encoding.UTF8.GetString(data, 1, data.Length - 1) : ""
            };
        }
    }

    public class DeviceGroupMessage
    {
        public string Action { get; set; } = ""; // "add", "remove", "rename"
        public string GroupName { get; set; } = "";
        public string DeviceId { get; set; } = "";

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static DeviceGroupMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<DeviceGroupMessage>(Encoding.UTF8.GetString(data))
                   ?? new DeviceGroupMessage();
        }
    }

    #endregion

    #region 远程桌面消息

    public class ScreenRequestMessage
    {
        public bool Start { get; set; }  // true=开始, false=停止
        public int Quality { get; set; } = 85;
        public int MaxFps { get; set; } = 15;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Start);
            w.Write(Quality);
            w.Write(MaxFps);
            return ms.ToArray();
        }

        public static ScreenRequestMessage Deserialize(byte[] data)
        {
            if (data.Length < 9) return new ScreenRequestMessage();
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            return new ScreenRequestMessage
            {
                Start = r.ReadBoolean(),
                Quality = r.ReadInt32(),
                MaxFps = r.ReadInt32()
            };
        }
    }

    public class ScreenFrameMessage
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsFullFrame { get; set; }
        public int JpegQuality { get; set; }
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(X);
            w.Write(Y);
            w.Write(Width);
            w.Write(Height);
            w.Write(IsFullFrame);
            w.Write(JpegQuality);
            w.Write(ImageData.Length);
            w.Write(ImageData);
            return ms.ToArray();
        }

        public static ScreenFrameMessage Deserialize(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);
                return new ScreenFrameMessage
                {
                    X = r.ReadInt32(),
                    Y = r.ReadInt32(),
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    IsFullFrame = r.ReadBoolean(),
                    JpegQuality = r.ReadInt32(),
                    ImageData = r.ReadBytes(r.ReadInt32())
                };
            }
            catch
            {
                return new ScreenFrameMessage();
            }
        }
    }

    #endregion

    #region 远程输入消息

    public class MouseEventMessage
    {
        public MouseAction Action { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Delta { get; set; } // 滚轮增量

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)Action);
            w.Write(X);
            w.Write(Y);
            w.Write(Delta);
            return ms.ToArray();
        }

        public static MouseEventMessage Deserialize(byte[] data)
        {
            if (data.Length < 13) return new MouseEventMessage();
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            return new MouseEventMessage
            {
                Action = (MouseAction)r.ReadByte(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Delta = r.ReadInt32()
            };
        }
    }

    public class KeyboardEventMessage
    {
        public KeyboardAction Action { get; set; }
        public int VirtualKey { get; set; }
        public int ScanCode { get; set; }

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)Action);
            w.Write(VirtualKey);
            w.Write(ScanCode);
            return ms.ToArray();
        }

        public static KeyboardEventMessage Deserialize(byte[] data)
        {
            if (data.Length < 9) return new KeyboardEventMessage();
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            return new KeyboardEventMessage
            {
                Action = (KeyboardAction)r.ReadByte(),
                VirtualKey = r.ReadInt32(),
                ScanCode = r.ReadInt32()
            };
        }
    }

    #endregion

    #region 文件传输消息

    public class FilePushMessage
    {
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public string FileMd5 { get; set; } = "";
        public int ChunkSize { get; set; } = 64 * 1024; // 64KB分片
        public string TargetPath { get; set; } = ""; // 客户端保存路径

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static FilePushMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<FilePushMessage>(Encoding.UTF8.GetString(data))
                   ?? new FilePushMessage();
        }
    }

    public class FileChunkMessage
    {
        public int ChunkIndex { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(ChunkIndex);
            w.Write(Data.Length);
            w.Write(Data);
            return ms.ToArray();
        }

        public static FileChunkMessage Deserialize(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);
                return new FileChunkMessage
                {
                    ChunkIndex = r.ReadInt32(),
                    Data = r.ReadBytes(r.ReadInt32())
                };
            }
            catch
            {
                return new FileChunkMessage();
            }
        }
    }

    public class FileAckMessage
    {
        public int ChunkIndex { get; set; }
        public bool Success { get; set; }

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(ChunkIndex);
            w.Write(Success);
            return ms.ToArray();
        }

        public static FileAckMessage Deserialize(byte[] data)
        {
            if (data.Length < 5) return new FileAckMessage();
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            return new FileAckMessage
            {
                ChunkIndex = r.ReadInt32(),
                Success = r.ReadBoolean()
            };
        }
    }

    public class FileProgressMessage
    {
        public string FileName { get; set; } = "";
        public int ReceivedChunks { get; set; }
        public int TotalChunks { get; set; }
        public long ReceivedBytes { get; set; }
        public long TotalBytes { get; set; }

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static FileProgressMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<FileProgressMessage>(Encoding.UTF8.GetString(data))
                   ?? new FileProgressMessage();
        }
    }

    public class FileCompleteMessage
    {
        public string FileName { get; set; } = "";
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static FileCompleteMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<FileCompleteMessage>(Encoding.UTF8.GetString(data))
                   ?? new FileCompleteMessage();
        }
    }

    #endregion

    #region 错误消息

    public class ErrorMessage
    {
        public string Message { get; set; } = "";
        public int Code { get; set; }

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }

        public static ErrorMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<ErrorMessage>(Encoding.UTF8.GetString(data))
                   ?? new ErrorMessage();
        }
    }

    #endregion
}
