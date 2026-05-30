using System;
using System.Runtime.InteropServices;
using VisualControl.Shared.Protocol;

namespace VisualControl.Client.Input
{
    /// <summary>
    /// 远程输入注入 - 将控制端的鼠标/键盘事件注入本机
    /// </summary>
    public static class InputInjector
    {
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);

        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_WHEEL = 0x0800;
        const uint KEYEVENTF_KEYUP = 0x0002;

        public static void InjectMouse(MouseEventMessage msg, int screenWidth, int screenHeight)
        {
            int x = msg.X, y = msg.Y;
            // 坐标缩放
            if (screenWidth > 0 && screenHeight > 0)
            {
                var dispW = GetSystemMetrics(0); // SM_CXSCREEN
                var dispH = GetSystemMetrics(1); // SM_CYSCREEN
                x = (int)((double)msg.X / screenWidth * dispW);
                y = (int)((double)msg.Y / screenHeight * dispH);
            }

            switch (msg.Action)
            {
                case MouseAction.Move:
                    SetCursorPos(x, y);
                    break;
                case MouseAction.LeftDown:
                    SetCursorPos(x, y);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                    break;
                case MouseAction.LeftUp:
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                    break;
                case MouseAction.RightDown:
                    SetCursorPos(x, y);
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
                    break;
                case MouseAction.RightUp:
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
                    break;
                case MouseAction.MiddleDown:
                    SetCursorPos(x, y);
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, IntPtr.Zero);
                    break;
                case MouseAction.MiddleUp:
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, IntPtr.Zero);
                    break;
                case MouseAction.Wheel:
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)msg.Delta, IntPtr.Zero);
                    break;
            }
        }

        public static void InjectKeyboard(KeyboardEventMessage msg)
        {
            byte vk = (byte)msg.VirtualKey;
            byte scan = (byte)msg.ScanCode;
            switch (msg.Action)
            {
                case KeyboardAction.KeyDown:
                    keybd_event(vk, scan, 0, IntPtr.Zero);
                    break;
                case KeyboardAction.KeyUp:
                    keybd_event(vk, scan, KEYEVENTF_KEYUP, IntPtr.Zero);
                    break;
            }
        }
    }
}
