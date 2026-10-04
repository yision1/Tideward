using Tideward.Core.Games;
using System;

namespace Tideward.Features.GameLauncher;

public sealed class KuroDailyNoteStatView(KuroDailyNoteStat stat)
{
    public string Label { get; } = stat.Key switch
    {
        "energyData" => "结晶波片",
        "storeEnergyData" => "结晶单质",
        "livenessData" => "活跃度",
        "weeklyData" => "战歌重奏",
        "towerData" => "逆境深塔",
        "slashTowerData" => "冥歌海墟",
        "weeklyFrameData" => "周度游历",
        _ => stat.Name,
    };
    public string Amount { get; } = stat.Amount == "暂无数据" ? "—" : stat.Amount;
    public string Detail { get; } = stat.RecoveryTime > 0
        ? $"回满时间 {DateTimeOffset.FromUnixTimeSeconds(stat.RecoveryTime).ToLocalTime():HH:mm}"
        : stat.Key == "storeEnergyData" && stat.Detail.Length == 0 ? "结晶单质" : stat.Detail.Replace("回满剩余 ", "回满 ");
    public string RecoveryDayHint { get; } = stat.RecoveryTime > 0 && DateTimeOffset.FromUnixTimeSeconds(stat.RecoveryTime).LocalDateTime.Date > DateTime.Now.Date
        ? $"+{(DateTimeOffset.FromUnixTimeSeconds(stat.RecoveryTime).LocalDateTime.Date - DateTime.Now.Date).Days}" : "";
    public string Icon { get; } = stat.Icon;
    public string Description { get; } = $"{stat.Name} · {stat.Amount} {stat.Detail}".TrimEnd();
}
