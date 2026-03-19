using System.Windows;
using System.Windows.Input;

namespace IpspoolAutomation.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.LoginViewModel vm)
        {
            vm.Password = PasswordBox.Password;
            if (vm.LoginCommand.CanExecute(null))
                vm.LoginCommand.Execute(null);
        }
    }
}
