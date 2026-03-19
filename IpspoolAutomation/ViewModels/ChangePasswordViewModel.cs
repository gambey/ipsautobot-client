using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Models.Auth;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.ViewModels;

public sealed partial class ChangePasswordViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IApiClient _apiClient;
    private Action? _onChangeSuccess;

    [ObservableProperty] private string _account = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _successMessage = "";
    [ObservableProperty] private bool _isLoading;

    public ChangePasswordViewModel(IAuthService authService, IApiClient apiClient)
    {
        _authService = authService;
        _apiClient = apiClient;
    }

    public void SetOnChangeSuccess(Action callback) => _onChangeSuccess = callback;

    [RelayCommand(CanExecute = nameof(CanChangePassword))]
    private async Task ChangePasswordAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = "";
        SuccessMessage = "";

        if (string.IsNullOrWhiteSpace(NewPassword) || string.IsNullOrWhiteSpace(ConfirmPassword))
        {
            ErrorMessage = "请输入新密码并确认";
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "两次密码不一致";
            return;
        }

        IsLoading = true;
        ChangePasswordCommand.NotifyCanExecuteChanged();
        try
        {
            // API: PUT /api/users/password requires bearerAuth, so we login first to obtain token.
            var loginResult = await _authService.LoginAsync(Account, Password, cancellationToken).ConfigureAwait(true);
            if (!loginResult.Success)
            {
                ErrorMessage = loginResult.Message ?? "登录失败";
                return;
            }

            var result = await _apiClient.ChangePasswordAsync(
                new ChangePasswordRequest { NewPassword = NewPassword },
                cancellationToken).ConfigureAwait(true);

            if (result.Success)
            {
                SuccessMessage = result.Message ?? "修改成功";
                _onChangeSuccess?.Invoke();
            }
            else
            {
                ErrorMessage = result.Message ?? "修改失败";
            }
        }
        finally
        {
            IsLoading = false;
            ChangePasswordCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanChangePassword()
    {
        if (IsLoading) return false;
        return !string.IsNullOrWhiteSpace(Account)
            && !string.IsNullOrWhiteSpace(Password)
            && !string.IsNullOrWhiteSpace(NewPassword)
            && !string.IsNullOrWhiteSpace(ConfirmPassword);
    }
}

