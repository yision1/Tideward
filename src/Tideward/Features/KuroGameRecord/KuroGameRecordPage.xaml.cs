using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Tideward.Controls;
using Tideward.Core;
using Tideward.Core.Games;
using Tideward.Features.GameLauncher;
using Tideward.Frameworks;
using Tideward.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Tideward.Features.KuroGameRecord;

public sealed partial class KuroGameRecordPage : PageBase
{
    private readonly KuroGameRecordClient recordClient = new();
    private bool refreshing;
    private bool loadingRoles;
    private bool suppressRoleSelection;
    private bool paneOpen;
    private string section = "overview";
    private KuroDailyNote? currentNote;
    private KuroDailyNoteCredentials? credentials;
    private List<KuroDailyNoteRole> roles = [];
    private KuroRecordData? recordData;
    private IReadOnlyList<KuroRecordPeriod> resourcePeriods = [];
    private bool selectingGroup;
    private CancellationTokenSource? automaticLoad;
    private KuroBattleChronicleWindow? battleChronicleWindow;
    private readonly Dictionary<RecordKey, KuroRecordData> recordCache = [];
    private readonly Dictionary<RecordKey, IReadOnlyList<KuroRecordPeriod>> periodCache = [];
    private readonly Dictionary<RecordKey, int> selectedGroups = [];
    private readonly record struct RecordKey(string Token, string Source, string RoleId, string ServerId, string Section, string Month = "");
    private RecordKey CurrentRecordKey(string month = "") => new(credentials!.Token, credentials.Source, credentials.RoleId, credentials.ServerId, section, month);
    private const string DefaultAvatar = "ms-appx:///Assets/Kuro/community.png";
    public Thickness NavigationViewItemContentMargin { get; private set => SetProperty(ref field, value); } = new(-2, 0, 0, 0);

    public KuroGameRecordPage() => InitializeComponent();

    protected override async void OnLoaded()
    {
        SetPaneOpen(AppConfig.KuroGameRecordPaneOpen);
        RecordNavigation.SelectedItem = OverviewItem;
        try
        {
            if (CurrentGameId.GameBiz != GameBiz.wuwa_cn)
            {
                ConnectButton.IsEnabled = RefreshButton.IsEnabled = false;
                ShowEmpty("暂不支持此区服", "库街区工具箱目前仅支持国服角色。");
                return;
            }
            credentials = await KuroDailyNoteService.ReadCredentialsAsync();
            RestoreSavedRole();
            if (KuroDailyNoteService.GetSavedRole(credentials) is { } role)
                Show(new(role.RoleId, role.Name, role.Server, [], role.ServerId, role.HeadIcon, role.Level));
            else ClearData();
            await InitializeSectionAsync();
        }
        catch (Exception ex) { ShowEmpty("暂时无法读取库街区数据", ErrorMessage(ex)); }
    }

    protected override void OnUnloaded() => CancelAutomaticLoad();

    private void CancelAutomaticLoad()
    {
        if (automaticLoad is null) return;
        automaticLoad.Cancel();
        automaticLoad = null;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
    }

    private void SetPaneOpen(bool open)
    {
        paneOpen = open;
        NavigationViewItemContentMargin = new Thickness(open ? -2 : 2, 0, 0, 0);
        RecordNavigation.IsPaneOpen = open;
        AvatarButton.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        CompactAvatarButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        HeaderRoleInfo.Visibility = open && credentials is not null ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Visibility = open && credentials is null ? Visibility.Visible : Visibility.Collapsed;
        RoleSelectorButton.Visibility = open && credentials is not null ? Visibility.Visible : Visibility.Collapsed;
        DisconnectAccountButton.Visibility = credentials is { RoleId.Length: 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AvatarButton_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        SetPaneOpen(false);
        AppConfig.KuroGameRecordPaneOpen = paneOpen;
    }

    private void CompactAvatarButton_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        SetPaneOpen(true);
        AppConfig.KuroGameRecordPaneOpen = paneOpen;
    }

