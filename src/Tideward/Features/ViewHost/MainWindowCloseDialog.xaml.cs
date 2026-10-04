using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Tideward.Helpers.Enumeration;
using System.ComponentModel;

namespace Tideward.Features.ViewHost;

[INotifyPropertyChanged]
public sealed partial class MainWindowCloseDialog : ContentDialog
{

    public MainWindowCloseDialog()
    {
        this.InitializeComponent();
    }

    public EnumItemsSource<MainWindowCloseOption> MainWindowCloseOption
    {
        get;
        set => SetProperty(ref field, value);
    } = new(ViewHost.MainWindowCloseOption.Hide);

}
