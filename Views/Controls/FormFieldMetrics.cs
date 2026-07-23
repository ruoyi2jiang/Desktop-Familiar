using System.Windows;

namespace DesktopFamiliar.Views.Controls;

/// <summary>
/// Shared layout metrics for the app's form controls. Different WPF content
/// presenters need different raw padding values to reach the same visual text
/// origin, so callers should use the metric for their control template.
/// </summary>
public static class FormFieldMetrics
{
    public static Thickness TextInputPadding => new(4, 0, 4, 0);

    public static Thickness MultilineTextInputPadding => new(4, 8, 4, 8);

    public static Thickness SelectionInputPadding => new(8, 0, 8, 0);

    public static Thickness DatePickerInputPadding => new(8, 0, 4, 0);
}
