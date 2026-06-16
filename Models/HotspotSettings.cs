namespace AdvancedWindowsHotspot.Models
{
    public class HotspotSettings
    {
        public string Ssid { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsPasswordVisible { get; set; } = false;
        public WiFiBand Band { get; set; } = WiFiBand.Auto;
        public bool AllowInternet { get; set; } = true;
        public bool UseSystemHotspot { get; set; } = false;
    }

    public enum WiFiBand
    {
        Auto,
        TwoPointFourGHz,
        FiveGHz
    }

    public enum HotspotStatus
    {
        Idle,
        Starting,
        Running,
        Stopping,
        Error
    }
}
