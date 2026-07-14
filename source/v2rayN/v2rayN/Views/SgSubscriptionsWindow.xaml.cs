using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace v2rayN.Views;

public partial class SgSubscriptionsWindow : Window
{
    private readonly Config _config = AppManager.Instance.Config;
    private readonly ObservableCollection<SgSubscriptionEntry> _subscriptions = [];
    private string? _editingId;
    private Func<Task>? _confirmAction;
    private bool _busy;

    public SgSubscriptionsWindow()
    {
        InitializeComponent();
        SgWindowSizing.AttachLarge(this);
        SourceInitialized += (_, _) => WindowsUtils.SetSgBorderlessFrame(this);
        lstSubscriptions.ItemsSource = _subscriptions;
        Loaded += async (_, _) => await RefreshSubscriptionsAsync();
    }

    private async Task RefreshSubscriptionsAsync()
    {
        _subscriptions.Clear();
        var items = await AppManager.Instance.SubItems() ?? [];

        foreach (var item in items.OrderBy(t => t.Sort))
        {
            var profiles = await AppManager.Instance.ProfileItems(item.Id) ?? [];
            _subscriptions.Add(new SgSubscriptionEntry
            {
                Id = item.Id,
                Name = item.Remarks.IsNullOrEmpty() ? "Без названия" : item.Remarks,
                Url = item.Url,
                UrlDisplay = BuildSafeUrlDisplay(item.Url),
                StateText = item.Enabled ? "ВКЛЮЧЕНА" : "ОТКЛЮЧЕНА",
                StateBrush = (Brush)FindResource(item.Enabled ? "SgAccentBrush" : "SgMutedBrush"),
                ProfileCountText = GetProfileCountText(profiles.Count),
                LastUpdateText = item.UpdateTime > 0
                    ? $"Обновлена: {DateTimeOffset.FromUnixTimeSeconds(item.UpdateTime).LocalDateTime:dd.MM.yyyy HH:mm}"
                    : "Ещё не обновлялась",
            });
        }

        txtSubscriptionCount.Text = GetSubscriptionCountText(_subscriptions.Count);
        txtEmpty.Visibility = _subscriptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        lstSubscriptions.Visibility = _subscriptions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void SaveSubscription_Click(object sender, RoutedEventArgs e)
    {
        var name = txtSubscriptionName.Text.Trim();
        var url = txtSubscriptionUrl.Text.Trim();

        if (name.IsNullOrEmpty())
        {
            SetStatus("Введите понятное название подписки.", "SgErrorBrush");
            txtSubscriptionName.Focus();
            return;
        }

        var uri = Utils.TryUri(url);
        if (uri == null || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            SetStatus("Введите корректный URL подписки, начинающийся с https:// или http://.", "SgErrorBrush");
            txtSubscriptionUrl.Focus();
            return;
        }

        var item = _editingId.IsNotEmpty()
            ? await AppManager.Instance.GetSubItem(_editingId)
            : new SubItem();

        item ??= new SubItem();
        item.Remarks = name;
        item.Url = url;
        item.Enabled = chkSubscriptionEnabled.IsChecked == true;
        item.UserAgent = item.UserAgent.IsNullOrEmpty() ? "SG-Client/055" : item.UserAgent;

        var result = await ConfigHandler.AddSubItem(_config, item);
        if (result != 0)
        {
            SetStatus("Не удалось сохранить подписку.", "SgErrorBrush");
            return;
        }

        await ConfigHandler.SaveConfig(_config);
        AppEvents.SubscriptionsRefreshRequested.Publish();
        ClearEditor();
        await RefreshSubscriptionsAsync();
        SetStatus("Подписка сохранена. Теперь её можно обновить напрямую или через VPN.", "SgSuccessBrush");
    }

    private async void EditSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var item = await AppManager.Instance.GetSubItem(id);
        if (item == null)
        {
            SetStatus("Подписка больше не существует.", "SgErrorBrush");
            await RefreshSubscriptionsAsync();
            return;
        }

        _editingId = item.Id;
        txtEditorTitle.Text = "Редактирование подписки";
        txtSubscriptionName.Text = item.Remarks;
        txtSubscriptionUrl.Text = item.Url;
        chkSubscriptionEnabled.IsChecked = item.Enabled;
        btnSaveSubscription.Content = "Сохранить изменения";
        btnCancelEdit.Visibility = Visibility.Visible;
        txtSubscriptionName.Focus();
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        ClearEditor();
        SetStatus("Редактирование отменено.", "SgMutedBrush");
    }

    private void ClearEditor()
    {
        _editingId = null;
        txtEditorTitle.Text = "Новая подписка";
        txtSubscriptionName.Clear();
        txtSubscriptionUrl.Clear();
        chkSubscriptionEnabled.IsChecked = true;
        btnSaveSubscription.Content = "Сохранить";
        btnCancelEdit.Visibility = Visibility.Collapsed;
    }

    private async void DeleteSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var item = await AppManager.Instance.GetSubItem(id);
        if (item == null)
        {
            await RefreshSubscriptionsAsync();
            return;
        }

