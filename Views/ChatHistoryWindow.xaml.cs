using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Views;

public partial class ChatHistoryWindow : Window
{
    private readonly CompanionManager _manager;
    private readonly ChatController _chat;
    private readonly bool _clear;
    public ChatHistoryWindow(CompanionManager manager, ChatController chat, bool clear = false)
    {
        InitializeComponent();
        _manager = manager;
        _chat = chat;
        _clear = clear;
        Title = Heading.Text = LocalizationService.Get(clear ? "Chat.ClearHistory" : "Chat.History");
        ClearPanel.Visibility = clear ? Visibility.Visible : Visibility.Collapsed;
        Pets.SelectionMode = clear ? SelectionMode.Multiple : SelectionMode.Single;
        Refresh();
        _chat.History.Changed += Changed;
        _manager.CompanionCollectionChanged += Changed;
        Closed += (_, _) => { _chat.History.Changed -= Changed; _manager.CompanionCollectionChanged -= Changed; };
    }

    private void Changed(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        try
        {
            var selected = (Pets.SelectedItem as ListBoxItem)?.Tag as Guid? ?? _manager.CurrentCompanion?.Id;
            Pets.Items.Clear();
            foreach (var (id, name) in _chat.History.Names().OrderBy(entry => entry.Value))
            {
                var pet = _manager.Data.Companions.FirstOrDefault(c => c.Id == id);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = pet == null ? null : CompanionCard.LoadIdlePreview(pet);
                if (icon != null) row.Children.Add(new Image { Source = icon, Width = 32, Height = 32 });
                else row.Children.Add(new TextBlock { Text = LocalizationService.Get("Chat.PetIcon"), Width = 32, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = pet?.Name ?? name, Margin = new Thickness(6,0,0,0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                var item = new ListBoxItem { Tag = id, Content = row };
                Pets.Items.Add(item);
                if (!_clear && id == selected) Pets.SelectedItem = item;
            }
            if (!_clear && Pets.SelectedIndex < 0 && Pets.Items.Count > 0) Pets.SelectedIndex = 0;
            Render();
        }
        catch (Exception ex) { ShowReadFailure(ex); }
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try { Render(); }
        catch (Exception ex) { ShowReadFailure(ex); }
    }

    private void ShowReadFailure(Exception ex)
    {
        AppLogger.Error("Could not open local chat history.", ex);
        Messages.Children.Clear();
        Messages.Children.Add(new TextBlock { Text = LocalizationService.Get("Chat.ReadFailed") });
    }

    private void Render()
    {
        Messages.Children.Clear();
        ClearButton.IsEnabled = Pets.SelectedItems.Count > 0;
        if (_clear || Pets.SelectedItem is not ListBoxItem { Tag: Guid id })
        {
            Messages.Children.Add(new TextBlock { Text = LocalizationService.Get(Pets.Items.Count == 0 ? "Chat.Empty" : "Chat.SelectClear"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var pet = _manager.Data.Companions.FirstOrDefault(p => p.Id == id);
        var conversation = _chat.History.ForPet(id);
        if (conversation.Turns.Count == 0) Messages.Children.Add(new TextBlock { Text = LocalizationService.Get("Chat.Empty") });
        var appearance = new Border();
        CompanionBubbleAppearance.Apply(appearance, pet);
        foreach (var turn in conversation.Turns)
        {
            AddMessage(turn.Prompt, true, appearance);
            AddMessage(turn.Reply, false, appearance);
        }
        Timeline.ScrollToBottom();
    }

    private void AddMessage(string text, bool user, Border appearance)
    {
        var border = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12,8,12,8),
            Margin = user ? new Thickness(36,4,0,4) : new Thickness(0,4,36,4),
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        if (user)
        {
            border.SetResourceReference(Border.BackgroundProperty, ThemeTokens.PrimaryAccent);
            border.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ThemeTokens.PrimaryTextOnAccent);
        }
        else
        {
            border.Background = appearance.Background;
            border.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, appearance.GetValue(System.Windows.Documents.TextElement.ForegroundProperty));
            border.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, appearance.GetValue(System.Windows.Documents.TextElement.FontFamilyProperty));
        }
        border.Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Messages.Children.Add(border);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var ids = Pets.SelectedItems.Cast<ListBoxItem>().Select(item => (Guid)item.Tag).ToArray();
        if (ids.Length == 0 || MessageBox.Show(this, LocalizationService.Format("Chat.ClearConfirm", ids.Length),
            Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _chat.Clear(ids, IncludeNotes.IsChecked == true); }
        catch (Exception ex)
        {
            AppLogger.Error("Could not clear selected chat histories.", ex);
            MessageBox.Show(this, LocalizationService.Get("Chat.ClearFailed"), Title);
        }
    }
}
