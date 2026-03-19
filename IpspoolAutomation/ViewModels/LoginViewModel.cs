using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Models.Auth;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private Action? _onLoginSuccess;
    private Action? _onOpenRegister;
    private Action? _onOpenChangePassword;

    [ObservableProperty] private string _account = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isLoading;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    public void SetOnLoginSuccess(Action callback) => _onLoginSuccess = callback;
    public void SetOnOpenRegister(Action callback) => _onOpenRegister = callback;
    public void SetOnOpenChangePassword(Action callback) => _onOpenChangePassword = callback;

    [RelayCommand]
    private void OpenRegister()
    {
        _onOpenRegister?.Invoke();
    }

    [RelayCommand]
    private void OpenChangePassword()
    {
        _onOpenChangePassword?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = "";
        IsLoading = true;
        LoginCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _authService.LoginAsync(Account, Password, cancellationToken).ConfigureAwait(true);
            if (result.Success)
            {
                _onLoginSuccess?.Invoke();
            }
            else
            {
                ErrorMessage = result.Message ?? "登录失败";
            }
        }
        finally
        {
            IsLoading = false;
            LoginCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLogin() => !IsLoading && !string.IsNullOrWhiteSpace(Account) && !string.IsNullOrWhiteSpace(Password);
}
