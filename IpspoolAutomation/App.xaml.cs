using System.Windows;
using IpspoolAutomation.Services;
using IpspoolAutomation.Views;
using IpspoolAutomation.ViewModels;
using System.Net.Http;
namespace IpspoolAutomation;

public partial class App : Application
{
    private IAuthService? _authService;
    private IApiClient? _apiClient;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var config = new AppConfig();
        var baseUrl = config.ApiBaseUrl.TrimEnd('/');
        var timeout = TimeSpan.FromSeconds(config.ApiTimeoutSeconds);

        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl + "/"),
            Timeout = timeout
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        _apiClient = new ApiClient(http);
        _authService = new AuthService(_apiClient);

        if (_authService.IsLoggedIn)
        {
            var main = new MainWindow();
            main.DataContext = CreateMainViewModel(main);
            main.Show();
        }
        else
        {
            var loginWindow = new LoginWindow();
            loginWindow.DataContext = CreateLoginViewModel(loginWindow);
            loginWindow.Show();
        }
    }

    private LoginViewModel CreateLoginViewModel(LoginWindow loginWindow)
    {
        var vm = new LoginViewModel(_authService!);
        vm.SetOnLoginSuccess(() =>
        {
            loginWindow.Hide();
            var main = new MainWindow();
            main.DataContext = CreateMainViewModel(main);
            main.Show();
            loginWindow.Close();
        });
        vm.SetOnOpenRegister(() =>
        {
            var registerWindow = new RegisterWindow();
            registerWindow.DataContext = new RegisterViewModel(_authService!);
            ((RegisterViewModel)registerWindow.DataContext).SetOnRegisterSuccess(() =>
            {
                registerWindow.Close();
                loginWindow.Show();
            });
            registerWindow.ShowDialog();
        });
        vm.SetOnOpenChangePassword(() =>
        {
            var changePasswordWindow = new ChangePasswordWindow();
            changePasswordWindow.DataContext = new ChangePasswordViewModel(_authService!, _apiClient!);
            ((ChangePasswordViewModel)changePasswordWindow.DataContext).SetOnChangeSuccess(() =>
            {
                changePasswordWindow.Close();
                loginWindow.Hide();
                var main = new MainWindow();
                main.DataContext = CreateMainViewModel(main);
                main.Show();
                loginWindow.Close();
            });
            changePasswordWindow.Owner = loginWindow;
            changePasswordWindow.ShowDialog();
        });
        return vm;
    }

    private MainViewModel CreateMainViewModel(MainWindow mainWindow)
    {
        var config = new AppConfig();
        var automationService = new UIAutomationService();
        var captureTargetSettingsService = new CaptureTargetSettingsService();
        var vm = new MainViewModel(_authService!, config, automationService, captureTargetSettingsService);
        vm.SetOnLogout(() =>
        {
            mainWindow.Hide();
            var loginWindow = new LoginWindow();
            loginWindow.DataContext = CreateLoginViewModel(loginWindow);
            loginWindow.Closed += (_, _) =>
            {
                if (!_authService!.IsLoggedIn)
                    Shutdown();
            };
            loginWindow.Show();
            mainWindow.Close();
        });
        vm.SetOnOpenSubscribe(() =>
        {
            var subscribeWindow = new SubscribeWindow();
            subscribeWindow.DataContext = new SubscribeViewModel(_apiClient!);
            subscribeWindow.Owner = mainWindow;
            subscribeWindow.ShowDialog();
        });
        return vm;
    }
}
