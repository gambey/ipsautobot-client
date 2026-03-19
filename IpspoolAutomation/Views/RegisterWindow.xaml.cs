using System.Windows;
using System.Windows.Input;

namespace IpspoolAutomation.Views;

public partial class RegisterWindow : Window
{
    public RegisterWindow()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RegisterViewModel vm)
        {
            vm.Password = PasswordBox.Password;
            vm.ConfirmPassword = ConfirmPasswordBox.Password;
            if (vm.RegisterCommand.CanExecute(null))
                vm.RegisterCommand.Execute(null);
        }
    }
}