    private async void RecordNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string selected } || section == selected) return;
        section = selected;
        PageTitleText.Text = selected switch { "challenge" => "逆境深塔", "slash" => "冥歌海墟", "matrix" => "终焉矩阵", "calendar" => "资源简报", _ => "资料" };
        ClearRecord();
        await InitializeSectionAsync();
    }

    private void BattleChronicleItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (CurrentGameId.GameBiz != GameBiz.wuwa_cn) { InAppToast.MainWindow?.Warning(null, "库街区战绩目前仅支持国服角色。"); return; }
        if (credentials is null || credentials.RoleId.Length == 0) { InAppToast.MainWindow?.Warning(null, "请先登录库街区并选择已绑定的鸣潮角色。"); return; }
        if (battleChronicleWindow?.AppWindow is null)
        {
            battleChronicleWindow = new KuroBattleChronicleWindow { CurrentConnection = credentials };
            battleChronicleWindow.Closed += (_, _) => battleChronicleWindow = null;
        }
        else battleChronicleWindow.CurrentConnection = credentials;
        battleChronicleWindow.Activate();
    }

    private async Task InitializeSectionAsync()
    {
        CancelAutomaticLoad();
        if (credentials is null)
        {
            ShowEmpty("尚未登录库街区", "请从左侧登录库街区，再选择已绑定的鸣潮角色。");
            return;
        }
        if (currentNote is null)
        {
            ShowEmpty("请选择角色", "从头像旁的菜单选择已绑定的鸣潮角色，或点击刷新读取角色信息。");
            return;
        }
        bool hasLocalRecord = TryShowCachedSection();
        if (section is not ("overview" or "calendar"))
        {
            if (!hasLocalRecord) ShowEmpty("暂无本地记录", "点击刷新，从库街区获取当前页面的记录。");
            return;
        }

        // Starward opens the profile online and retrieves the current resource summary on entry.
        // Challenge pages only read saved records until the user refreshes them.
        var connection = credentials;
        string requestedSection = section;
        var role = KuroDailyNoteService.GetSavedRole(connection)!;
        string selectedMonth = requestedSection == "calendar" && resourcePeriods.Count > 0
            ? resourcePeriods[Math.Clamp(selectedGroups.GetValueOrDefault(CurrentRecordKey()), 0, resourcePeriods.Count - 1)].Id : "";
        var displayedData = hasLocalRecord ? GetCachedRecord(selectedMonth) : null;
        using var cancellation = new CancellationTokenSource();
        automaticLoad = cancellation;
        if (!hasLocalRecord)
        {
            ShowEmpty("正在读取" + (requestedSection == "calendar" ? "资源简报" : "资料"));
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
        }
        try
        {
            if (requestedSection == "overview" && role.UserId.Length == 0)
            {
                var availableRoles = await KuroDailyNoteService.Client.GetGameRolesAsync(connection.Token, connection.Source, cancellation.Token, KuroDeviceService.GetIdentity(connection));
                cancellation.Token.ThrowIfCancellationRequested();
                role = availableRoles.FirstOrDefault(x => x.RoleId == connection.RoleId && x.ServerId == connection.ServerId)
                    ?? throw new InvalidDataException("未找到已绑定的鸣潮角色，请刷新角色信息。");
                if (role.UserId.Length == 0 && connection.UserId.Length > 0) role = role with { UserId = connection.UserId };
                credentials = connection = await KuroDailyNoteService.SaveRoleAsync(connection, role);
                roles = availableRoles;
                RestoreSavedRole();
            }
            var key = new RecordKey(connection.Token, connection.Source, connection.RoleId, connection.ServerId, requestedSection);
            if (requestedSection == "overview")
            {
                var data = await recordClient.GetAsync(connection.Token, role, connection.Source, requestedSection, cancellation.Token, KuroDeviceService.GetIdentity(connection));
                cancellation.Token.ThrowIfCancellationRequested();
                recordCache[key] = data;
                KuroGameRecordService.SaveRecord(connection, requestedSection, data);
                if (!KuroGameRecordService.SameRecord(displayedData, data)) ShowRecordData(data);
            }
            else
            {
                var periods = await recordClient.GetResourcePeriodsAsync(connection.Token, connection.Source, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                periodCache[key] = periods;
                KuroGameRecordService.SavePeriods(connection, periods);
                bool changed = !hasLocalRecord || !periods.SequenceEqual(resourcePeriods);
                if (periods.Count > 0)
                {
                    var currentMonth = periods[0];
                    var data = await recordClient.GetResourceMonthAsync(connection.Token, role, connection.Source, currentMonth.Id, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    recordCache[key with { Month = currentMonth.Id }] = data;
                    KuroGameRecordService.SaveRecord(connection, requestedSection, data, currentMonth.Id);
                    if (selectedMonth == currentMonth.Id) changed |= !KuroGameRecordService.SameRecord(displayedData, data);
                }
                resourcePeriods = periods;
                int index = periods.ToList().FindIndex(x => x.Id == selectedMonth);
                selectedGroups[key] = Math.Max(0, index);
                if (changed && !TryShowCachedSection()) ShowEmpty("暂无本地记录", "点击刷新，从库街区获取所选月份的资源简报。");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ReferenceEquals(automaticLoad, cancellation)) return;
            if (hasLocalRecord) StatusText.Text = ErrorMessage(ex);
            else ShowEmpty("暂时无法读取" + (requestedSection == "calendar" ? "资源简报" : "资料"), ErrorMessage(ex));
        }
        finally
        {
            if (ReferenceEquals(automaticLoad, cancellation))
            {
                automaticLoad = null;
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void UpdateDeviceInfoItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try
        {
            KuroDeviceService.UpdateDeviceCode();
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(ex);
        }
    }

    private void SetBusy(bool busy)
    {
        refreshing = busy;
        RefreshButton.IsEnabled = ConnectButton.IsEnabled = RoleSelectorButton.IsEnabled = !busy;
        GroupList.IsEnabled = !busy;
        foreach (var item in RecordNavigation.MenuItems.OfType<NavigationViewItem>()) item.IsEnabled = !busy;
        LoadingRing.IsActive = busy;
        LoadingRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) EmptyState.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshAsync(bool force = false)
    {
        if (refreshing) return;
        CancelAutomaticLoad();
        if (CurrentGameId.GameBiz != GameBiz.wuwa_cn)
        {
            ConnectButton.IsEnabled = RefreshButton.IsEnabled = false;
            ShowEmpty("暂不支持此区服", "库街区工具箱目前仅支持国服角色。");
            return;
        }
        SetBusy(true);
        try
        {
            credentials = await KuroDailyNoteService.ReadCredentialsAsync();
            if (credentials is null) { ClearData(); ShowEmpty("尚未登录库街区", "请从左侧登录库街区，再选择已绑定的鸣潮角色。"); return; }
            RestoreSavedRole();
            if (currentNote is null) ClearData();
            if (roles.Count == 0 || force || currentNote is null) await LoadRolesAsync();
            if (credentials.RoleId.Length == 0)
            {
                var selected = roles.FirstOrDefault(x => x.IsDefault) ?? roles.FirstOrDefault();
                if (selected is null) { ShowEmpty("暂无可用角色", RoleListStatusText.Text.Length > 0 ? RoleListStatusText.Text : "请先在库街区绑定鸣潮角色，再刷新角色信息。"); return; }
                await ConnectRoleAsync(credentials with { RoleId = selected.RoleId, ServerId = selected.ServerId }, force);
                return;
            }
            var selectedRole = roles.FirstOrDefault(x => x.RoleId == credentials.RoleId && x.ServerId == credentials.ServerId);
            if (selectedRole is null) { ClearData(); ShowEmpty("请选择角色", "从头像旁的菜单选择已绑定的鸣潮角色。"); return; }
            Show(new(selectedRole.RoleId, selectedRole.Name, selectedRole.Server, [], selectedRole.ServerId, selectedRole.HeadIcon, selectedRole.Level));
            await LoadDetailsAsync(force);
        }
        catch (Exception ex) { ShowEmpty("暂时无法读取库街区数据", ErrorMessage(ex)); }
        finally { SetBusy(false); }
    }

    private void Show(KuroDailyNote note)
    {
        currentNote = note;
        RoleNameText.Text = note.Name;
        RoleInfoText.Text = note.Level.Length > 0 ? $"{note.Server}  Lv.{note.Level}" : note.Server;
        SetAvatar(note.HeadIcon);
        SetPaneOpen(paneOpen);
        ClearRecord();
    }

    private void ShowStats(IEnumerable<KuroDailyNoteStat> stats)
    {
        var views = stats.Select(x => section == "calendar" ? x with { Icon = ResourceIcon(x.Name) } : x).Select(x => new KuroDailyNoteStatView(decimal.TryParse(x.Amount, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? x with { Amount = value.ToString("N0") } : x)).ToArray();
        RecordStatsRepeater.ItemsSource = views;
        bool overview = section == "overview";
        RecordStatsRepeater.ItemTemplate = (DataTemplate)Resources[section == "calendar" ? "ResourceStatTemplate" : overview ? "OverviewStatTemplate" : "CompactStatTemplate"];
        StatsLayout.MaximumRowsOrColumns = Math.Max(1, Math.Min(views.Length, overview ? 4 : section == "calendar" ? 2 : 3));
        StatsLayout.MinItemWidth = overview ? 130 : section == "calendar" ? 200 : 220;
        StatsArea.Padding = new(overview ? 20 : 8);
        StatsArea.Visibility = views.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadRolesAsync()
    {
        if (loadingRoles || credentials is null) return;
        loadingRoles = true;
        RefreshRolesButton.IsEnabled = RoleList.IsEnabled = false;
        RoleListStatusText.Text = "正在读取角色…";
        try
        {
            roles = await KuroDailyNoteService.Client.GetGameRolesAsync(credentials.Token, credentials.Source, device: KuroDeviceService.GetIdentity(credentials));
            if (credentials.UserId.Length > 0)
                roles = roles.Select(role => role.UserId.Length == 0 ? role with { UserId = credentials.UserId } : role).ToList();
            var saved = roles.FirstOrDefault(role => role.RoleId == credentials.RoleId && role.ServerId == credentials.ServerId);
            if (saved is not null) credentials = await KuroDailyNoteService.SaveRoleAsync(credentials, saved);
            RestoreSavedRole();
            UpdateRoleSelection();
            RoleListStatusText.Text = roles.Count == 0 ? "没有可用的已绑定鸣潮角色。" : "";
        }
        catch (Exception ex) { RestoreSavedRole(); RoleListStatusText.Text = ErrorMessage(ex); }
        finally { loadingRoles = false; RefreshRolesButton.IsEnabled = RoleList.IsEnabled = true; }
    }

    private void UpdateRoleSelection()
    {
        suppressRoleSelection = true;
        try
        {
            RoleList.ItemsSource = roles;
            RoleList.SelectedItem = roles.FirstOrDefault(x => x.RoleId == credentials?.RoleId && x.ServerId == credentials?.ServerId);
        }
        finally { suppressRoleSelection = false; }
    }

    private void RestoreSavedRole()
    {
        if (KuroDailyNoteService.GetSavedRole(credentials) is { } saved && !roles.Any(role => role.RoleId == saved.RoleId && role.ServerId == saved.ServerId))
            roles.Insert(0, saved);
        UpdateRoleSelection();
    }

    private async void RoleFlyout_Opened(object sender, object e)
    {
        if (roles.Count == 0) await LoadRolesAsync();
    }

    private async void RefreshRolesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(true);
    }

    private async void RoleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressRoleSelection || refreshing || credentials is null || e.AddedItems.FirstOrDefault() is not KuroDailyNoteRole role) return;
        if (role.RoleId == credentials.RoleId && role.ServerId == credentials.ServerId) return;
        RoleFlyout.Hide();
        SetBusy(true);
        try { await ConnectRoleAsync(credentials with { RoleId = role.RoleId, ServerId = role.ServerId }); }
        catch (Exception ex) { StatusText.Text = ErrorMessage(ex); UpdateRoleSelection(); }
        finally { SetBusy(false); }
    }

    private async Task ConnectRoleAsync(KuroDailyNoteCredentials connection, bool refresh = false)
    {
        CancelAutomaticLoad();
        connection = connection with { Role = roles.FirstOrDefault(role => role.RoleId == connection.RoleId && role.ServerId == connection.ServerId) };
        KuroDailyNote note;
        if (connection.Role is { } selectedRole)
        {
            connection = connection with { UserId = selectedRole.UserId.Length > 0 ? selectedRole.UserId : connection.UserId };
            await KuroDailyNoteService.SaveRoleAsync(connection, selectedRole);
            note = new(selectedRole.RoleId, selectedRole.Name, selectedRole.Server, [], selectedRole.ServerId, selectedRole.HeadIcon, selectedRole.Level);
        }
        else note = await KuroDailyNoteService.ConnectAsync(connection);
        if (credentials?.Token != connection.Token) roles = [];
        credentials = await KuroDailyNoteService.ReadCredentialsAsync() ?? connection;
        Show(note);
        if (roles.Count == 0) await LoadRolesAsync();
        UpdateRoleSelection();
        WeakReferenceMessenger.Default.Send(new KuroDailyNoteConnectionChangedMessage());
        if (refresh) await LoadDetailsAsync(true);
        else await InitializeSectionAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        RoleFlyout.Hide();
        if (refreshing) return;
        SetBusy(true);
        try
        {
            var login = await KuroLoginWindow.LoginAsync(XamlRoot);
            if (login is null) return;

            credentials = await KuroDailyNoteService.SaveLoginAsync(login);
            WeakReferenceMessenger.Default.Send(new KuroDailyNoteConnectionChangedMessage());
            roles = [];
            ClearData();
            StatusText.Text = "已登录，正在读取角色信息…";
            await LoadRolesAsync();
            var selected = roles.FirstOrDefault(x => x.IsDefault) ?? roles.FirstOrDefault();
            if (selected is null) { StatusText.Text = "库街区登录已保存。" + RoleListStatusText.Text; return; }
            await ConnectRoleAsync(credentials with { RoleId = selected.RoleId, ServerId = selected.ServerId });
        }
        catch (Exception ex) { StatusText.Text = ErrorMessage(ex); }
        finally { SetBusy(false); }
    }

    private async Task LoadDetailsAsync(bool force)
    {
        if (credentials is null || currentNote is null) return;
        var role = roles.FirstOrDefault(x => x.RoleId == currentNote.RoleId && x.ServerId == currentNote.ServerId);
        if (role is null) return;
        try
        {
            if (section == "calendar")
            {
                var key = CurrentRecordKey();
                var periods = force ? null : GetCachedPeriods();
                if (periods is null)
                {
                    periods = await recordClient.GetResourcePeriodsAsync(credentials.Token, credentials.Source);
                    periodCache[key] = periods;
                    KuroGameRecordService.SavePeriods(credentials, periods);
                }
                resourcePeriods = periods;
                SetGroups(resourcePeriods.Select(x => x.Title));
                if (resourcePeriods.Count == 0) { ShowEmpty("暂无资源简报月份"); return; }
                int index = Math.Clamp(selectedGroups.GetValueOrDefault(key), 0, resourcePeriods.Count - 1);
                SetSelectedGroup(index);
                await LoadResourceMonthAsync(index, force);
            }
            else
            {
                var key = CurrentRecordKey();
                var data = force ? null : GetCachedRecord();
                if (data is null)
                {
                    data = await recordClient.GetAsync(credentials.Token, role, credentials.Source, section, device: KuroDeviceService.GetIdentity(credentials), refresh: force);
                    recordCache[key] = data;
                    KuroGameRecordService.SaveRecord(credentials, section, data);
                }
                ShowRecordData(data);
            }
        }
        catch (Exception ex)
        {
            if (section == "overview" && role.SummaryStats?.Count > 0) { ShowStats(role.SummaryStats); StatusText.Text = ErrorMessage(ex); }
            else ShowEmpty("暂时无法读取" + (section == "calendar" ? "资源简报" : section == "overview" ? "资料" : "挑战记录"), ErrorMessage(ex));
        }
    }

    private bool TryShowCachedSection()
    {
        if (credentials is null || currentNote is null) return false;
        var key = CurrentRecordKey();
        if (section != "calendar")
        {
            if (GetCachedRecord() is not { } data) return false;
            ShowRecordData(data);
            return true;
        }
        if (GetCachedPeriods() is not { } periods) return false;
        resourcePeriods = periods;
        if (resourcePeriods.Count == 0) { ShowEmpty("暂无资源简报月份"); return true; }
        int index = Math.Clamp(selectedGroups.GetValueOrDefault(key), 0, resourcePeriods.Count - 1);
        SetGroups(resourcePeriods.Select(x => x.Title));
        SetSelectedGroup(index);
        if (GetCachedRecord(resourcePeriods[index].Id) is not { } month) return false;
        DisplayRecord(month.Stats, month.Panels, resourcePeriods[index].Title + " · 资源简报", "本月暂无资源收入");
        return true;
    }

    private KuroRecordData? GetCachedRecord(string month = "")
    {
        var key = CurrentRecordKey(month);
        if (recordCache.TryGetValue(key, out var data)) return data;
        data = KuroGameRecordService.GetRecord(credentials!, section, month);
        if (data is not null) recordCache[key] = data;
        return data;
    }

    private IReadOnlyList<KuroRecordPeriod>? GetCachedPeriods()
    {
        var key = CurrentRecordKey();
        if (periodCache.TryGetValue(key, out var periods)) return periods;
        periods = KuroGameRecordService.GetPeriods(credentials!);
        if (periods is not null) periodCache[key] = periods;
        return periods;
    }

    private void ShowRecordData(KuroRecordData data)
    {
        recordData = data;
        if (data.Groups?.Count > 0)
        {
            SetGroups(data.Groups.Select(x => x.Title));
            SelectGroup(Math.Clamp(selectedGroups.GetValueOrDefault(CurrentRecordKey()), 0, data.Groups.Count - 1));
        }
        else DisplayRecord(data.Stats, data.Panels, data.Period, data.EmptyMessage);
    }

    private void SetSelectedGroup(int index)
    {
        selectingGroup = true;
        GroupList.SelectedIndex = index;
        selectingGroup = false;
        selectedGroups[CurrentRecordKey()] = index;
    }

    private void SetGroups(IEnumerable<string> titles)
    {
        selectingGroup = true;
        try
        {
            GroupList.ItemsSource = titles.Select((title, index) => CreateSelectorItem(title, index)).ToArray();
            GroupList.SelectedIndex = 0; GroupList.Visibility = Visibility.Visible;
        }
        finally { selectingGroup = false; }
    }

    private void SelectGroup(int index)
    {
        SetSelectedGroup(index);
        if (recordData?.Groups is not { } groups || index < 0 || index >= groups.Count) return;
        var group = groups[index];
        DisplayRecord(group.Stats, group.Panels, string.Join(" · ", new[] { group.Title, recordData.Period, group.Detail }.Where(x => x.Length > 0)),
            group.Detail == "暂未解锁" ? group.Detail : recordData.EmptyMessage);
    }

    private async void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingGroup || refreshing || GroupList.SelectedIndex < 0) return;
        if (section != "calendar") { SelectGroup(GroupList.SelectedIndex); return; }
        CancelAutomaticLoad();
        try { await LoadResourceMonthAsync(GroupList.SelectedIndex); }
        catch (Exception ex) { ShowEmpty("暂时无法读取资源简报", ErrorMessage(ex)); }
    }

    private async Task LoadResourceMonthAsync(int index, bool refresh = false)
    {
        var role = KuroDailyNoteService.GetSavedRole(credentials);
        if (credentials is null || role is null || index < 0 || index >= resourcePeriods.Count) return;
        var period = resourcePeriods[index];
        selectedGroups[CurrentRecordKey()] = index;
        ShowStats([]);
        DetailItems.Items.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
        PeriodText.Text = period.Title + " · 资源简报";
        var key = CurrentRecordKey(period.Id);
        var data = refresh ? null : GetCachedRecord(period.Id);
        if (data is null)
        {
            if (!refresh) { ShowEmpty("暂无本地记录", "点击刷新，从库街区获取当前月份的资源简报。"); return; }
            data = await recordClient.GetResourceMonthAsync(credentials.Token, role, credentials.Source, period.Id);
            recordCache[key] = data;
            KuroGameRecordService.SaveRecord(credentials, section, data, period.Id);
        }
        DisplayRecord(data.Stats, data.Panels, period.Title + " · 资源简报", "本月暂无资源收入");
    }

    private void DisplayRecord(IReadOnlyList<KuroDailyNoteStat> stats, IReadOnlyList<KuroRecordPanel> panels, string period, string emptyMessage)
    {
        if (stats.Count == 0 && panels.Count == 0) { ShowEmpty(emptyMessage); return; }
        EmptyState.Visibility = Visibility.Collapsed;
        DetailScrollViewer.Visibility = Visibility.Visible;
        StatusText.Text = "";
        PeriodText.Text = period;
        DetailScrollViewer.ChangeView(null, 0, null, true);
        DetailItems.Items.Clear();
        DetailItems.HorizontalAlignment = section == "overview" ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        if (section == "overview")
        {
            ShowStats([]);
            DetailItems.Items.Add(CreateProfileCard(stats, panels));
            ResizeCards();
            return;
        }
        ShowStats(stats);
        foreach (var group in panels.GroupBy(x => x.Group))
        {
            if (section is "challenge" or "slash" or "matrix") DetailItems.Items.Add(CreateBattleGroup(group.Key, group.ToArray()));
            else
            {
                if (group.Key.Length > 0) DetailItems.Items.Add(new TextBlock { Text = group.Key, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap });
                foreach (var panel in group) DetailItems.Items.Add(CreatePanel(panel));
            }
        }
        ResizeCards();
    }

    private void ShowEmpty(string message, string description = "")
    {
        ShowStats([]);
        DetailItems.Items.Clear();
        DetailScrollViewer.Visibility = Visibility.Collapsed;
        EmptyStateText.Text = message;
        EmptyStateDescriptionText.Text = description;
        EmptyState.Visibility = Visibility.Visible;
    }

    private static string ResourceIcon(string name) => "ms-appx:///Assets/Kuro/" + (name switch
    {
        "星声" => "resource-star.png", "贝币" => "resource-coin.png", "唤声涡纹" => "resource-lustrous.png",
        "浮金波纹 & 铸潮波纹" => "resource-radiant.png", _ => "resource-star.png",
    });

    private void ClearRecord()
    {
        recordData = null;
        selectingGroup = true;
        GroupList.ItemsSource = null;
        selectingGroup = false;
        GroupList.Visibility = Visibility.Collapsed;
        PeriodText.Text = StatusText.Text = "";
        ShowStats([]);
        DetailItems.Items.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        DetailScrollViewer.Visibility = Visibility.Visible;
    }

    private Grid CreateSelectorItem(string title, int index)
    {

        var tile = new Grid { Padding = new(12, 8, 12, 8), RowSpacing = 2, CornerRadius = new(4),
            Background = (Brush)Application.Current.Resources["CustomOverlayAcrylicBrush"] };
        tile.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tile.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tile.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        if (recordData?.Groups is { } groups && index < groups.Count && groups[index].Stats.Count > 0)
        {
            var stats = groups[index].Stats;
            string amount = stats[0].Amount;
            if (section == "challenge" && stats.Count > 1)
            {
                var marks = stats.Select(x => x.Amount.Split('/')).ToArray();
                if (marks.All(x => x.Length == 2 && int.TryParse(x[0], out _) && int.TryParse(x[1], out _)))
                    amount = $"{marks.Sum(x => int.Parse(x[0]))}/{marks.Sum(x => int.Parse(x[1]))}";
            }
            var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            string icon = section switch { "challenge" => "tower.png", "slash" => "slash.png", _ => "matrix.svg" };
            ImageSource source = icon.EndsWith(".svg") ? new SvgImageSource(new Uri("ms-appx:///Assets/Kuro/" + icon)) : new BitmapImage(new Uri("ms-appx:///Assets/Kuro/" + icon));
            summary.Children.Add(new Image { Width = 20, Height = 20, Source = source });
            summary.Children.Add(new TextBlock { Text = amount, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(summary, 1); tile.Children.Add(summary);
        }
        return tile;
    }

    private void DetailScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeCards();

    private void ResizeCards()
    {
        if (DetailScrollViewer.ActualWidth <= 48) return;
        double width = section == "overview" ? DetailScrollViewer.ActualWidth - 48 : Math.Min(388, DetailScrollViewer.ActualWidth - 48);
        foreach (var card in DetailItems.Items.OfType<FrameworkElement>()) card.Width = width;
    }

    private Border CreateProfileCard(IReadOnlyList<KuroDailyNoteStat> stats, IReadOnlyList<KuroRecordPanel> panels)
    {
        KuroDailyNoteStatView View(KuroDailyNoteStat stat, string label, string icon = "") => new(stat with
        {
            Name = label,
            Amount = decimal.TryParse(stat.Amount, out var amount) ? amount.ToString("N0") : stat.Amount,
            Icon = icon.Length > 0 ? "ms-appx:///Assets/Kuro/profile-" + icon + ".png" : "",
        });

        var primary = new List<KuroDailyNoteStatView>();
        foreach (var (name, label) in new[] { ("活跃天数", "游戏天数"), ("联觉等级", "联觉等级"), ("索拉等级", "索拉等级"), ("共鸣者", "解锁角色") })
            if (stats.FirstOrDefault(x => x.Name == name) is { } stat) primary.Add(View(stat, label));
        var collections = new List<KuroDailyNoteStatView>();
        foreach (var (name, label, icon) in new[] { ("成就", "已达成成就", "achievementCount"), ("成就星数", "成就星数", "achievementStar"),
            ("小型信标", "小型信标解锁数", "smallCount"), ("中枢信标", "中枢信标解锁数", "bigCount") })
            if (stats.FirstOrDefault(x => x.Name == name) is { } stat) collections.Add(View(stat, label, icon));
        foreach (var (title, names, prefix) in new[] { ("奇藏箱", new[] { "朴素", "基准", "精密", "辉光" }, "box"),
            ("潮汐之遗", new[] { "绿", "紫", "金" }, "phantom") })
        {
            var metrics = panels.FirstOrDefault(x => x.Title == title)?.Metrics;
            if (metrics is null) continue;
            for (int i = 0; i < names.Length; i++)
                if (metrics.FirstOrDefault(x => x.Name == names[i]) is { } stat)
                    collections.Add(View(stat, title == "奇藏箱" ? names[i] + title : title + "·" + names[i], prefix + (i + 1)));
        }

        ItemsRepeater Repeater(IEnumerable<KuroDailyNoteStatView> items, string template) => new()
        {
            ItemsSource = items.ToArray(), ItemTemplate = (DataTemplate)Resources[template],
            Layout = new UniformGridLayout { MaximumRowsOrColumns = 4, MinItemWidth = 148, MinColumnSpacing = 16,
                MinRowSpacing = 28, ItemsStretch = UniformGridLayoutItemsStretch.Fill, Orientation = Orientation.Horizontal },
        };
        var content = new StackPanel { Padding = new(24), Spacing = 24 };
        if (primary.Count > 0) content.Children.Add(Repeater(primary, "OverviewStatTemplate"));
        if (primary.Count > 0 && collections.Count > 0)
            content.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] });
        if (collections.Count > 0) content.Children.Add(Repeater(collections, "ProfileStatTemplate"));
        var heading = new StackPanel { Height = 52, Padding = new(20, 0, 20, 0), Orientation = Orientation.Horizontal, Spacing = 12,
            CornerRadius = new(8, 28, 0, 0), Background = (Brush)Application.Current.Resources["ControlOnImageFillColorDefaultBrush"] };
        heading.Children.Add(new Image { Width = 24, Height = 24, Source = new BitmapImage(new Uri("ms-appx:///Assets/Kuro/record.png")) });
        heading.Children.Add(new TextBlock { Text = "我的资料", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var body = new StackPanel();
        body.Children.Add(heading); body.Children.Add(content);
        return new Border { Width = 640, CornerRadius = new(8, 28, 8, 8), Child = body,
            Background = (Brush)Application.Current.Resources["CustomOverlayAcrylicBrush"],
            Shadow = (ThemeShadow)Application.Current.Resources["ThemeShadow"], Translation = new(0, 0, 16) };
    }

    private Border CreateBattleGroup(string title, IReadOnlyList<KuroRecordPanel> panels)
    {
        var first = panels[0];
        bool tower = section == "challenge";

        var heading = new Grid { Height = 52, Padding = new(20, 0, 20, 0), ColumnSpacing = 8,
            CornerRadius = new(8, 28, 0, 0), Background = (Brush)Application.Current.Resources["ControlOnImageFillColorDefaultBrush"] };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = title.Length > 0 ? title : first.Title, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var score = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (tower) score.Children.Add(new Image { Width = 24, Height = 24, Source = new BitmapImage(new Uri("ms-appx:///Assets/Kuro/tower.png")) });
        else if (first.Rank is "B" or "A" or "S" or "SS" or "SSS")
            score.Children.Add(new Image { Width = 32, Height = 24, Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri($"ms-appx:///Assets/Kuro/rank-{first.Rank}.png")) });
        score.Children.Add(new TextBlock { Text = first.GroupDetail, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(score, 1); heading.Children.Add(score);
        var rows = new StackPanel();
        rows.Children.Add(heading);
        foreach (var panel in panels) rows.Children.Add(new KuroBattleRow(panel, tower));
        return new Border { Width = 388, CornerRadius = new(8, 28, 8, 8), Child = rows,
            Background = (Brush)Application.Current.Resources["CustomOverlayAcrylicBrush"],
            Shadow = (ThemeShadow)Application.Current.Resources["ThemeShadow"], Translation = new(0, 0, 16) };
    }
    private Border CreatePanel(KuroRecordPanel panel)
    {
        var stack = new StackPanel { Spacing = 12 };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = panel.Title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = panel.Detail, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        Grid.SetColumn(detail, 1); heading.Children.Add(detail);
        stack.Children.Add(heading);
        var list = new ItemsRepeater
        {
            Layout = new UniformGridLayout { MinItemWidth = 64, MinColumnSpacing = 12, MinRowSpacing = 12, MaximumRowsOrColumns = 3, Orientation = Orientation.Horizontal },
            ItemTemplate = (DataTemplate)Resources["RecordAvatarTemplate"], ItemsSource = panel.Roles,
        };

        if (panel.Roles.Count > 0) stack.Children.Add(list);
        if (panel.Metrics is { Count: > 0 } metrics)
        {
            double total = metrics.Sum(x => double.TryParse(x.Amount, out var value) ? value : 0);
            Color[] colors = [Color.FromArgb(255, 97, 154, 212), Color.FromArgb(255, 141, 174, 111), Color.FromArgb(255, 200, 150, 93), Color.FromArgb(255, 159, 140, 197), Color.FromArgb(255, 193, 125, 156), Color.FromArgb(255, 100, 174, 172)];
            if (section == "calendar" && total > 0)
                stack.Children.Add(new ColorRectChart { Height = 20, CornerRadius = new(4), Series = metrics.Select((x, i) => new ColorRectChart.ChartLegend(x.Name,
                    double.TryParse(x.Amount, out var amount) ? (int)Math.Round(amount / total * 100) : 0, colors[i % colors.Length])).ToList() });
            foreach (var (metric, index) in metrics.Select((metric, index) => (metric, index)))
            {
                var row = new Grid { ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                if (section == "calendar") row.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new(4), Background = new SolidColorBrush(colors[index % colors.Length]), VerticalAlignment = VerticalAlignment.Center });
                var label = new TextBlock { Text = metric.Name, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(label, 1); row.Children.Add(label);
                var amount = new TextBlock { Text = double.TryParse(metric.Amount, out var value) ? value.ToString("N0") : metric.Amount, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                if (section == "calendar" && total > 0) amount.Text += $" · {value / total:P0}";
                Grid.SetColumn(amount, 2); row.Children.Add(amount); stack.Children.Add(row);
            }
        }
        return new Border { Width = 388, Child = stack, Style = (Style)Resources["RecordPanelStyle"], Background = (Brush)Application.Current.Resources["CustomOverlayAcrylicBrush"],
            Shadow = (ThemeShadow)Application.Current.Resources["ThemeShadow"], Translation = new(0, 0, 16) };
    }

    private void ClearData()
    {
        recordCache.Clear();
        periodCache.Clear();
        selectedGroups.Clear();
        currentNote = null;
        ClearRecord();
        var saved = KuroDailyNoteService.GetSavedRole(credentials);
        RoleNameText.Text = saved?.Name ?? (credentials is null ? "" : credentials.UserName.Length > 0 ? credentials.UserName : "库街区账号");
        RoleInfoText.Text = saved?.RoleInfo ?? (credentials is null ? "" : "请选择角色");
        SetAvatar(saved?.HeadIcon);
        SetPaneOpen(paneOpen);
    }

    private void SetAvatar(string? source)
    {
        RoleAvatarImage.Source = CompactRoleAvatarImage.Source = string.IsNullOrWhiteSpace(source) ? DefaultAvatar : source;
    }

    private static string ErrorMessage(Exception ex) => ex is InvalidDataException or ArgumentException ? ex.Message : "读取库街区数据失败，请检查网络或重新登录。";

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        CancelAutomaticLoad();
        RoleFlyout.Hide();
        KuroDailyNoteService.Disconnect();
        credentials = null; roles = [];
        UpdateRoleSelection();
        ClearData();
        ShowEmpty("尚未登录库街区", "请从左侧登录库街区，再选择已绑定的鸣潮角色。");
        WeakReferenceMessenger.Default.Send(new KuroDailyNoteConnectionChangedMessage());
    }

    private void RoleMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
        {
            item.IsEnabled = !refreshing && credentials is not null;
            // Only the selected role has a saved local connection. Other entries
            // are bound-role choices returned by Kuro, not locally saved accounts.
            if (item.Text == "删除游戏角色" && item.Tag is KuroDailyNoteRole role)
                item.IsEnabled &= role.RoleId == credentials?.RoleId && role.ServerId == credentials?.ServerId;
        }
    }

    private void CopyRoleToken_Click(object sender, RoutedEventArgs e)
    {
        if (credentials is not null) ClipboardHelper.SetText(credentials.Token);
    }

    private void DeleteRole_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: KuroDailyNoteRole role }
            && role.RoleId == credentials?.RoleId && role.ServerId == credentials?.ServerId)
            DisconnectButton_Click(sender, e);
    }

    private async void ManualConnectButton_Click(object sender, RoutedEventArgs e)
    {

        RoleFlyout.Hide();
        var token = new PasswordBox { Header = "库街区 Token" };
        var role = new TextBox { Header = "鸣潮角色 UID", PlaceholderText = "已在库街区绑定的国服角色" };
        var device = new TextBox { Header = "库街区 App 设备标识（可选）", PlaceholderText = "现有 App 会话的 did，没有则留空" };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400,
            Text = "Token 在本机加密保存，用于向库街区读取便笺、资料与挑战记录。请在库街区绑定角色并开启数据终端。" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(message); panel.Children.Add(role); panel.Children.Add(token); panel.Children.Add(device);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
        panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "输入 Token", Content = panel,
            PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                string did = device.Text.Trim();
                if (!KuroDeviceIdentity.IsValidDid(did)) throw new ArgumentException("库街区设备标识无效。");
                await ConnectRoleAsync(new(token.Password.Trim(), role.Text.Trim(), Did: did));
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Text = ex is InvalidDataException or ArgumentException ? ex.Message : "连接失败，请检查网络和库街区登录状态。";
            }
            finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        try { await dialog.ShowAsync(); }
        finally { token.Password = ""; }
    }

}