        ShowConfirmation(
            "Удалить подписку?",
            $"Подписка «{item.Remarks}» и все полученные из неё профили будут удалены.",
            "Удалить",
            async () =>
            {
                await ConfigHandler.DeleteSubItem(_config, id);
                await ConfigHandler.SaveConfig(_config);
                AppEvents.SubscriptionsRefreshRequested.Publish();
                AppEvents.ProfilesRefreshRequested.Publish();
                if (_editingId == id)
                {
                    ClearEditor();
                }
                await RefreshSubscriptionsAsync();
                SetStatus("Подписка удалена.", "SgMutedBrush");
            });
    }

    private async void UpdateDirect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            await UpdateSubscriptionAsync(id, useVpn: false);
        }
    }

    private async void UpdateVpn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            await UpdateSubscriptionAsync(id, useVpn: true);
        }
    }

    private async void UpdateAllDirect_Click(object sender, RoutedEventArgs e)
    {
        await UpdateAllAsync(useVpn: false);
    }

    private async void UpdateAllVpn_Click(object sender, RoutedEventArgs e)
    {
        await UpdateAllAsync(useVpn: true);
    }

    private async Task UpdateAllAsync(bool useVpn)
    {
        if (_busy)
        {
            return;
        }

        var items = (await AppManager.Instance.SubItems() ?? [])
            .Where(t => t.Enabled)
            .ToList();

        if (items.Count == 0)
        {
            SetStatus("Нет включённых подписок для обновления.", "SgWarningBrush");
            return;
        }

        foreach (var item in items)
        {
            await UpdateSubscriptionAsync(item.Id, useVpn, keepBusyBetweenItems: true);
        }

        SetBusy(false);
        SetStatus($"Обновление завершено. Обработано подписок: {items.Count}.", "SgSuccessBrush");
    }

    private async Task UpdateSubscriptionAsync(
        string id,
        bool useVpn,
        bool keepBusyBetweenItems = false)
    {
        if (_busy && !keepBusyBetweenItems)
        {
            return;
        }

        var item = await AppManager.Instance.GetSubItem(id);
        if (item == null)
        {
            SetStatus("Подписка больше не существует.", "SgErrorBrush");
            await RefreshSubscriptionsAsync();
            return;
        }

        if (!item.Enabled)
        {
            SetStatus($"Подписка «{item.Remarks}» отключена.", "SgWarningBrush");
            return;
        }

        SetBusy(true);
        var before = (await AppManager.Instance.ProfileItems(id))?.Count ?? 0;
        var succeeded = false;
        var mode = useVpn ? "через VPN" : "напрямую";
        SetStatus($"Обновляется «{item.Remarks}» {mode}…", "SgAccentBrush");

        try
        {
            await SubscriptionHandler.UpdateProcess(
                _config,
                id,
                useVpn,
                async (success, message) =>
                {
                    succeeded |= success;
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        SetStatus(CleanStatusMessage(message), success ? "SgSuccessBrush" : "SgMutedBrush");
                    }
                    await Task.CompletedTask;
                });

            var after = (await AppManager.Instance.ProfileItems(id))?.Count ?? 0;
            if (succeeded)
            {
                item.UpdateTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                await ConfigHandler.AddSubItem(_config, item);
                await ConfigHandler.SaveConfig(_config);
                AppEvents.SubscriptionsRefreshRequested.Publish();
                AppEvents.ProfilesRefreshRequested.Publish();
                SetStatus($"«{item.Remarks}» обновлена {mode}. Профилей: {before} → {after}.", "SgSuccessBrush");
            }
            else
            {
                SetStatus($"Не удалось обновить «{item.Remarks}». Последние рабочие профили сохранены.", "SgErrorBrush");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SgSubscriptionsWindow.Update", ex);
            SetStatus($"Ошибка обновления «{item.Remarks}». Последние рабочие профили сохранены.", "SgErrorBrush");
        }
        finally
        {
            await RefreshSubscriptionsAsync();
            if (!keepBusyBetweenItems)
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        progressUpdate.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        btnSaveSubscription.IsEnabled = !busy;
        lstSubscriptions.IsEnabled = !busy;
    }

    private void SetStatus(string text, string brushKey)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text, brushKey));
            return;
        }

        txtStatus.Text = text;
        statusDot.Fill = (Brush)FindResource(brushKey);
    }

    private static string CleanStatusMessage(string message)
    {
        var value = message.Replace("-------------------------------------------------------", string.Empty).Trim();
        return value.Length > 220 ? value[..220] + "…" : value;
    }

    private static string BuildSafeUrlDisplay(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath;
        var display = $"{uri.Scheme}://{uri.Host}{path}";
        return display.Length > 86 ? display[..83] + "…" : display;
    }

    private static string GetSubscriptionCountText(int count)
    {
        var tail = count % 10 == 1 && count % 100 != 11
            ? "подписка"
            : count % 10 is >= 2 and <= 4 && (count % 100 < 10 || count % 100 >= 20)
                ? "подписки"
                : "подписок";
        return $"{count} {tail}";
    }

    private static string GetProfileCountText(int count)
    {
        return count == 1 ? "1 профиль" : $"{count} профилей";
    }

    private void ShowConfirmation(
        string title,
        string body,
        string actionText,
        Func<Task> action)
    {
        _confirmAction = action;
        txtConfirmTitle.Text = title;
        txtConfirmBody.Text = body;
        btnConfirmAction.Content = actionText;
        confirmationOverlay.Visibility = Visibility.Visible;
    }

    private void CancelConfirmation_Click(object sender, RoutedEventArgs e)
    {
        _confirmAction = null;
        confirmationOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _confirmAction;
        _confirmAction = null;
        confirmationOverlay.Visibility = Visibility.Collapsed;
        if (action != null)
        {
            await action();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        new SgHelpWindow("subscriptions") { Owner = this }.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class SgSubscriptionEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string UrlDisplay { get; init; } = string.Empty;
    public string StateText { get; init; } = string.Empty;
    public Brush StateBrush { get; init; } = Brushes.Gray;
    public string ProfileCountText { get; init; } = string.Empty;
    public string LastUpdateText { get; init; } = string.Empty;
}
