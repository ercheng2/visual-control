using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using VisualControl.Shared.Compression;
using VisualControl.Shared.Protocol;

namespace VisualControl.Client.Screen
{
    public class ScreenCapture : IDisposable
    {
        private Bitmap? _previousFrame;
        private int _width, _height;
        private bool _capturing;
        private Thread? _captureThread;
        private int _quality = 85, _maxFps = 15;
        private readonly object _lock = new();

        public event Action<ScreenFrameMessage>? FrameCaptured;
        public bool IsCapturing => _capturing;

        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int xD, int yD, int w, int h, IntPtr hdcSrc, int xS, int yS, int rop);
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        const int SRCCOPY = 0x00CC0020;

        public void Start(int quality = 85, int maxFps = 15)
        {
            if (_capturing) return;
            _quality = quality; _maxFps = maxFps;
            _width = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            _height = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
            _capturing = true;
            _captureThread = new Thread(CaptureLoop) { IsBackground = true };
            _captureThread.Start();
        }

        public void Stop()
        {
            _capturing = false;
            _captureThread?.Join(2000);
            lock (_lock) { _previousFrame?.Dispose(); _previousFrame = null; }
        }

        public void UpdateConfig(int quality, int maxFps) { _quality = quality; _maxFps = maxFps; }

        private void CaptureLoop()
        {
            var interval = 1000 / Math.Max(_maxFps, 1);
            bool sendFull = true;
            while (_capturing)
            {
                try
                {
                    var t0 = DateTime.UtcNow;
                    using var frame = CaptureScreen();
                    if (frame == null) { Thread.Sleep(100); continue; }

                    ScreenFrameMessage msg;
                    lock (_lock)
                    {
                        if (_previousFrame == null || sendFull)
                        {
                            msg = BuildFrame(frame, 0, 0, _width, _height, true);
                            _previousFrame?.Dispose();
                            _previousFrame = (Bitmap)frame.Clone();
                            sendFull = false;
                        }
                        else
                        {
                            var diff = DetectChange(frame, _previousFrame);
                            if (diff == null) { Thread.Sleep(interval); continue; }
                            msg = BuildFrame(frame, diff.Value.X, diff.Value.Y, diff.Value.Width, diff.Value.Height, false);
                            _previousFrame?.Dispose();
                            _previousFrame = (Bitmap)frame.Clone();
                        }
                    }
                    FrameCaptured?.Invoke(msg);

                    var elapsed = (DateTime.UtcNow - t0).TotalMilliseconds;
                    if (interval - elapsed > 0) Thread.Sleep((int)(interval - elapsed));
                    if (DateTime.UtcNow.Second % 5 == 0) sendFull = true;
                }
                catch { Thread.Sleep(500); }
            }
        }

        private Bitmap? CaptureScreen()
        {
            try
            {
                var bmp = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);
                using var g = Graphics.FromImage(bmp);
                var hdcDest = g.GetHdc();
                var hdcSrc = GetWindowDC(GetDesktopWindow());
                BitBlt(hdcDest, 0, 0, _width, _height, hdcSrc, 0, 0, SRCCOPY);
                ReleaseDC(GetDesktopWindow(), hdcSrc);
                g.ReleaseHdc(hdcDest);
                return bmp;
            }
            catch { return null; }
        }

        private Rectangle? DetectChange(Bitmap cur, Bitmap prev)
        {
            int block = 32;
            int minX = _width, minY = _height, maxX = 0, maxY = 0;
            bool changed = false;
            var cd = cur.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            var pd = prev.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                unsafe
                {
                    byte* cp = (byte*)cd.Scan0, pp = (byte*)pd.Scan0;
                    int stride = cd.Stride;
                    for (int by = 0; by < _height; by += block)
                        for (int bx = 0; bx < _width; bx += block)
                        {
                            int px = Math.Min(bx + block / 2, _width - 1);
                            int py = Math.Min(by + block / 2, _height - 1);
                            int off = py * stride + px * 4;
                            if (cp[off] != pp[off] || cp[off+1] != pp[off+1] || cp[off+2] != pp[off+2])
                            {
                                changed = true;
                                if (bx < minX) minX = bx; if (by < minY) minY = by;
                                if (bx + block > maxX) maxX = bx + block; if (by + block > maxY) maxY = by + block;
                            }
                        }
                }
            }
            finally { cur.UnlockBits(cd); prev.UnlockBits(pd); }
            if (!changed) return null;
            return new Rectangle(Math.Max(0,minX-4), Math.Max(0,minY-4), Math.Min(_width,maxX+4)-Math.Max(0,minX-4), Math.Min(_height,maxY+4)-Math.Max(0,minY-4));
        }

        private ScreenFrameMessage BuildFrame(Bitmap src, int x, int y, int w, int h, bool isFull)
        {
            Bitmap? region = (x==0&&y==0&&w==_width&&h==_height) ? src : src.Clone(new Rectangle(x,y,w,h), PixelFormat.Format32bppRgb);
            using var ms = new MemoryStream();
            var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)_quality);
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            region.Save(ms, codec, ep);
            if (!ReferenceEquals(region, src)) region.Dispose();
            var compressed = Lz4Helper.Compress(ms.ToArray());
            return new ScreenFrameMessage { X=x, Y=y, Width=w, Height=h, IsFullFrame=isFull, JpegQuality=_quality, ImageData=compressed };
        }

        public void Dispose() => Stop();
    }
}
