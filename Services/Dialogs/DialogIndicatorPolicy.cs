namespace DesktopFamiliar.Services.Dialogs;

public enum DialogRestoreIndicatorTone
{
    Active,
    Inactive
}

public readonly record struct DialogIndicatorPresentation(
    bool ShowBubble,
    bool ShowHideButton,
    bool ShowRestoreButton,
    DialogRestoreIndicatorTone RestoreTone);

/// <summary>
/// Resolves the bubble and restore-indicator presentation without depending on
/// WPF controls. Normal dialogue and reminders deliberately use the same rule.
/// </summary>
public static class DialogIndicatorPolicy
{
    public static DialogIndicatorPresentation Resolve(
        bool hasActiveDialog,
        bool hasDialogContent,
        bool dialogHidden,
        bool restoreIndicatorDismissed)
    {
        var hasPresentableDialog = hasActiveDialog && hasDialogContent;
        var showBubble = hasPresentableDialog && !dialogHidden;
        var showRestoreButton = dialogHidden && !restoreIndicatorDismissed;

        return new DialogIndicatorPresentation(
            ShowBubble: showBubble,
            ShowHideButton: showBubble,
            ShowRestoreButton: showRestoreButton,
            RestoreTone: hasPresentableDialog
                ? DialogRestoreIndicatorTone.Active
                : DialogRestoreIndicatorTone.Inactive);
    }
}
