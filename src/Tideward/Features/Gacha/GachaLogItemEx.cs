
using CommunityToolkit.Mvvm.ComponentModel;
using Tideward.Core.Gacha;

namespace Tideward.Features.Gacha;

[INotifyPropertyChanged]
public partial class GachaLogItemEx : GachaLogItem
{

    public int Index { get; set; }

    public int Pity { get; set; }

    public string Icon { get; set => SetProperty(ref field, value); }

    public int PityLimit { get; set; } = 80;
    public double Progress => (double)Pity / PityLimit * 100;

    public bool IsPointerIn { get; set => SetProperty(ref field, value); }

    public int ItemCount { get; set; }

    public bool HasUpItem { get; set; }

    public bool IsUp { get; set; }

    public double UpTextOpacity => IsUp ? 1 : 0;

}

