using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AdvancedWindowsHotspot.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility.Visible;
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility.Collapsed;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? false : true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? false : true;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                return string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
            }
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HotspotStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Models.HotspotStatus status)
            {
                return status switch
                {
                    Models.HotspotStatus.Running => Application.Current.FindResource("SuccessBrush"),
                    Models.HotspotStatus.Error => Application.Current.FindResource("ErrorBrush"),
                    Models.HotspotStatus.Starting or Models.HotspotStatus.Stopping => Application.Current.FindResource("WarningBrush"),
                    _ => Application.Current.FindResource("OnSurfaceVariantBrush")
                };
            }
            return Application.Current.FindResource("OnSurfaceVariantBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HotspotStatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Models.HotspotStatus status)
            {
                return status switch
                {
                    Models.HotspotStatus.Idle => "就绪",
                    Models.HotspotStatus.Starting => "正在启动...",
                    Models.HotspotStatus.Running => "热点运行中",
                    Models.HotspotStatus.Stopping => "正在停止...",
                    Models.HotspotStatus.Error => "发生错误",
                    _ => "未知"
                };
            }
            return "未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
