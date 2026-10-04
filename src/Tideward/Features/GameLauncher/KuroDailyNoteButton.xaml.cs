using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Tideward.Core;
using Tideward.Core.Games;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.Features.GameLauncher;

public sealed partial class KuroDailyNoteButton : UserControl
{
    public GameId CurrentGameId { get; set; } = null!;
    private bool refreshing;
    private CancellationTokenSource? refreshCancellation;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer refreshTimer;

    public KuroDailyNoteButton()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromMinutes(5);
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Register<KuroDailyNoteConnectionChangedMessage>(this, OnConnectionChanged);
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        refreshCancellation?.Cancel();
        HideNote();
    }

    private async void OnConnectionChanged(object _, KuroDailyNoteConnectionChangedMessage message)
    {
        refreshCancellation?.Cancel();
        HideNote();
        await RefreshAsync();
    }

    private async void NoteFlyout_Opened(object sender, object e) => await RefreshAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private async Task RefreshAsync(bool force = false)
    {
        if (refreshing || !IsLoaded) return;
        refreshing = true;
        var cancellation = new CancellationTokenSource();
        refreshCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        try
        {
            if (CurrentGameId?.GameBiz != GameBiz.wuwa_cn) { HideNote(); return; }
            var credentials = await KuroDailyNoteService.ReadCredentialsAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (KuroDailyNoteService.GetSavedRole(credentials) is null) { HideNote(); return; }
            // Like Starward, a locally selected role owns the data-only button.
            Visibility = Visibility.Visible;
            NoteIcon.Source = "https://web-static.kurobbs.com/gamerdata/widget/game3/energy.png";
            refreshTimer.Start();
            var note = await KuroDailyNoteService.GetAsync(force, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (note is null) { HideNote(); return; }
            Show(note);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            bool canceled = cancellation.IsCancellationRequested;
            refreshCancellation = null;
            cancellation.Dispose();
            refreshing = false;
            RefreshButton.IsEnabled = true;
            // A logout/role change during a read must not restore the old note.
            if (canceled && IsLoaded) await RefreshAsync();
        }
    }

    private void Show(KuroDailyNote note)
    {
        RoleNameText.Text = note.Name;
        RoleInfoText.Text = note.Level.Length > 0 ? $"{note.Server}  Lv.{note.Level}" : $"UID {note.RoleId}";
        ToolTipService.SetToolTip(RoleInfoText, $"UID {note.RoleId}");
        RoleAvatarImage.Source = note.HeadIcon.Length > 0 ? note.HeadIcon : null;
        RoleAvatarImage.Visibility = note.HeadIcon.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AvatarPlaceholder.Visibility = note.HeadIcon.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        PrimaryStatsRepeater.ItemsSource = note.Stats.Where(x => x.Key is "energyData" or "storeEnergyData").Select(x => new KuroDailyNoteStatView(x)).ToArray();
        StatsRepeater.ItemsSource = note.Stats.Where(x => x.Key is "livenessData" or "weeklyData" or "weeklyFrameData").Select(x => new KuroDailyNoteStatView(x)).ToArray();
        StatsArea.Visibility = Visibility.Visible;

        StatusText.Text = "";
    }

    private void ShowError(Exception ex)
    {
        ClearStats();
        StatusText.Text = ex is InvalidDataException or ArgumentException ? ex.Message : "读取便笺失败，请检查网络或重新连接库街区。";
    }

    private void ClearStats()
    {
        PrimaryStatsRepeater.ItemsSource = null;
        StatsRepeater.ItemsSource = null;
        StatsArea.Visibility = Visibility.Collapsed;
    }

    private void HideNote()
    {
        Visibility = Visibility.Collapsed;
        refreshTimer.Stop();
        NoteFlyout.Hide();
        ClearStats();
        NoteIcon.Source = RoleAvatarImage.Source = null;
        RoleNameText.Text = RoleInfoText.Text = StatusText.Text = "";
    }

}
