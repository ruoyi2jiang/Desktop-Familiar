using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Views;

public partial class SettingsWindow
{
    private bool _refreshingChat;
    private enum SetupAction { Download, File, Folder }
    private void ChatMenu_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage.Chat);

    private void RefreshChatSettings()
    {
        _refreshingChat = true;
        try
        {
            var chat = App.CurrentApp.Chat;
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
            ChatAvailability.Text = LocalizationService.Get(installed.Count == 0 ? "Chat.Unavailable"
                : selected.Tag == null ? "Chat.SelectModel" : "Chat.Local");
            ChatShortcut.Text = prefs.Shortcut;
            ShortcutHint.Text = LocalizationService.Get(chat.ShortcutAvailable ? "Chat.ShortcutHint" : "Chat.ShortcutUnavailable");
        }
        finally { _refreshingChat = false; }
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
        var picker = new ChatModelWindow(App.CurrentApp.Chat.Models, file, folder) { Owner = this };
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
            ShortcutHint.Text = LocalizationService.Get("Chat.ShortcutHint");
            return;
        }
        var gesture = new KeyGesture(key, Keyboard.Modifiers);
        var text = new KeyGestureConverter().ConvertToInvariantString(gesture)!;
        ApplyChat(_manager.Data.Settings.Chat with { Shortcut = text });
    }

    private void ClearChatHistory_Click(object sender, RoutedEventArgs e) =>
        new ChatHistoryWindow(_manager, App.CurrentApp.Chat, clear: true) { Owner = this }.ShowDialog();
}
