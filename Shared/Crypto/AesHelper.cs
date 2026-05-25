using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VisualControl.Shared.Crypto
{
    /// <summary>
    /// AES-256-CBC 加密解密
    /// 密钥和IV由通信双方协商(首次注册时交换)
    /// </summary>
    public static class AesHelper
    {
        private const int KeySize = 256;
        private const int BlockSize = 128;
        private const int IvSize = 16;

        /// <summary>
        /// 生成随机AES密钥
        /// </summary>
        public static byte[] GenerateKey()
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.GenerateKey();
            return aes.Key;
        }

        /// <summary>
        /// 生成随机IV
        /// </summary>
        public static byte[] GenerateIv()
        {
            using var aes = Aes.Create();
            aes.GenerateIV();
            return aes.IV;
        }

        /// <summary>
        /// AES-256-CBC加密
        /// 输出格式: [IV 16B] [EncryptedData]
        /// </summary>
        public static byte[] Encrypt(byte[] plainData, byte[] key)
        {
            if (plainData == null || plainData.Length == 0)
                return Array.Empty<byte>();

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            // 写入IV
            ms.Write(aes.IV, 0, aes.IV.Length);
            // 写入加密数据
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainData, 0, plainData.Length);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }

        /// <summary>
        /// AES-256-CBC解密
        /// 输入格式: [IV 16B] [EncryptedData]
        /// </summary>
        public static byte[] Decrypt(byte[] cipherData, byte[] key)
        {
            if (cipherData == null || cipherData.Length <= IvSize)
                return Array.Empty<byte>();

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;

            // 提取IV
            var iv = new byte[IvSize];
            Buffer.BlockCopy(cipherData, 0, iv, 0, IvSize);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
            {
                cs.Write(cipherData, IvSize, cipherData.Length - IvSize);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }
    }
}
