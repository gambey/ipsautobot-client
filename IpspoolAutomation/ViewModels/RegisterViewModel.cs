using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Models.Auth;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.ViewModels;

public sealed partial class RegisterViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private Action? _onRegisterSuccess;

    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _successMessage = "";
    [ObservableProperty] private bool _isLoading;

    public RegisterViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    public void SetOnRegisterSuccess(Action callback) => _onRegisterSuccess = callback;

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = "";
        SuccessMessage = "";
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "两次密码不一致";
            return;
        }
        IsLoading = true;
        RegisterCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _authService.RegisterAsync(new RegisterRequest(UserName, Password), cancellationToken).ConfigureAwait(true);
            if (result.Success)
            {
                SuccessMessage = "注册成功，请登录";
                _onRegisterSuccess?.Invoke();
            }
            else
            {
                ErrorMessage = result.Message ?? "注册失败";
            }
        }
        finally
        {
            IsLoading = false;
            RegisterCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRegister() => !IsLoading && !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);
}
