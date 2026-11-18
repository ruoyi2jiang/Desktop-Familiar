using System.Windows.Automation;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Views;

public partial class ChatHistoryWindow : Window
{
    private readonly CompanionManager _manager;
    private readonly ChatController _chat;
    private readonly bool _manage;

    public ChatHistoryWindow(CompanionManager manager, ChatController chat, bool manage = false)
    {
        InitializeComponent();
        _manager = manager;
        _chat = chat;
        _manage = manage;
        Title = Heading.Text = LocalizationService.Get(manage ? "Chat.ManageTitle" : "Chat.History");
        if (manage)
        {
            MinWidth = 300;
            Width = 344;
            MinHeight = 170;
            MaxHeight = Math.Min(520, SystemParameters.WorkArea.Height);
            SizeToContent = SizeToContent.Height;
            Surface.Padding = new Thickness(22, 20, 16, 16);
            Heading.Margin = new Thickness(0, 0, 0, 10);
            Heading.FontSize = 22;
            Heading.TextWrapping = TextWrapping.Wrap;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Pets.Visibility = Divider.Visibility = Timeline.Visibility = Visibility.Collapsed;
            ManageScroll.Visibility = Visibility.Visible;
        }
        Refresh();
        _chat.History.Changed += Changed;
        _manager.CompanionCollectionChanged += Changed;
        Closed += (_, _) => { _chat.History.Changed -= Changed; _manager.CompanionCollectionChanged -= Changed; };
    }

    private void Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.InvokeAsync(Refresh);
    }

    private void Refresh()
    {
        try
        {
            var selected = (Pets.SelectedItem as ListBoxItem)?.Tag as Guid? ?? _manager.CurrentCompanion?.Id;
            var items = _manage ? ManagedPets.Items : Pets.Items;
            items.Clear();
            foreach (var (id, saved) in _chat.History.ListPets().OrderBy(entry => entry.Value.Name))
            {
                var pet = _manager.Data.Companions.FirstOrDefault(c => c.Id == id);
                var name = pet?.Name ?? saved.Name;
                var sizeText = _manage ? FileSizeFormatter.Format(saved.SizeBytes) : null;
                var row = new ChatHistoryPetRow(name,
                    pet == null ? saved.Portrait?.PicturePath : pet.ProfilePicturePath,
                    pet == null ? saved.Portrait?.IdleFramePath : pet.IdleDefaultAnimation?.GetPreviewPath(),
                    sizeText, dividerBefore: items.Count > 0);
                if (_manage)
                {
                    row.ClearButton.Click += (_, _) => ClearPet(id, name);
                    items.Add(row);
                    continue;
                }

                var item = new ListBoxItem { Tag = id, Content = row };
                AutomationProperties.SetName(item, name);
                items.Add(item);
                if (id == selected) Pets.SelectedItem = item;
            }
            if (!_manage && Pets.SelectedIndex < 0 && Pets.Items.Count > 0) Pets.SelectedIndex = 0;
            Status.Text = LocalizationService.Get("Chat.EmptyHistory");
            Status.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        Status.Text = LocalizationService.Get("Chat.ReadFailed");
        Status.Visibility = Visibility.Visible;
    }

    private void Render()
    {
        if (_manage) return;
        Messages.Children.Clear();
        if (Pets.SelectedItem is not ListBoxItem { Tag: Guid id }) return;
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

    private void ClearPet(Guid id, string name)
    {
        try
        {
            var saved = _chat.History.ForPet(id);
            bool notesOnly = saved.Turns.Count == 0;
            var dialog = new DeleteConfirmationWindow(
                LocalizationService.Format("Chat.ClearPet", name),
                LocalizationService.Format(notesOnly ? "Chat.ClearPetNotesConfirm" : "Chat.ClearPetConfirm", name),
                LocalizationService.Get("Common.Delete"),
                !notesOnly && saved.Notes.Count > 0 ? LocalizationService.Get("Chat.ClearNotes") : null,
                optionSelected: true) { Owner = this };
            if (dialog.ShowDialog() == true) _chat.Clear([id], saved.GetClearScope(dialog.OptionSelected));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Could not clear the companion's history.", ex);
            MessageBox.Show(this, LocalizationService.Get("Chat.ClearFailed"), Title);
        }
    }
}
