using System.Windows;
using IpspoolAutomation.ViewModels;

namespace IpspoolAutomation.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ChangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangePasswordViewModel vm)
        {
            vm.Password = CurrentPasswordBox.Password;
            vm.NewPassword = NewPasswordBox.Password;
            vm.ConfirmPassword = ConfirmPasswordBox.Password;

            if (vm.ChangePasswordCommand.CanExecute(null))
                vm.ChangePasswordCommand.Execute(null);
        }
    }
}

