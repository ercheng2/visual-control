using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using VisualControl.Shared.Protocol;

namespace VisualControl.Server.Utils
{
    public class FileTransferManager
    {
        private const int CHUNK_SIZE = 64 * 1024;
        private readonly ConcurrentDictionary<string, TransferTask> _tasks = new();

        public event Action<TransferTask, double>? ProgressChanged;
        public event Action<TransferTask, bool>? TransferCompleted;

        public class TransferTask
        {
            public string TaskId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
            public string FilePath { get; set; } = "";
            public string FileName { get; set; } = "";
            public long FileSize { get; set; }
            public string Md5 { get; set; } = "";
            public int TotalChunks { get; set; }
            public string TargetDeviceId { get; set; } = "";
            public double Progress { get; set; }
        }

        public static string ComputeMd5(string filePath)
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(filePath);
            var hash = md5.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public TransferTask StartPush(string filePath, string targetDeviceId, Network.ServerListener server)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            var fi = new FileInfo(filePath);
            var task = new TransferTask
            {
                FilePath = filePath, FileName = fi.Name, FileSize = fi.Length,
                Md5 = ComputeMd5(filePath), TotalChunks = (int)Math.Ceiling((double)fi.Length / CHUNK_SIZE),
                TargetDeviceId = targetDeviceId
            };
            _tasks[task.TaskId] = task;

            var pushMsg = new FilePushMessage { FileName = task.FileName, FileSize = task.FileSize, FileMd5 = task.Md5, ChunkSize = CHUNK_SIZE };
            server.SendToDevice(targetDeviceId, MessageType.FilePush, pushMsg.Serialize());

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] buffer = new byte[CHUNK_SIZE];
                    using var fs = File.OpenRead(filePath);
                    int idx = 0, read;
                    while ((read = fs.Read(buffer, 0, CHUNK_SIZE)) > 0)
                    {
                        var chunk = read < CHUNK_SIZE ? buffer.Take(read).ToArray() : buffer;
                        var msg = new FileChunkMessage { ChunkIndex = idx, Data = chunk };
                        server.SendToDevice(targetDeviceId, MessageType.FileChunk, msg.Serialize());
                        idx++;
                        task.Progress = (double)idx / task.TotalChunks * 100;
                        ProgressChanged?.Invoke(task, task.Progress);
                        System.Threading.Thread.Sleep(5);
                    }
                    var comp = new FileCompleteMessage { FileName = task.FileName, Success = true };
                    server.SendToDevice(targetDeviceId, MessageType.FileComplete, comp.Serialize());
                    TransferCompleted?.Invoke(task, true);
                }
                catch (Exception ex)
                {
                    var comp = new FileCompleteMessage { FileName = task.FileName, Success = false, ErrorMessage = ex.Message };
                    server.SendToDevice(targetDeviceId, MessageType.FileComplete, comp.Serialize());
                    TransferCompleted?.Invoke(task, false);
                }
                finally { TransferTask? removed; _tasks.TryRemove(task.TaskId, out removed); }
            });
            return task;
        }
    }
}
