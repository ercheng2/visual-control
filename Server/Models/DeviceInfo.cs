namespace VisualControl.Server.Models
{
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
        public string GroupName { get; set; } = "默认分组";
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public DateTime ConnectTime { get; set; } = DateTime.Now;
        public DateTime LastHeartbeat { get; set; } = DateTime.Now;

        public Protocol.DeviceInfoMessage ToMessage()
        {
            return new Protocol.DeviceInfoMessage
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                IpAddress = IpAddress,
                Status = Status,
                GroupName = GroupName,
                ScreenWidth = ScreenWidth,
                ScreenHeight = ScreenHeight
            };
        }
    }
}
