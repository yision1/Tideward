using Tideward.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tideward.Core;
using Tideward.Core.Games.Models;
using Tideward.Features.Database;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tideward.Features.PlayTime;

[INotifyPropertyChanged]
public sealed partial class PlayTimeStatsDialog : ContentDialog
{

    private readonly ILogger<PlayTimeStatsDialog> _logger = AppConfig.GetLogger<PlayTimeStatsDialog>();

    private readonly PlayTimeStatsService _playTimeStatsService = AppConfig.GetService<PlayTimeStatsService>();

    private Dictionary<DateOnly, long> _playTimePerDay = [];

    public PlayTimeStatsDialog()
    {
        this.InitializeComponent();
        this.Loaded += PlayTimeStatsDialog_Loaded;
        this.Unloaded += PlayTimeStatsDialog_Unloaded;
    }

    public GameId CurrentGameId { get; set; }

    public GameBiz CurrentGameBiz { get; set; }

    private void PlayTimeStatsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _playTimeStatsService.ConvertItemToStats();
            LoadPlayTimeStats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load play time stats: GameBiz {biz}", CurrentGameBiz);
        }
        _playTimeLoaded = true;
    }

    private void PlayTimeStatsDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Loaded -= PlayTimeStatsDialog_Loaded;
        this.Unloaded -= PlayTimeStatsDialog_Unloaded;
        StatCards?.Clear();
        PlayTimeBarChart.Items = null;
        PlayTimeHeatmap.Days = null;
        _playTimeLoaded = false;
    }

    private bool _playTimeLoaded;

        public string TotalTimeText { get; set => SetProperty(ref field, value); }

        public string StartUpCountText { get; set => SetProperty(ref field, value); }

        public string AverageDayTimeText { get; set => SetProperty(ref field, value); }

        public string PlayDaysText { get; set => SetProperty(ref field, value); }

        public int LongestContinuousDays { get; set => SetProperty(ref field, value); }

        public string LongestContinuousDaysText { get; set => SetProperty(ref field, value); }

        public string LongestRunTimeText { get; set => SetProperty(ref field, value); }

        public string LongestRunStartText { get; set => SetProperty(ref field, value); }

        public string LastPlayDurationText { get; set => SetProperty(ref field, value); }

        public string LastPlayTimeText { get; set => SetProperty(ref field, value); }

        public string MaxDayPlayTimeText { get; set => SetProperty(ref field, value); }

        public string MaxDayPlayDateText { get; set => SetProperty(ref field, value); }

        public string BarTotalText { get; set => SetProperty(ref field, value); }

        public List<StatCardItem> StatCards { get; set => SetProperty(ref field, value); }

    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }

        private void LoadPlayTimeStats()
    {
        var biz = CurrentGameBiz;
        try
        {
            var sessions = _playTimeStatsService.GetPlayTimeInRange(biz, default, DateTimeOffset.Now);
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            long totalMs = 0;

            long longestSpan = 0, longestStart = 0;

            long lastStart = 0, lastSpan = 0;

            Dictionary<DateOnly, long> timePerDay = new Dictionary<DateOnly, long>();

            foreach (var session in sessions)
            {
                long span = session.EndTime - session.StartTime;
                totalMs += span;
                if (span > longestSpan)
                {
                    longestSpan = span;
                    longestStart = session.StartTime;
                }

                if (session.StartTime > lastStart && now - session.EndTime > 60_000)
                {
                    lastStart = session.StartTime;
                    lastSpan = span;
                }

                DateTimeOffset startTime = DateTimeOffset.FromUnixTimeMilliseconds(session.StartTime).ToLocalTime();
                DateTimeOffset endTime = DateTimeOffset.FromUnixTimeMilliseconds(session.EndTime).ToLocalTime();

                // 计算每日时长，把数据添加到 timePerDay 字典中
                for (DateTime day = startTime.Date; day <= endTime.Date; day = day.AddDays(1))
                {
                    DateTimeOffset dayStart = day == startTime.Date ? startTime : day;
                    DateTimeOffset dayEnd = day == endTime.Date ? endTime : day.AddDays(1).AddTicks(-1);
                    var duration = (long)(dayEnd - dayStart).TotalMilliseconds;
                    var dateOnly = DateOnly.FromDateTime(day);
                    if (timePerDay.ContainsKey(dateOnly))
                    {
                        timePerDay[dateOnly] += duration;
                    }
                    else
                    {
                        timePerDay[dateOnly] = duration;
                    }
                }
            }

            _playTimePerDay = timePerDay;

            TotalTimeText = TimeSpanToString(TimeSpan.FromMilliseconds(totalMs));
            StartUpCountText = totalMs > 0 ? string.Format(Lang.PlayTimeStatsDialog_Started0Times, sessions.Count) : "";
            DatabaseService.SetValue($"playtime_total_{biz}", TimeSpan.FromMilliseconds(totalMs));

            AverageDayTimeText = timePerDay.Count > 0 ? TimeSpanToString(TimeSpan.FromMilliseconds(totalMs / timePerDay.Count)) : "-";
            PlayDaysText = timePerDay.Count > 0 ? string.Format(Lang.PlayTimeStatsDialog_PlayedFor0Days, timePerDay.Count) : "";

            int longestContinuousDays = 0;
            DateOnly? longestContinuousStart = null;
            DateOnly? longestContinuousEnd = null;
            var orderedDays = timePerDay.Keys.OrderBy(d => d).ToList();

            DateOnly? currentStart = null;
            DateOnly? previousDay = null;
            int currentStreak = 0;

            foreach (var orderedDay in orderedDays)
            {
                if (previousDay.HasValue && orderedDay == previousDay.Value.AddDays(1))
                {

                    currentStreak++;
                }
                else
                {

                    currentStart = orderedDay;
                    currentStreak = 1;
                }

                if (currentStreak > longestContinuousDays)
                {
                    longestContinuousDays = currentStreak;
                    longestContinuousStart = currentStart;
                    longestContinuousEnd = orderedDay;
                }

                previousDay = orderedDay;
            }

            if (longestContinuousDays > 0)
            {
                LongestContinuousDays = longestContinuousDays;
                LongestContinuousDaysText = $"{longestContinuousStart:yyyy/MM/dd} - {longestContinuousEnd:yyyy/MM/dd}";
            }
            else
            {
                LongestContinuousDays = 0;
                LongestContinuousDaysText = "";
            }

            long maxDayMs = 0;
            DateOnly maxDayDate = default;
            foreach (var (day, ms) in timePerDay)
            {
                if (ms > maxDayMs)
                {
                    maxDayMs = ms;
                    maxDayDate = day;
                }
            }

            if (maxDayMs > 0)
            {
                MaxDayPlayTimeText = TimeSpanToString(TimeSpan.FromMilliseconds(maxDayMs));
                MaxDayPlayDateText = maxDayDate.ToString("yyyy-MM-dd");
            }
            else
            {
                MaxDayPlayTimeText = "-";
                MaxDayPlayDateText = "";
            }

            LongestRunTimeText = TimeSpanToString(TimeSpan.FromMilliseconds(longestSpan));
            LongestRunStartText = longestSpan > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(longestStart).LocalDateTime.ToString("yyyy-MM-dd") : "";
            if (lastStart > 0)
            {
                LastPlayDurationText = TimeSpanToString(TimeSpan.FromMilliseconds(Math.Max(lastSpan, 60_000)));
                LastPlayTimeText = DateTimeOffset.FromUnixTimeMilliseconds(lastStart).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                LastPlayDurationText = "-";
                LastPlayTimeText = "";
            }

            StatCards =
            [
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_TotalPlaytime, Value = TotalTimeText,SubText = StartUpCountText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_AverageDailyPlaytime, Value = AverageDayTimeText,SubText= PlayDaysText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_LongestStreak, Value = string.Format(Lang.PlayTimeStatsDialog_0Days,LongestContinuousDays), SubText = LongestContinuousDaysText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_LongestSession, Value = LongestRunTimeText, SubText = LongestRunStartText },
                new StatCardItem { Title = Lang.PlayTimeStatsDialog_LongestDailyPlaytime, Value = MaxDayPlayTimeText, SubText = MaxDayPlayDateText },
                new StatCardItem { Title = Lang.PlayTimeButton_LastStartup, Value = LastPlayDurationText, SubText = LastPlayTimeText },
            ];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load play time stats: GameBiz {biz}", biz);
        }

        try
        {
            BuildBarChart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build bar chart: GameBiz {biz}, range {range}", CurrentGameBiz, Segmented_BarRange.SelectedIndex);
        }

        try
        {
            BuildHeatmap();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build heatmap: GameBiz {biz}", CurrentGameBiz);
        }

        if (Lang.PlayTimeStatsDialog_AverageDailyPlaytime.Length > 22)
        {
            UniformGridLayout_StatCards.MinItemHeight = 98;
            Grid_BarChartSwitcher.Margin = new Thickness(4, 16, 4, 0);
            PlayTimeHeatmap.Margin = new Thickness(0, 20, 0, 0);
        }

    }

    private void BuildBarChart()
    {
        int range = Segmented_BarRange.SelectedIndex;
        var today = DateTime.Today;
        if (range == 1)
        {

            int sinceMonday = ((int)today.DayOfWeek + 6) % 7;
            var firstMonday = today.AddDays(-sinceMonday - 11 * 7);
            var items = new List<BarChartItem>(12);
            long total = 0;
            for (int w = 0; w < 12; w++)
            {
                var weekStart = firstMonday.AddDays(w * 7);
                var weekEnd = weekStart.AddDays(6);
                var actualLast = weekEnd <= today ? DateOnly.FromDateTime(weekEnd) : DateOnly.FromDateTime(today);
                long sum = SumDayRange(DateOnly.FromDateTime(weekStart), DateOnly.FromDateTime(weekEnd));
                total += sum;
                items.Add(new BarChartItem
                {
                    Label = weekStart.ToString("MM-dd"),
                    Value = sum / 60_000.0,
                    Tooltip = $"{weekStart:MM/dd} - {actualLast:MM/dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(sum))}",
                });
            }
            PlayTimeBarChart.Items = items;
            BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
        }
        else if (range == 2)
        {

            var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
            var items = new List<BarChartItem>(12);
            long total = 0;
            for (int m = 0; m < 12; m++)
            {
                var monthStart = firstMonth.AddMonths(m);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                long sum = SumDayRange(DateOnly.FromDateTime(monthStart), DateOnly.FromDateTime(monthEnd));
                total += sum;
                items.Add(new BarChartItem
                {
                    Label = monthStart.ToString("MMM", CultureInfo.CurrentUICulture),
                    Value = sum / 60_000.0,
                    Tooltip = $"{monthStart:yyyy-MM}\n{TimeSpanToString(TimeSpan.FromMilliseconds(sum))}",
                });
            }
            PlayTimeBarChart.Items = items;
            BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
        }
        else
        {
            var firstDay = today.AddDays(-14);
            var items = new List<BarChartItem>(15);
            long total = 0;
            for (int i = 0; i < 15; i++)
            {
                var d = firstDay.AddDays(i);
                long ms = _playTimePerDay.GetValueOrDefault(DateOnly.FromDateTime(d));
                total += ms;
                items.Add(new BarChartItem
                {
                    Label = d.ToString("MM-dd"),
                    Value = Math.Max(0, ms / 60_000.0),
                    Tooltip = $"{d:yyyy-MM-dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(ms))}",
                });
            }
            PlayTimeBarChart.Items = items;
            BarTotalText = TimeSpanToString(TimeSpan.FromMilliseconds(total));
        }
    }

    private void Segmented_BarRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_playTimeLoaded)
        {
            return;
        }
        try
        {
            BuildBarChart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rebuild bar chart: GameBiz {biz}", CurrentGameBiz);
        }
    }

    private void BuildHeatmap()
    {

        var today = DateTime.Today;

        // 注意不能用 ((int)DayOfWeek - 1) % 6：C# 取余保留被除数符号，周日会得到 -1 而反向偏移一天。
        int sinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var firstDay = today.AddDays(-sinceMonday - 51 * 7);
        int totalDays = (today - firstDay).Days + 1;
        var items = new List<HeatmapDayItem>(totalDays);
        long total = 0;
        for (int i = 0; i < totalDays; i++)
        {
            var d = firstDay.AddDays(i);
            long ms = _playTimePerDay.GetValueOrDefault(DateOnly.FromDateTime(d));
            total += ms;
            items.Add(new HeatmapDayItem
            {
                Date = DateOnly.FromDateTime(d),
                Value = Math.Max(0, ms / 60_000.0),
                Tooltip = $"{d:yyyy-MM-dd}\n{TimeSpanToString(TimeSpan.FromMilliseconds(ms))}",
            });
        }
        PlayTimeHeatmap.Days = items;
    }

    /// <summary>
    /// 累加 [start, end] 日期区间内的每日游戏毫秒数
    /// </summary>
    private long SumDayRange(DateOnly start, DateOnly end)
    {
        long sum = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            sum += _playTimePerDay.GetValueOrDefault(d);
        }
        return sum;
    }

    public static string TimeSpanToString(TimeSpan timeSpan)
    {
        int totalMinutes = (int)Math.Round(timeSpan.TotalMinutes);
        if (totalMinutes < 1)
        {
            return "0m";
        }
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }
        int hours = totalMinutes / 60, minutes = totalMinutes % 60;
        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

}