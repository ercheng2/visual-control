using System;
using System.IO;
using System.Net.Sockets;

namespace VisualControl.Shared.Protocol
{
    /// <summary>
    /// 二进制帧协议
    /// 帧格式: [Magic 2B] [Type 2B] [Length 4B] [Payload Length B] [CRC16 2B]
    /// </summary>
    public static class FrameProtocol
    {
        public const ushort MAGIC = 0x5643; // "VC" in ASCII
        public const int HEADER_SIZE = 10;   // 2+2+4+2
        public const int MAX_PAYLOAD_SIZE = 10 * 1024 * 1024; // 10MB

        /// <summary>
        /// 封装消息为二进制帧
        /// </summary>
        public static byte[] Encode(MessageType type, byte[] payload)
        {
            if (payload == null) payload = Array.Empty<byte>();
            if (payload.Length > MAX_PAYLOAD_SIZE)
                throw new ArgumentException($"Payload too large: {payload.Length}");

            using var ms = new MemoryStream(HEADER_SIZE + payload.Length);
            using var writer = new BinaryWriter(ms);

            // Magic
            writer.Write(BitConverter.GetBytes(MAGIC));
            // Type
            writer.Write(BitConverter.GetBytes((ushort)type));
            // Length
            writer.Write(BitConverter.GetBytes(payload.Length));
            // Payload
            writer.Write(payload);
            // CRC16
            ushort crc = Crc16.Compute(payload);
            writer.Write(BitConverter.GetBytes(crc));

            return ms.ToArray();
        }

        /// <summary>
        /// 从网络流读取一帧
        /// </summary>
        public static bool TryReadFrame(NetworkStream stream, out MessageType type, out byte[] payload, int timeoutMs = 30000)
        {
            type = default;
            payload = Array.Empty<byte>();

            try
            {
                // 读取头部
                var header = new byte[8]; // magic(2) + type(2) + length(4)
                if (!ReadExact(stream, header, 0, 8, timeoutMs))
                    return false;

                int offset = 0;
                // 校验Magic
                ushort magic = BitConverter.ToUInt16(header, offset);
                if (magic != MAGIC) return false;
                offset += 2;

                // 消息类型
                type = (MessageType)BitConverter.ToUInt16(header, offset);
                offset += 2;

                // 载荷长度
                int length = BitConverter.ToInt32(header, offset);
                if (length < 0 || length > MAX_PAYLOAD_SIZE) return false;

                // 读取载荷
                payload = new byte[length];
                if (length > 0 && !ReadExact(stream, payload, 0, length, timeoutMs))
                    return false;

                // 读取CRC
                var crcBuf = new byte[2];
                if (!ReadExact(stream, crcBuf, 0, 2, timeoutMs))
                    return false;

                ushort receivedCrc = BitConverter.ToUInt16(crcBuf, 0);
                ushort computedCrc = Crc16.Compute(payload);
                if (receivedCrc != computedCrc) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 精确读取指定字节数
        /// </summary>
        private static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count, int timeoutMs)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, offset + read, count - read);
                if (n == 0) return false;
                read += n;
            }
            return true;
        }
    }

    /// <summary>
    /// CRC16校验 (CCITT)
    /// </summary>
    public static class Crc16
    {
        private static readonly ushort[] Table = BuildTable();

        private static ushort[] BuildTable()
        {
            var table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                ushort crc = (ushort)(i << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc = (ushort)(crc << 1);
                }
                table[i] = crc;
            }
            return table;
        }

        public static ushort Compute(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
                crc = (ushort)((crc << 8) ^ Table[((crc >> 8) ^ b) & 0xFF]);
            return crc;
        }
    }
}
