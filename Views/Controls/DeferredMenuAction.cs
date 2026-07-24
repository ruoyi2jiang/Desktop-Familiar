namespace DesktopFamiliar.Views.Controls;

/// <summary>
/// Holds a command selected from a menu until that menu reports that it has
/// fully closed. Deferring window activation past the close boundary prevents
/// the menu's keyboard-input mode from leaking into the newly activated WPF
/// window.
/// </summary>
public sealed class DeferredMenuAction
{
    private Action? _pendingAction;
    private long _dispatchGeneration;

    public bool HasPendingAction => _pendingAction is not null;

    public void Queue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pendingAction = action;
    }

    public void DispatchPending(Action<Action> dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        var action = _pendingAction;
        var dispatchGeneration = _dispatchGeneration;
        _pendingAction = null;
        if (action is not null)
        {
            dispatcher(() =>
            {
                if (dispatchGeneration == _dispatchGeneration)
                    action();
            });
        }
    }

    /// <summary>Prevents a callback already dispatched to the UI queue from running.</summary>
    public void InvalidateDispatchedAction() => _dispatchGeneration++;

    public void Clear()
    {
        _pendingAction = null;
        InvalidateDispatchedAction();
    }
}
