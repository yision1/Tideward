using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Foundation;

namespace Tideward.Features.PlayTime;

public sealed class CalendarHeatmap : Grid
{

        public double CellSize { get; set; } = double.NaN;

        public double CellGap { get; set; } = 1.0;

        public double? ScaleMax { get; set; }

        public bool ShowMonthSplit { get; set; }

    private const double WeekdayLabelRightMargin = 6;

    private List<HeatmapDayItem>? _days;
    private bool _rebuildPending;
    private bool _layoutPending;
    private double _cellSize;
    private double _pitch;
    private double _lastAutoWidth;
    private double _lastLabelWidthUsed;
    private int _reconcileCount;
    private Brush[] _levelBrushes = Array.Empty<Brush>();
    private readonly List<FrameworkElement> _itemSlots = new();
    private readonly List<(int FirstK, TextBlock Label)> _monthLabels = new();
    private readonly List<(int LeftK, int RightK)> _monthBoundaries = new();
    private readonly List<Polyline> _monthPolyLines = new();

    private readonly Canvas _plate = new();
    private readonly Canvas _monthLayer = new() { Height = 18 };
    private readonly Canvas _dividerLayer = new() { IsHitTestVisible = false };
    private readonly Grid _weekdayColumn = new();
    private readonly HoverCard _hoverCard = new();

        private double AvailablePixelWidth => ActualWidth > 0 ? ActualWidth : Width > 0 ? Width : 0;

    private bool HasLayoutWidth() => AvailablePixelWidth > 0;

        public List<HeatmapDayItem>? Days
    {
        get => _days;
        set
        {
            _days = value;
            if (HasLayoutWidth())
            {
                Rebuild();
            }
            else
            {
                _rebuildPending = true;
            }
        }
    }

    public CalendarHeatmap()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        SetRow(_monthLayer, 1);
        SetColumn(_monthLayer, 1);
        Children.Add(_monthLayer);

        SetRow(_weekdayColumn, 0);
        SetColumn(_weekdayColumn, 0);
        Children.Add(_weekdayColumn);

        SetRow(_plate, 0);
        SetColumn(_plate, 1);
        Children.Add(_plate);

        SetRow(_dividerLayer, 0);
        SetColumn(_dividerLayer, 1);
        Children.Add(_dividerLayer);

        SetRow(_hoverCard, 0);
        SetColumn(_hoverCard, 0);
        SetRowSpan(_hoverCard, 2);
        SetColumnSpan(_hoverCard, 2);
        Children.Add(_hoverCard);

