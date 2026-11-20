using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Views;

namespace DesktopFamiliar.Services.Chat;

public sealed class ChatController : IDisposable
{
    private readonly CompanionManager _manager;
    private readonly CompanionOverlayWindow _overlay;
    private readonly ChatHotkey _hotkey = new();
    private readonly ChatHotkey _talkKey = new();
    private readonly ChatSession _session;
    private CancellationTokenSource? _turn;
    private ChatPromptWindow? _prompt;
    private bool _disposed;

    public ChatController(CompanionManager manager, CompanionOverlayWindow overlay, ChatModelStore models, ChatHistoryStore history)
    {
        _manager = manager;
        _overlay = overlay;
        Models = models;
        History = history;
        _session = new(history, new LlamaChatEngine(models));
        _hotkey.Pressed += (_, _) =>
        {
            if (_prompt != null) _prompt.Close();
            else OpenPrompt();
        };
        _manager.ChatSettingsChanged += Reset;
        _talkKey.Pressed += (_, _) =>
        {
            if (!Available || !_manager.Data.Settings.Chat.VoiceEnabled) return;
            OpenPrompt();
            _prompt?.BeginTalk(() => _talkKey.IsHeld);
        };
        _manager.CurrentCompanionChanged += Reset;
        _manager.FocusModeChanged += Reset;
        Reset(this, EventArgs.Empty);
    }

    public ChatModelStore Models { get; }
    public ChatHistoryStore History { get; }
    public bool Available => AvailableFor(_manager.Data.Settings.Chat);
    public bool ShortcutAvailable { get; private set; }
    public bool TalkShortcutAvailable { get; private set; }

    private bool AvailableFor(ChatPreferences prefs) => !_disposed && prefs.Enabled
        && _manager.CurrentCompanion != null && !_manager.Data.Settings.FocusModeEnabled && Models.Installed(prefs.ModelId);

    private string? ActiveShortcut(ChatPreferences prefs) => AvailableFor(prefs) ? prefs.Shortcut : null;

    public bool SetPreferences(ChatPreferences prefs)
    {
        if (string.Equals(prefs.TalkShortcut, prefs.Shortcut, StringComparison.OrdinalIgnoreCase)) return false;
        if (!_hotkey.Set(ActiveShortcut(prefs)) && prefs.Shortcut != _manager.Data.Settings.Chat.Shortcut) return false;
        if (!_talkKey.Set(AvailableFor(prefs) && prefs.VoiceEnabled ? prefs.TalkShortcut : null)
            && prefs.TalkShortcut != _manager.Data.Settings.Chat.TalkShortcut)
        {
            _hotkey.Set(ActiveShortcut(_manager.Data.Settings.Chat));
            return false;
        }
        try { _manager.SetChatPreferences(prefs); }
        catch { Reset(this, EventArgs.Empty); throw; }
        return true;
    }

    private void Reset(object? sender, EventArgs args)
    {
        _turn?.Cancel();
        _session.Select(Available ? _manager.CurrentCompanion?.Id : null);
        _prompt?.Close();
        _overlay.ResetChat();
        var prefs = _manager.Data.Settings.Chat;
        ShortcutAvailable = _hotkey.Set(ActiveShortcut(prefs));
        TalkShortcutAvailable = _talkKey.Set(Available && prefs.VoiceEnabled ? prefs.TalkShortcut : null);
    }

    public void Clear(IReadOnlyCollection<Guid> ids, ChatHistoryClearScope scope)
    {
        if (_manager.CurrentCompanion is { } pet && ids.Contains(pet.Id)) Reset(this, EventArgs.Empty);
        History.Clear(ids, scope);
    }

    public void ClearAll()
    {
        Reset(this, EventArgs.Empty);
        History.ClearAll();
    }

    public void OpenPrompt()
    {
        if (!Available) return;
        if (_prompt == null)
        {
            _prompt = new(_overlay, _manager.CurrentCompanion!, SendAsync, _manager.Data.Settings.Chat);
            _prompt.Closed += (_, _) => _prompt = null;
            _prompt.Show();
        }
        _prompt.FocusPrompt();
    }

    private async Task<bool> SendAsync(string prompt)
    {
        if (!Available || _turn is { IsCancellationRequested: false } || _manager.CurrentCompanion is not { } pet) return false;
        using var turn = new CancellationTokenSource();
        _turn = turn;
        try
        {
            var answer = _session.SendReplyAsync(pet.Clone(), _manager.Data.Settings.Chat.ModelId!, prompt, turn.Token);
            if (await Task.WhenAny(answer, Task.Delay(200, turn.Token)) != answer && !turn.IsCancellationRequested)
                _overlay.SetThinking(string.IsNullOrWhiteSpace(pet.ThinkingPhrase) ? LocalizationService.Get("Chat.Thinking") : pet.ThinkingPhrase);
            var reply = await answer;
            if (turn.IsCancellationRequested) return false;
            _overlay.QueueReply(reply.Text, expressionSceneId: reply.ExpressionSceneId);
            return true;
        }
        catch (OperationCanceledException) when (turn.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            AppLogger.Error("Local companion chat failed.", ex);
            if (!turn.IsCancellationRequested) _overlay.QueueReply(LocalizationService.Get("Chat.ReplyFailed"), animate: false);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_turn, turn))
            {
                _turn = null;
                _overlay.SetThinking(null);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.ChatSettingsChanged -= Reset;
        _manager.CurrentCompanionChanged -= Reset;
        _manager.FocusModeChanged -= Reset;
        Reset(this, EventArgs.Empty);
        _hotkey.Dispose();
        _talkKey.Dispose();
    }
}
