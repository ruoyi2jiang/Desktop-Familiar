using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Tests;

public sealed class FormFieldMetricsTests
{
    [Fact]
    public void TextInputPadding_CompensatesForItsDifferentContentHost()
    {
        Assert.Equal(4, FormFieldMetrics.TextInputPadding.Left);
        Assert.Equal(4, FormFieldMetrics.MultilineTextInputPadding.Left);
        Assert.Equal(8, FormFieldMetrics.SelectionInputPadding.Left);
        Assert.Equal(8, FormFieldMetrics.DatePickerInputPadding.Left);
    }
}
