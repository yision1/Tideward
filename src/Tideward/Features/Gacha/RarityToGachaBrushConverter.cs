using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Tideward.Features.Gacha;

internal partial class RarityToGachaBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Color color = (int)value switch
        {
            5 => Color.FromArgb(255, 220, 159, 100),
            4 => Color.FromArgb(255, 166, 105, 211),
            _ => Color.FromArgb(255, 94, 170, 237),
        };
        if (parameter as string == "stripe") return new SolidColorBrush(color);
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(100, color.R, color.G, color.B), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(30, color.R, color.G, color.B), Offset = 1 },
            },
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
