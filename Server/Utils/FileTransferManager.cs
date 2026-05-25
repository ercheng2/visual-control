using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VisualControl.Shared.Protocol;

namespace VisualControl.Server.Utils
{
    /// <summary>
    /// 文件传输管理器 - 处理文件分片发送
    /// </summary>
    public class FileTransferManager
    {
        private const int CHUNK_SIZE = 64 * 1024; // 64KB

        public class TransferTask
        {
            public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..8];
            public string FilePath { get; set; } = "";
            public string FileName { get; set; } = "";
            public long FileSize { get; set; }
            public string Md5 { get; set; } = "";
            public int TotalChunks { get; set; }
            public string TargetDeviceId { get; set; } = "";
            public DateTime StartTime { get; set; } = DateTime.Now;
            public double Progress { get; set; }
        }

        private readonly Concurrent.ConcurrentDictionary<string, TransferTask> _tasks = new();

        public event Action<TransferTask, double>? ProgressChanged;
        public event Action<TransferTask, bool>? TransferCompleted;

        /// <summary>
        /// 计算文件MD5
        /// </summary>
        public static string ComputeMd5(string filePath)
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(filePath);
            var hash = md5.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// 开始推送文件到设备
        /// </summary>
        public TransferTask StartPush(string filePath, string targetDeviceId, Network.ServerListener server)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            var fileInfo = new FileInfo(filePath);
            var task = new TransferTask
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                Md5 = ComputeMd5(filePath),
                TotalChunks = (int)Math.Ceiling((double)fileInfo.Length / CHUNK_SIZE),
                TargetDeviceId = targetDeviceId
            };

            _tasks[task.TaskId] = task;

            // 发送文件头
            var pushMsg = new FilePushMessage
            {
                FileName = task.FileName,
                FileSize = task.FileSize,
                FileMd5 = task.Md5,
                ChunkSize = CHUNK_SIZE
            };
            server.SendToDevice(targetDeviceId, MessageType.FilePush, pushMsg.Serialize());

            // 启动分片发送线程
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] buffer = new byte[CHUNK_SIZE];
                    using var fs = File.OpenRead(filePath);
                    int chunkIndex = 0;
                    int bytesRead;

                    while ((bytesRead = fs.Read(buffer, 0, CHUNK_SIZE)) > 0)
                    {
                        var chunkData = bytesRead < CHUNK_SIZE
                            ? buffer.Take(bytesRead).ToArray()
                            : buffer;

                        var chunkMsg = new FileChunkMessage
                        {
                            ChunkIndex = chunkIndex,
                            Data = chunkData
                        };

                        server.SendToDevice(targetDeviceId, MessageType.FileChunk, chunkMsg.Serialize());

                        chunkIndex++;
                        task.Progress = (double)chunkIndex / task.TotalChunks * 100;
                        ProgressChanged?.Invoke(task, task.Progress);

                        // 稍微延迟避免堵塞
                        System.Threading.Thread.Sleep(5);
                    }

                    // 发送完成通知
                    var completeMsg = new FileCompleteMessage
                    {
                        FileName = task.FileName,
                        Success = true
                    };
                    server.SendToDevice(targetDeviceId, MessageType.FileComplete, completeMsg.Serialize());

                    TransferCompleted?.Invoke(task, true);
                }
                catch (Exception ex)
                {
                    var completeMsg = new FileCompleteMessage
                    {
                        FileName = task.FileName,
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                    server.SendToDevice(targetDeviceId, MessageType.FileComplete, completeMsg.Serialize());
                    TransferCompleted?.Invoke(task, false);
                }
                finally
                {
                    _tasks.TryRemove(task.TaskId, out _);
                }
            });

            return task;
        }
    }
}