        LayoutUpdated += OnLayoutUpdated;
        SizeChanged += OnSizeChanged;
    }

    private void OnLayoutUpdated(object? sender, object e)
    {

        // 就用真实宽度重建（每次布局后都校验，直至收敛）。字体解析、Margin 计入方式、

        if (_days is { Count: > 0 })
        {
            double realLabelWidth = _weekdayColumn.ActualWidth;
            if (realLabelWidth > 0 && Math.Abs(realLabelWidth - _lastLabelWidthUsed) > 0.01)
            {
                if (_reconcileCount < 3)
                {
                    _reconcileCount++;
                    Rebuild();
                    return;
                }
            }
            else
            {
                _reconcileCount = 0;
            }
        }
        if (_layoutPending && CanPlaceOverlays())
        {
            _layoutPending = false;
            PlaceMonthOverlays();
        }
        if (_rebuildPending && HasLayoutWidth())
        {
            _rebuildPending = false;
            Rebuild();
        }
    }

    private bool CanPlaceOverlays()
    {
        if (_plate.ActualWidth <= 0)
        {
            return false;
        }

        return _itemSlots.Count > 7
            && _itemSlots[0].ActualWidth > 0
            && _itemSlots[1].ActualWidth > 0
            && _itemSlots[7].ActualWidth > 0;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_days is { Count: > 0 } && double.IsNaN(CellSize) && HasLayoutWidth()
            && Math.Abs(ActualWidth - _lastAutoWidth) > 0.5)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {

        _hoverCard.Clear();
        _plate.Children.Clear();
        _monthLayer.Children.Clear();
        _dividerLayer.Children.Clear();
        _monthLabels.Clear();
        _monthBoundaries.Clear();
        _monthPolyLines.Clear();
        _weekdayColumn.Children.Clear();
        _weekdayColumn.RowDefinitions.Clear();

        var days = _days;
        if (days is null || days.Count == 0)
        {
            return;
        }

        var start = days[0].Date;
        int pad = ((int)start.DayOfWeek + 6) % 7;
        int total = pad + days.Count;
        int columnCount = (int)Math.Ceiling(total / 7.0);

        // 仅首次构建（尚无布局历史）用 24px 通用值兜底——它只影响首个可能来不及渲染的中间帧，

        double labelColumnWidth = _weekdayColumn.ActualWidth > 0 ? _weekdayColumn.ActualWidth : 24;
        _lastLabelWidthUsed = labelColumnWidth;
        double avail = AvailablePixelWidth;
        ComputeMetrics(columnCount, Math.Max(0, avail - labelColumnWidth - 1));
        double cellPitch = _pitch;

        var monday = new DateTime(2024, 1, 1);
        for (int r = 0; r < 7; r++)
        {
            _weekdayColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellPitch) });
            if (r is > 0 and < 6)
            {
                continue;
            }
            var label = new TextBlock
            {
                Text = monday.AddDays(r).ToString("ddd", CultureInfo.CurrentUICulture),
                FontSize = 10,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, WeekdayLabelRightMargin, 0),
                Foreground = ChartHelpers.GetResource<Brush>("TextFillColorSecondaryBrush"),
                IsTextScaleFactorEnabled = false,
            };
            Grid.SetRow(label, r);
            _weekdayColumn.Children.Add(label);
        }

        double scaleMax = ScaleMax ?? ComputeScaleMax(days);
        EnsureLevelBrushes();
        Brush transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        _itemSlots.Clear();
        _itemSlots.Capacity = total;

        for (int i = 0; i < pad; i++)
        {
            _itemSlots.Add(CreateSlot(null, scaleMax, transparent));
        }

        int currentMonth = -1;
        int lastK = -1;
        int dayIndex = 0;
        foreach (var day in days)
        {
            int k = pad + dayIndex;
            int month = day.Date.Year * 12 + day.Date.Month;
            if (month != currentMonth)
            {
                currentMonth = month;

                if (day.Date.Day == 1)
                {
                    var label = new TextBlock
                    {
                        Text = day.Date.ToDateTime(TimeOnly.MinValue).ToString("MMM", CultureInfo.CurrentUICulture),
                        TextAlignment = TextAlignment.Left,
                        FontSize = 11,
                        Foreground = ChartHelpers.GetResource<Brush>("TextFillColorSecondaryBrush"),
                        IsTextScaleFactorEnabled = false,
                    };
                    _monthLayer.Children.Add(label);
                    _monthLabels.Add((k, label));
                }
                if (ShowMonthSplit && lastK >= 0)
                {

                    var polyline = new Polyline
                    {
                        Stroke = ChartHelpers.GetResource<Brush>("ControlStrongStrokeColorDefaultBrush"),
                        StrokeThickness = 1,
                        Opacity = 0.45,
                        StrokeLineJoin = PenLineJoin.Round,
                        IsHitTestVisible = false,
                        Points = new PointCollection { new(0, 0), new(0, 0), new(0, 0), new(0, 0) },
                    };
                    _dividerLayer.Children.Add(polyline);
                    _monthBoundaries.Add((lastK, k));
                    _monthPolyLines.Add(polyline);
                }
            }
            var daySlot = CreateSlot(day, scaleMax, transparent);
            lastK = k;
            dayIndex++;
            _itemSlots.Add(daySlot);
        }

        for (int i = 0; i < _itemSlots.Count; i++)
        {
            var slot = _itemSlots[i];
            Canvas.SetLeft(slot, (i / 7) * _pitch);
            Canvas.SetTop(slot, (i % 7) * _pitch);
            _plate.Children.Add(slot);
        }
        if (CanPlaceOverlays())
        {
            PlaceMonthOverlays();
        }
        else
        {
            _layoutPending = true;
        }
    }

        private FrameworkElement CreateSlot(HeatmapDayItem? day, double scaleMax, Brush transparent)
    {
        var slot = new Grid
        {
            Width = _pitch,
            Height = _pitch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var border = new Border
        {
            Width = _cellSize,
            Height = _cellSize,
            CornerRadius = new CornerRadius(2),
            Background = day is { } d ? LevelBrush(d.Value, scaleMax) ?? transparent : transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slot.Children.Add(border);
        if (day is { } d2 && !string.IsNullOrEmpty(d2.Tooltip))
        {
            _hoverCard.Bind(border, () => d2.Tooltip);
        }
        return slot;
    }

        private void ComputeMetrics(int columnCount, double itemWidth)
    {
        if (double.IsNaN(CellSize))
        {
            if (itemWidth > 0 && columnCount > 0)
            {

                double pitch = Math.Clamp(Math.Floor(itemWidth / columnCount * 100) / 100, 8, 44);
                _cellSize = Math.Max(2, pitch - CellGap * 2);
                _pitch = _cellSize + CellGap * 2;
            }
            else
            {
                _cellSize = 14;
                _pitch = _cellSize + CellGap * 2;
            }
        }
        else
        {
            _cellSize = Math.Max(2, CellSize);
            _pitch = _cellSize + CellGap * 2;
        }
        _lastAutoWidth = ActualWidth;
    }

        private void PlaceMonthOverlays()
    {
        var p0 = _itemSlots[0].TransformToVisual(_dividerLayer).TransformPoint(new Point(0, 0));
        var p1 = _itemSlots[1].TransformToVisual(_dividerLayer).TransformPoint(new Point(0, 0));
        var p7 = _itemSlots[7].TransformToVisual(_dividerLayer).TransformPoint(new Point(0, 0));
        double baseX = p0.X;
        double baseY = p0.Y;
        double colW = p7.X - p0.X;
        double rowH = p1.Y - p0.Y;
        if (colW <= 0 || rowH <= 0)
        {
            colW = _pitch;
            rowH = _pitch;
        }
        double height = baseY + 7 * rowH;

        foreach (var (firstK, label) in _monthLabels)
        {

            int colA = firstK / 7;
            double left = Math.Floor(baseX + colA * colW) + CellGap;
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, 2);
        }
        for (int i = 0; i < _monthBoundaries.Count; i++)
        {
            var (_, rightK) = _monthBoundaries[i];
            int colR = rightK / 7;
            int rowR = rightK % 7;
            double xLeft = Math.Floor(baseX + colR * colW) + 0.5;
            if (rowR == 0)
            {
                _monthPolyLines[i].Points = new PointCollection
                {
                    new(xLeft, 0),
                    new(xLeft, height),
                    new(xLeft, height),
                    new(xLeft, height),
                };
            }
            else
            {
                double xRight = Math.Floor(baseX + (colR + 1) * colW) + 0.5;
                double jogY = Math.Floor(baseY + rowR * rowH) + 0.5;
                _monthPolyLines[i].Points = new PointCollection
                {
                    new(xRight, 0),
                    new(xRight, jogY),
                    new(xLeft, jogY),
                    new(xLeft, height),
                };
            }
        }
    }

    private static double ComputeScaleMax(List<HeatmapDayItem> days)
    {
        double max = 0;
        foreach (var d in days)
        {
            max = Math.Max(max, d.Value);
        }
        return max > 0 ? max : 1;
    }

        private void EnsureLevelBrushes()
    {
        if (_levelBrushes.Length == 8)
        {
            return;
        }
        bool dark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        _levelBrushes = new Brush[8]
        {
            ChartHelpers.GetResource<Brush>("CardBackgroundFillColorDefaultBrush"),
            LevelColorBrush("SystemAccentColorDark3", dark ? "#0A331E" : "#0F3A20"),
            LevelColorBrush("SystemAccentColorDark2", dark ? "#0E4429" : "#154F27"),
            LevelColorBrush("SystemAccentColorDark1", dark ? "#125B27" : "#1B5E32"),
            LevelColorBrush("SystemAccentColor", dark ? "#187632" : "#216E39"),
            LevelColorBrush("SystemAccentColorLight1", dark ? "#1F8B3B" : "#30A14E"),
            LevelColorBrush("SystemAccentColorLight2", dark ? "#26A641" : "#40C463"),
            LevelColorBrush("SystemAccentColorLight3", dark ? "#39D353" : "#9BE9A8"),
        };
    }

    private static SolidColorBrush LevelColorBrush(string colorResourceKey, string fallbackHex)
    {
        if (Application.Current.Resources.TryGetValue(colorResourceKey, out var value) && value is Windows.UI.Color color)
        {
            return new SolidColorBrush(color);
        }
        return new SolidColorBrush(ParseHex(fallbackHex));
    }

    /// <summary>
    /// 数值映射为色阶画刷；负数返回 null（占位透明），0 为无数据底色。
    /// 色阶分段采用线性增长的窗口：第 k 级覆盖的时长 = k × w（w = scaleMax / 28），
    /// 即每个 level 包含的时间长度相对上一个 level 线性增长，7 级总界恰好达到 scaleMax。
    /// </summary>
    public Brush? LevelBrush(double value, double scaleMax)
    {
        EnsureLevelBrushes();
        if (value < 0)
        {
            return null;
        }
        double ceiling = scaleMax <= 0 ? 1 : scaleMax;
        int level = 0;
        if (value > 0)
        {
            level = 7;
            for (int k = 1; k <= 7; k++)
            {
                if (value <= ceiling * k * (k + 1) / 56.0)
                {
                    level = k;
                    break;
                }
            }
        }
        return _levelBrushes[level];
    }

    private static Windows.UI.Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(
            255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

}
