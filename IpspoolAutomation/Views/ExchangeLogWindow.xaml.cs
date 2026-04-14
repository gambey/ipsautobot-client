using System.Windows;
using System.Windows.Controls;

namespace IpspoolAutomation.Views;

public partial class ExchangeLogWindow : Window
{
    public ExchangeLogWindow()
    {
        InitializeComponent();
    }

    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.ScrollToEnd();
    }
}
