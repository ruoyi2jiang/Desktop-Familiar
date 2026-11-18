using System.Windows.Input;
using DesktopFamiliar.Models;
using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Views;

public partial class ChatPromptWindow : Window
{
    private readonly Func<string, Task<bool>> _send;

    public ChatPromptWindow(CompanionOverlayWindow overlay, CompanionDefinition pet, Func<string, Task<bool>> send)
    {
        InitializeComponent();
        _send = send;
        Owner = overlay;
        CompanionBubbleAppearance.Apply(Surface, pet);
        Loaded += (_, _) => { overlay.SetChatPrompt(this); Input.Focus(); };
        Closed += (_, _) => overlay.SetChatPrompt(null);
    }

    public void FocusPrompt() { Activate(); Keyboard.Focus(Input); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Prompt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.Enter && Input.IsKeyboardFocused) { e.Handled = true; await SubmitAsync(); }
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0 || !SendButton.IsEnabled) return;
        Input.Clear();
        SendButton.IsEnabled = false;
        Input.Focus();
        try
        {
            if (!await _send(text) && Input.Text.Length == 0) Input.Text = text;
        }
        finally { SendButton.IsEnabled = true; }
    }
}
