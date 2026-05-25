namespace VisualControl.Shared.Protocol
{
    /// <summary>
    /// 消息类型枚举
    /// </summary>
    public enum MessageType : ushort
    {
        // 连接管理
        Register = 0x0001,          // 客户端注册
        RegisterAck = 0x0002,       // 注册确认
        Heartbeat = 0x0003,         // 心跳
        HeartbeatAck = 0x0004,      // 心跳确认

        // 设备管理
        DeviceInfo = 0x0010,        // 设备信息上报
        DeviceListRequest = 0x0011, // 请求设备列表
        DeviceList = 0x0012,        // 设备列表响应
        DeviceGroup = 0x0013,       // 设备分组操作
        DeviceCommand = 0x0014,     // 远程指令(锁屏/重启/关机)

        // 远程桌面
        ScreenRequest = 0x0020,     // 请求屏幕画面
        ScreenStop = 0x0021,        // 停止屏幕画面
        ScreenFrame = 0x0022,       // 屏幕帧数据
        ScreenConfig = 0x0023,      // 屏幕参数配置

        // 远程输入
        MouseEvent = 0x0030,        // 鼠标事件
        KeyboardEvent = 0x0031,     // 键盘事件

        // 文件传输
        FilePush = 0x0040,          // 推送文件(服务端→客户端)
        FileChunk = 0x0041,         // 文件分片数据
        FileAck = 0x0042,           // 文件分片确认
        FileComplete = 0x0043,      // 文件传输完成
        FileProgress = 0x0044,      // 传输进度上报

        // 通用
        Error = 0x00FF,             // 错误消息
    }

    /// <summary>
    /// 远程指令类型
    /// </summary>
    public enum RemoteCommand : byte
    {
        LockScreen = 1,
        Restart = 2,
        Shutdown = 3,
        OpenFile = 4,
    }

    /// <summary>
    /// 鼠标事件类型
    /// </summary>
    public enum MouseAction : byte
    {
        Move = 0,
        LeftDown = 1,
        LeftUp = 2,
        RightDown = 3,
        RightUp = 4,
        MiddleDown = 5,
        MiddleUp = 6,
        Wheel = 7,
    }

    /// <summary>
    /// 键盘事件类型
    /// </summary>
    public enum KeyboardAction : byte
    {
        KeyDown = 0,
        KeyUp = 1,
    }

    /// <summary>
    /// 设备在线状态
    /// </summary>
    public enum DeviceStatus : byte
    {
        Offline = 0,
        Online = 1,
        Busy = 2,
    }

    /// <summary>
    /// 权限等级
    /// </summary>
    public enum PermissionLevel : byte
    {
        Admin = 0,
        Operator = 1,
    }
}
