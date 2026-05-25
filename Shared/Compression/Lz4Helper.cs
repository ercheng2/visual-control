using System;
using K4os.Compression.LZ4;

namespace VisualControl.Shared.Compression
{
    /// <summary>
    /// LZ4压缩解压工具
    /// </summary>
    public static class Lz4Helper
    {
        /// <summary>
        /// LZ4压缩
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            var maxCompressed = LZ4Codec.MaximumOutputSize(data.Length);
            var compressed = new byte[maxCompressed];
            var compressedSize = LZ4Codec.Encode(data, 0, data.Length, compressed, 0, compressed.Length);

            if (compressedSize <= 0)
                return data; // 压缩失败返回原数据

            // 裁剪到实际压缩大小
            var result = new byte[compressedSize];
            Buffer.BlockCopy(compressed, 0, result, 0, compressedSize);
            return result;
        }

        /// <summary>
        /// LZ4解压
        /// </summary>
        public static byte[] Decompress(byte[] compressedData, int originalSize)
        {
            if (compressedData == null || compressedData.Length == 0)
                return Array.Empty<byte>();

            var decompressed = new byte[originalSize];
            var decoded = LZ4Codec.Decode(compressedData, 0, compressedData.Length, decompressed, 0, originalSize);

            if (decoded != originalSize)
            {
                // 大小不匹配，返回尽力解压的数据
                var result = new byte[decoded];
                Buffer.BlockCopy(decompressed, 0, result, 0, decoded);
                return result;
            }

            return decompressed;
        }
    }
}
