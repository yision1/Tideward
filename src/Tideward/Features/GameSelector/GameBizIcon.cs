using Tideward.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using Tideward.Core;
using Tideward.Core.Games.Models;
using System;

namespace Tideward.Features.GameSelector;

public partial class GameBizIcon : ObservableObject, IEquatable<GameBizIcon>
{

    private const double GB = 1 << 30;

    public GameId GameId { get; set; }

    public GameBiz GameBiz { get; set; }

    public string GameIcon { get; set => SetProperty(ref field, value); }

    public string GameName { get; set => SetProperty(ref field, value); }

    public string ServerName { get; set => SetProperty(ref field, value); }

    public double MaskOpacity { get; set => SetProperty(ref field, value); } = 1.0;

    public bool IsPinned { get; set => SetProperty(ref field, value); }

    public string? InstallPath { get; set => SetProperty(ref field, value); }

    public long TotalSize { get; set { field = value; OnPropertyChanged(nameof(TotalSizeText)); } }

    public string? TotalSizeText => TotalSize == 0 ? null : $"{TotalSize / GB:F2}GB";

    public bool IsSelected
    {
        get;
        set
        {
            field = value;
            MaskOpacity = value ? 0 : 1;
        }
    }

    public GameBizIcon(GameBiz gameBiz)
    {
        GameBiz = gameBiz;
        GameId = GameId.FromGameBiz(gameBiz)!;
        GameIcon = GameBizToIcon(gameBiz);
        GameName = gameBiz.ToGameName();
        ServerName = gameBiz.ToGameServerName();
    }

    public GameBizIcon(GameInfo gameInfo)
    {
        GameId = gameInfo;
        GameBiz = gameInfo.GameBiz;
        GameIcon = gameInfo.Display.Icon.Url;
        GameName = gameInfo.Display.Name;
        ServerName = gameInfo.GameBiz.ToGameServerName();
    }

    public void UpdateInfo()
    {
        GameIcon = GameBizToIcon(GameBiz);
        GameName = GameBiz.ToGameName();
        ServerName = GameBiz.ToGameServerName();
    }

    public void UpdateInfo(GameInfo gameInfo)
    {
        GameIcon = gameInfo.Display.Icon.Url;
        GameName = gameInfo.Display.Name;
        ServerName = gameInfo.GameBiz.ToGameServerName();
    }

    private static string GameBizToIcon(GameBiz gameBiz)
    {
        return KuroPresentationClient.GameIcon;
    }

    public bool Equals(GameBizIcon? other)
    {
        return ReferenceEquals(this, other) || GameBiz == other?.GameBiz;
    }

}
