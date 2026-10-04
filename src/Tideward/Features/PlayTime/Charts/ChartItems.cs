using System;

namespace Tideward.Features.PlayTime;

public class BarChartItem
{

        public double Value { get; set; }

        public string Label { get; set; } = "";

        public string? Tooltip { get; set; }

}

public class HeatmapDayItem
{

        public DateOnly Date { get; set; }

        public double Value { get; set; }

        public string? Tooltip { get; set; }

}
