using System.Diagnostics;
using System.Windows.Threading;
using System.Drawing;
using DesktopFamiliar.Services;
using MonitorScreen = System.Windows.Forms.Screen;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Views;

public partial class CompanionOverlayWindow
{
    private readonly Queue<string> _chatPages = new();
    private readonly DispatcherTimer _chatClock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _readClock = new();
    private string? _reading;
    private string? _thinking;

    private ChatPromptWindow? _chatPrompt;
    private bool _promptLayoutQueued;

    internal void SetChatPrompt(ChatPromptWindow? prompt)
    {
        _chatPrompt = prompt;
        UpdateDialogAreaForContent();
    }

    private void QueuePromptLayout()
    {
        if (_chatPrompt == null || _promptLayoutQueued) return;
        _promptLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _promptLayoutQueued = false;
            if (_chatPrompt != null) UpdateDialogAreaForContent();
        }));
    }

    private double ArrangeChatPrompt(double characterTop, double dialogueHeight)
    {
        if (_chatPrompt == null) return 0;
        var settings = _manager.Data.Settings;
        var character = new RectangleF((float)(Left + OverlayLayoutPolicy.GetCharacterHorizontalOffset(settings.ViewportWidth)),
            (float)characterTop, settings.ViewportWidth, settings.ViewportHeight);
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new System.Drawing.Point((int)Math.Round((Left + Width / 2) * dpi.DpiScaleX),
            (int)Math.Round((characterTop + character.Height / 2) * dpi.DpiScaleY));
        var pixels = MonitorScreen.FromPoint(center).WorkingArea;
        var workArea = new RectangleF((float)(pixels.Left / dpi.DpiScaleX), (float)(pixels.Top / dpi.DpiScaleY),
            (float)(pixels.Width / dpi.DpiScaleX), (float)(pixels.Height / dpi.DpiScaleY));
        var placement = OverlayLayoutPolicy.PlaceChatPrompt(character, workArea, _chatPrompt.Height, dialogueHeight);
        _chatPrompt.Width = placement.Width;
        _chatPrompt.Left = placement.Left;
        _chatPrompt.Top = placement.Top;
        return placement.DialogueClearance;
    }

    public void SetThinking(string? text)
    {
        _thinking = text;
        RenderChat();
    }

    public void QueueReply(string reply)
    {
        _thinking = null;
        foreach (var page in ChatBubbleTiming.Pages(reply)) _chatPages.Enqueue(page);
        RenderChat();
    }

    public void ResetChat()
    {
        _chatPages.Clear();
        _reading = _thinking = null;
        _readClock.Reset();
        RenderChat();
    }

    private void RenderChat()
    {
        if (_reading != null && _readClock.Elapsed >= ChatBubbleTiming.ReadingTime(_reading))
        {
            _reading = null;
            _readClock.Reset();
        }
        if (_reading == null && _chatPages.TryDequeue(out var page)) _reading = page;
        var text = _reading ?? _thinking;
        var visible = text != null && IsVisible && BubbleRoot.Visibility != Visibility.Visible;
        if (visible && _reading != null) _readClock.Start();
        else _readClock.Stop();
        ChatReplyBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ChatReplyText.Text = text ?? string.Empty;
        if (text == null) _chatClock.Stop();
        else _chatClock.Start();
        UpdateDialogAreaForContent();
    }
}
