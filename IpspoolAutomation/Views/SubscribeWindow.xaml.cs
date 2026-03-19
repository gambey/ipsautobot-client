using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IpspoolAutomation.Views;

public partial class SubscribeWindow : Window
{
    public SubscribeWindow()
    {
        InitializeComponent();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (DataContext is ViewModels.SubscribeViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.SubscribeViewModel.QrCodeUrl) && DataContext is ViewModels.SubscribeViewModel vm && !string.IsNullOrEmpty(vm.QrCodeUrl))
        {
            try
            {
                QrImage.Source = new BitmapImage(new Uri(vm.QrCodeUrl));
            }
            catch { /* ignore */ }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
