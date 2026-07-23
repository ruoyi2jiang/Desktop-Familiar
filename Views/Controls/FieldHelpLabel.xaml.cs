namespace DesktopFamiliar.Views.Controls;

public partial class FieldHelpLabel : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(FieldHelpLabel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
        nameof(HelpText),
        typeof(string),
        typeof(FieldHelpLabel),
        new PropertyMetadata(string.Empty));

    public FieldHelpLabel() => InitializeComponent();

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string HelpText
    {
        get => (string)GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }
}
