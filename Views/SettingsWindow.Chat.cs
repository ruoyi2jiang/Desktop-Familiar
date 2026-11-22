using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Views;

public partial class SettingsWindow
{
    private bool _refreshingChat;
    private CancellationTokenSource? _testOperation;
    private CancellationTokenSource? _gpuOperation;
    private string? _testingId;
    private enum SetupAction { Download, File, Folder }
    private string? SelectedModelId => (ChatModelChoice.SelectedItem as ComboBoxItem)?.Tag is AvailableChatModel model ? model.Id : null;
    private void ChatMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.Chat);

    private void RefreshChatSettings()
    {
        _refreshingChat = true;
        try
        {
            var chat = App.CurrentApp.Chat;
            RefreshHistorySize();
            var prefs = _manager.Data.Settings.Chat;
            var installed = chat.Models.AvailableModels();
            EnableChat.IsChecked = prefs.Enabled;
            ChatOptions.IsEnabled = prefs.Enabled;
            ChatOptions.Opacity = prefs.Enabled ? 1 : 0.5;
            ChatModelChoice.Items.Clear();
            foreach (var model in installed)
                ChatModelChoice.Items.Add(new ComboBoxItem { Content = model.Name, Tag = model, ToolTip = model.Path });
            var selected = ChatModelChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(
                item => item.Tag is AvailableChatModel model && model.Id == prefs.ModelId);
            if (selected == null)
            {
                selected = new ComboBoxItem { Content = LocalizationService.Get("Chat.ChooseModel"), IsEnabled = false };
                ChatModelChoice.Items.Insert(0, selected);
            }
            foreach (var (action, key) in new[] { (SetupAction.Download, "Chat.GetModel"), (SetupAction.File, "Chat.PickFile"), (SetupAction.Folder, "Chat.ScanFolder") })
                ChatModelChoice.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(key), Tag = action });
            ChatModelChoice.SelectedItem = selected;
            var selectedId = (selected.Tag as AvailableChatModel)?.Id;
            var entry = selectedId == null ? null : chat.Models.FindEntry(selectedId);
            ChatAvailability.Text = selectedId == null
                ? LocalizationService.Get(installed.Count == 0 ? "Chat.Unavailable" : "Chat.SelectModel")
                : LocalizationService.Get(entry?.StrengthsKey ?? "Chat.CustomModel");
            RefreshModelTest(prefs.Enabled ? selectedId : null);
            if (!prefs.Enabled) _gpuOperation?.Cancel();
            RefreshGpuSettings(chat.Models);
            ChatShortcut.Text = prefs.Shortcut;
            RefreshVoiceSettings(prefs);
            ShortcutHint.Text = LocalizationService.Get(chat.ShortcutAvailable ? "Chat.ShortcutHint" : "Chat.ShortcutUnavailable");
        }
        finally { _refreshingChat = false; }
    }

    private void RefreshGpuSettings(ChatModelStore models, string? notice = null)
    {
        ChatGpu.IsEnabled = _gpuOperation == null && _testOperation == null && ChatModelStore.Supported;
        ChatGpu.IsChecked = models.GpuEnabled;
        ChatGpuHint.Text = LocalizationService.Get(notice ?? (_gpuOperation != null ? "Chat.GpuPreparing"
            : models.GpuInstalled ? "Chat.GpuHint" : "Chat.GpuDownloadHint"));
    }

    private async void ChatGpu_Click(object sender, RoutedEventArgs e)
    {
        if (_gpuOperation != null) return;
        var models = App.CurrentApp.Chat.Models;
        bool enable = ChatGpu.IsChecked == true;
        using var operation = new CancellationTokenSource();
        _gpuOperation = operation;
        RefreshModelTest(SelectedModelId);
        RefreshGpuSettings(models);
        string? notice = null;
        try { await models.SetGpuEnabledAsync(enable, operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.Warning("Could not change chat GPU acceleration.", ex);
            notice = ex is NotSupportedException ? "Chat.GpuUnavailable" : "Chat.GpuFailed";
        }
        finally
        {
            _gpuOperation = null;
            RefreshGpuSettings(models, notice);
            RefreshModelTest(_manager.Data.Settings.Chat.Enabled ? SelectedModelId : null);
        }
    }

    private void RefreshModelTest(string? selectedId, string? notice = null, bool showDetails = false)
    {
        var testingSelection = _testOperation != null && _testingId == selectedId;
        if (!testingSelection) _testOperation?.Cancel();
        var chat = App.CurrentApp.Chat;
        var result = selectedId == null ? null : chat.ModelTests.Get(chat.Models.TestKey(selectedId), chat.Models.GpuEnabled);
        ModelSummary.Text = selectedId == null ? "" : ChatModelTestResults.Summary(chat.Models.FindEntry(selectedId), result);
        ModelSummary.Visibility = selectedId == null ? Visibility.Collapsed : Visibility.Visible;
        if (!ReferenceEquals(TestDetails.Tag, result))
        {
            TestDetails.Tag = result;
            TestDetails.IsExpanded = false;
            TestDetailText.Text = result == null ? "" : ChatModelTestResults.Details(result);
        }
        TestDetails.Visibility = result == null ? Visibility.Collapsed : Visibility.Visible;
        if (showDetails && result != null) TestDetails.IsExpanded = true;
        TestChatModel.IsEnabled = selectedId != null && ChatModelStore.Supported && _gpuOperation == null && (_testOperation == null || testingSelection);
        TestChatModel.Content = LocalizationService.Get(testingSelection ? "Chat.CancelTest" : "Chat.TestModel");
        var status = testingSelection ? LocalizationService.Get("Chat.TestRunning") : notice;
        ModelTestStatus.Text = status ?? "";
        ModelTestStatus.Visibility = status == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void TestChatModel_Click(object sender, RoutedEventArgs e)
    {
        if (_testOperation != null) { _testOperation.Cancel(); return; }
        if (ChatModelChoice.SelectedItem is not ComboBoxItem { Tag: AvailableChatModel model }
            || !_manager.Data.Settings.Chat.Enabled) return;
        using var operation = new CancellationTokenSource();
        _testOperation = operation;
        _testingId = model.Id;
        RefreshGpuSettings(App.CurrentApp.Chat.Models);
        RefreshModelTest(model.Id);
        string? notice = null;
        bool completed = false;
        try
        {
            var result = await App.CurrentApp.Chat.TestModelAsync(model.Id, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (result == null) notice = LocalizationService.Get("Chat.TestBusy");
            else completed = true;
        }
        catch (OperationCanceledException) { /* The user, a preference change, or a live chat turn ended the test. */ }
        catch (Exception ex)
        {
            AppLogger.Warning("The local chat model test failed.", ex);
            notice = LocalizationService.Get(ex is TimeoutException ? "Chat.TestTimedOut" : "Chat.TestFailed");
        }
        finally
        {
            _testOperation = null;
            _testingId = null;
            if (IsVisible && ChatPanel.IsVisible)
            {
                var id = _manager.Data.Settings.Chat.Enabled ? SelectedModelId : null;
                RefreshGpuSettings(App.CurrentApp.Chat.Models);
                RefreshModelTest(id, id == model.Id ? notice : null, completed && id == model.Id);
            }
        }
    }

    private void ApplyChat(ChatPreferences settings)
    {
        bool success;
        try { success = App.CurrentApp.Chat.SetPreferences(settings); }
        finally { RefreshChatSettings(); }
        if (!success) ShortcutHint.Text = LocalizationService.Get("Chat.ShortcutUnavailable");
    }

    private void EnableChat_Click(object sender, RoutedEventArgs e) =>
        ApplyChat(_manager.Data.Settings.Chat with { Enabled = EnableChat.IsChecked == true });

    private void ChatModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingChat && !ChatModelChoice.IsDropDownOpen) CommitModelChoice();
    }

    private void ChatModel_Closed(object? sender, EventArgs e) => CommitModelChoice();

    private void CommitModelChoice()
    {
        if (_refreshingChat || ChatModelChoice.SelectedItem is not ComboBoxItem choice) return;
        if (choice.Tag is AvailableChatModel model && model.Id != _manager.Data.Settings.Chat.ModelId)
            ApplyChat(_manager.Data.Settings.Chat with { ModelId = model.Id });
        if (choice.Tag is not SetupAction action) return;
        RefreshChatSettings();
        Dispatcher.InvokeAsync(() => OpenModelSetup(action), DispatcherPriority.ContextIdle);
    }

    private void OpenModelSetup(SetupAction action)
    {
        if (!IsVisible || !_manager.Data.Settings.Chat.Enabled) return;
        string? file = null;
        string? folder = null;
        if (action == SetupAction.File)
        {
            var open = new OpenFileDialog { Title = LocalizationService.Get("Chat.PickFile"), Filter = LocalizationService.Get("Chat.FileFilter") };
            if (open.ShowDialog(this) != true) return;
            file = open.FileName;
        }
        else if (action == SetupAction.Folder)
        {
            var open = new OpenFolderDialog { Title = LocalizationService.Get("Chat.ScanFolder") };
            if (open.ShowDialog(this) != true) return;
            folder = open.FolderName;
        }
        var chat = App.CurrentApp.Chat;
        var picker = new ChatModelWindow(chat.Models, file, folder, chat.ModelTests) { Owner = this };
        picker.ShowDialog();
        if (picker.InstalledId is { } id) ApplyChat(_manager.Data.Settings.Chat with { ModelId = id });
        else RefreshChatSettings();
    }

    private void ChatShortcut_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            (sender == TalkShortcut ? TalkShortcutHint : ShortcutHint).Text = LocalizationService.Get("Chat.ShortcutHint");
            return;
        }
        var gesture = new KeyGesture(key, Keyboard.Modifiers);
        var text = new KeyGestureConverter().ConvertToInvariantString(gesture)!;
        if (sender == TalkShortcut)
        {
            var ok = App.CurrentApp.Chat.SetPreferences(_manager.Data.Settings.Chat with { TalkShortcut = text });
            RefreshChatSettings();
            if (!ok) TalkShortcutHint.Text = LocalizationService.Get("Chat.ShortcutUnavailable");
        }
        else ApplyChat(_manager.Data.Settings.Chat with { Shortcut = text });
    }

    private void ManageHistory_Click(object sender, RoutedEventArgs e) =>
        new ChatHistoryWindow(_manager, App.CurrentApp.Chat, manage: true) { Owner = this }.ShowDialog();

    private void HistorySizeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(RefreshHistorySize);
            return;
        }
        RefreshHistorySize();
    }

    private void RefreshHistorySize()
    {
        if (ChatPanel.Visibility != Visibility.Visible) return;
        try
        {
            var size = App.CurrentApp.Chat.History.GetTotalSizeBytes();
            TotalHistorySize.Text = LocalizationService.Format("Chat.TotalHistorySize", FileSizeFormatter.Format(size));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("Could not read the size of the chat history file.", ex);
            TotalHistorySize.Text = LocalizationService.Get("Chat.HistorySizeUnavailable");
        }
    }

    private void ClearAllHistory_Click(object sender, RoutedEventArgs e)
    {
        var title = LocalizationService.Get("Chat.ClearAll");
        var dialog = new DeleteConfirmationWindow(title, LocalizationService.Get("Chat.ClearAllConfirm"), title) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { App.CurrentApp.Chat.ClearAll(); }
        catch (Exception ex)
        {
            AppLogger.Error("Could not clear all companion chat history.", ex);
            MessageBox.Show(this, LocalizationService.Get("Chat.ClearFailed"), title);
        }
    }
}
