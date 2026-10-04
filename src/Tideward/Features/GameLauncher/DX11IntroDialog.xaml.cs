using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System;
using Tideward.Language;

namespace Tideward.Features.GameLauncher;

public sealed partial class DX11IntroDialog : ContentDialog
{
    public Uri OfficialSourceUri => new(Lang.DX11IntroDialog_OfficialSourceUrl);

    public DX11IntroDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
