using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.UI.Converters
{
    public class HealthRatingToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HealthRating rating)
                return rating switch
                {
                    HealthRating.Good => new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4A)),
                    HealthRating.Fair => new SolidColorBrush(Color.FromRgb(0xD9, 0x9A, 0x1E)),
                    HealthRating.Poor => new SolidColorBrush(Color.FromRgb(0xE0, 0x6B, 0x1F)),
                    HealthRating.Critical => new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x2B)),
                    _ => Brushes.Gray
                };
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class ScoreToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int score)
            {
                if (score >= 90) return new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4A));
                if (score >= 75) return new SolidColorBrush(Color.FromRgb(0x7A, 0xB8, 0x2E));
                if (score >= 60) return new SolidColorBrush(Color.FromRgb(0xD9, 0x9A, 0x1E));
                if (score >= 40) return new SolidColorBrush(Color.FromRgb(0xE0, 0x6B, 0x1F));
                return new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x2B));
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
