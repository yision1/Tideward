using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Tideward.Controls;
using Tideward.Core.Games;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace Tideward.Features.KuroGameRecord;

public sealed partial class KuroBattleRow : UserControl
{
    private readonly List<Grid> portraitSlots = [];
    private readonly List<Button> buffButtons = [];

    public KuroBattleRow(KuroRecordPanel panel, bool tower)
    {
        InitializeComponent();
        TitleText.Text = panel.Title;
        DetailText.Text = panel.Detail;
        if (tower && panel.Stars >= 0 && panel.Roles.Count > 0)
        {
            DetailText.Visibility = Visibility.Collapsed;
            Marks.Visibility = Visibility.Visible;
            for (int i = 0; i < 3; i++)
                Marks.Children.Add(new Image { Width = 20, Height = 20, Source = new BitmapImage(new Uri(i < panel.Stars ? "ms-appx:///Assets/Kuro/tower.png" : "ms-appx:///Assets/Kuro/tower-empty.png")) });
            AutomationProperties.SetName(Marks, $"{panel.Stars}/3 印记");
        }

        foreach (var role in panel.Roles)
            AddPortrait(role);
        foreach (var buff in panel.Buffs ?? [])
        {
            var button = new Button { Width = 56, Height = 72, Padding = new(2), CornerRadius = new(4), Tag = buff,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new(0) };
            var art = new Grid();
            if (buff.Icon.Length > 0) art.Children.Add(new CachedImage { Source = buff.Icon, Stretch = Stretch.Uniform });
            else art.Children.Add(new FontIcon { Glyph = "\uE8D4", FontSize = 24 });
            if (buff.Quality is >= 1 and <= 5) art.Children.Add(new Image { Source = new BitmapImage(new Uri($"ms-appx:///Assets/Kuro/quality-{buff.Quality}.png")), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom });
            button.Content = art;
            ToolTipService.SetToolTip(button, buff.Name.Length > 0 ? buff.Name : "信物说明");
            AutomationProperties.SetName(button, buff.Name.Length > 0 ? buff.Name : "信物说明");
            button.Click += Buff_Click;
            Buffs.Children.Add(button);
            buffButtons.Add(button);
        }
    }

    private void AddPortrait(KuroRecordAvatar role)
    {
        // Keep the compact upstream avatar tile, with Kuro's portrait-only battle information.
        var slot = new Grid { Width = 72, Height = 72, Background = (Brush)Application.Current.Resources["ControlOnImageFillColorDefaultBrush"], CornerRadius = new(0, 12, 0, 0) };
        if (role.Icon.Length > 0) slot.Children.Add(new CachedImage { Source = role.Icon, CornerRadius = new(0, 12, 0, 0), Stretch = Stretch.UniformToFill });
        if (role.SkillBranch is 0 or 1)
        {
            var marker = new Image { Width = 22, Height = 22,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Source = new BitmapImage(new Uri("ms-appx:///Assets/Kuro/skill-branch.png")) };
            if (role.SkillBranch == 1) marker.RenderTransform = new ScaleTransform { ScaleX = -1, CenterX = 11 };
            slot.Children.Add(marker);
        }
        portraitSlots.Add(slot);
        if (role.Name.Length > 0) ToolTipService.SetToolTip(slot, role.Name);
        AutomationProperties.SetName(slot, role.Name.Length > 0 ? role.Name : "未出战");
        Portraits.Children.Add(slot);
    }

    private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 360;
        ContentGrid.Padding = compact ? new(12, 8, 12, 12) : new(20, 8, 20, 12);
        Portraits.Spacing = compact ? 8 : 12;
        foreach (var slot in portraitSlots) slot.Width = slot.Height = compact ? 60 : 72;
        foreach (var button in buffButtons) button.Width = compact ? 44 : 56;
    }

    private async void Buff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: KuroRecordBuff buff } button) return;
        button.IsEnabled = false;
        try
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = buff.Name.Length > 0 ? buff.Name : "信物说明", CloseButtonText = "关闭",
                Content = new TextBlock { Text = buff.Description.Length > 0 ? buff.Description : "库街区未提供此信物的说明。", TextWrapping = TextWrapping.Wrap, MaxWidth = 420 } }.ShowAsync();
        }
        finally { button.IsEnabled = true; }
    }
}
