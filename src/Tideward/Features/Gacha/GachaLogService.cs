using Dapper;
using Microsoft.Extensions.Logging;
using Tideward.Core;
using Tideward.Core.Gacha;
using Tideward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.Features.Gacha;

internal abstract class GachaLogService
{

    protected readonly ILogger<GachaLogService> _logger;

    protected GachaLogService(ILogger<GachaLogService> logger)
    {
        _logger = logger;
    }

    protected abstract GameBiz CurrentGameBiz { get; }

    protected abstract string GachaTableName { get; }

    protected abstract List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type);

    protected abstract int GetPityLimit(IGachaType type);

    public abstract IReadOnlyCollection<IGachaType> QueryGachaTypes { get; }

    public static string GetGachaLogText(GameBiz biz)
    {
        return biz.ToGame().Value switch
        {
            GameBiz.wuwa => "唤取记录",
            _ => ""
        };
    }

    public virtual List<long> GetUids()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<long>($"SELECT DISTINCT Uid FROM {GachaTableName};").ToList();
    }

    public virtual List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GachaLogItemEx>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Time, Id;", new { uid }).ToList();
        foreach (IGachaType type in QueryGachaTypes)
        {
            var l = GetGachaLogItemsByQueryType(list, type);
            int index = 0;
            int pity = 0;
            foreach (var item in l)
            {
                item.Index = ++index;
                item.Pity = ++pity;
                item.PityLimit = GetPityLimit(type);
                if (item.RankType == 5)
                {
                    pity = 0;
                }
            }
        }
        return list;
    }

    public abstract string? GetGachaLogUrlByUid(long uid);

    public abstract string? GetGachaLogUrlFromWebCache(GameBiz gameBiz, string path);
    public abstract Task<long> GetUidFromGachaLogUrl(string url);
    public abstract Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    protected abstract int InsertGachaLogItems(List<GachaLogItem> items);

    public virtual (List<GachaTypeStats> GachaStats, List<GachaLogItemEx> ItemStats) GetGachaTypeStats(long uid)
    {
        var statsList = new List<GachaTypeStats>();
        var groupStats = new List<GachaLogItemEx>();
        var allItems = GetGachaLogItemEx(uid);
        if (allItems.Count > 0)
        {
            foreach (IGachaType type in QueryGachaTypes)
            {
                var list = GetGachaLogItemsByQueryType(allItems, type);
                if (list.Count == 0)
                {
                    continue;
                }
                var stats = new GachaTypeStats
                {
                    GachaType = type.Value,
                    GachaTypeText = type.ToLocalization(),
                    Count = list.Count,
                    Count_5_Up = list.Count(x => x.RankType == 5 && x.IsUp),
                    Count_5 = list.Count(x => x.RankType == 5),
                    Count_4 = list.Count(x => x.RankType == 4),
                    Count_3 = list.Count(x => x.RankType == 3),
                    StartTime = list.First().Time,
                    EndTime = list.Last().Time
                };
                stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
                stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
                stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
                stats.List_5 = list.Where(x => x.RankType == 5).Reverse().ToList();
                stats.List_4 = list.Where(x => x.RankType == 4).Reverse().ToList();
                stats.Pity_5 = list.Last().Pity;
                if (list.Last().RankType == 5)
                {
                    stats.Pity_5 = 0;
                }
                stats.Average_5 = stats.Count_5 == 0 ? 0 : (double)(stats.Count - stats.Pity_5) / stats.Count_5;
                stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 4);

                if (stats.Count_5_Up > 0)
                {
                    int c = stats.Count - stats.Pity_5;
                    stats.Average_5_Up = (double)c / stats.Count_5_Up;
                }

                int pity_4 = 0;
                foreach (var item in list)
                {
                    pity_4++;
                    if (item.RankType == 4)
                    {
                        item.Pity = pity_4;
                        pity_4 = 0;
                    }
                }

                statsList.Add(stats);
                stats.List_5.Insert(0, CreatePityItem(type, stats.Pity_5, list.Last().Time));
                stats.List_4.Insert(0, CreatePityItem(type, stats.Pity_4, list.Last().Time));
            }
            groupStats = allItems.GroupBy(x => x.ItemId)
                                 .Select(x => { var item = x.First(); item.ItemCount = x.Count(); return item; })
                                 .OrderByDescending(x => x.RankType)
                                 .ThenByDescending(x => x.ItemCount)
                                 .ThenByDescending(x => x.Time)
                                 .ToList();
        }
        return (statsList, groupStats);
    }

    private GachaLogItemEx CreatePityItem(IGachaType type, int pity, DateTime time) => new()
    {
        GachaType = type.Value,
        Name = Lang.GachaStatsCard_Pity,
        Pity = pity,
        Time = time,
        PityLimit = GetPityLimit(type),
    };

    public virtual int DeleteUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid;", new { uid });
    }

    public virtual int DeleteGachaLogByTime(long uid, DateTime begin, DateTime end)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid AND Time >= @begin AND Time <= @end;", new { uid, begin, end });
    }

    public abstract Task ExportGachaLogAsync(long uid, string file, string format);

    public abstract long ImportGachaLog(string file);

    public abstract Task<(string Language, int Count)> ChangeGachaItemNameAsync(string language);

}

