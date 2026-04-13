using System.Windows;
using System.Windows.Controls;
using IpspoolAutomation.ViewModels;

namespace IpspoolAutomation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncPaymentPasswordBoxFromVm();
        DataContextChanged += (_, _) => SyncPaymentPasswordBoxFromVm();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }

    private void TogglePaymentPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.IsPaymentPasswordVisible = !vm.IsPaymentPasswordVisible;
        if (!vm.IsPaymentPasswordVisible)
            SyncPaymentPasswordBoxFromVm();
    }

    private void PaymentPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not PasswordBox pb)
            return;
        if (vm.PaymentPassword != pb.Password)
            vm.PaymentPassword = pb.Password;
    }

    private void SyncPaymentPasswordBoxFromVm()
    {
        if (DataContext is not MainViewModel vm || PaymentPasswordBox == null)
            return;
        if (PaymentPasswordBox.Password != (vm.PaymentPassword ?? ""))
            PaymentPasswordBox.Password = vm.PaymentPassword ?? "";
    }
}
