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
    private IMacAddressProvider? _macAddressProvider;
    private INetworkBindingGuard? _networkBindingGuard;
    private ICaptureTargetSettingsService? _captureTargetSettingsService;

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
        _macAddressProvider = new MacAddressProvider();
        _captureTargetSettingsService = new CaptureTargetSettingsService();
        _authService = new AuthService(_apiClient, _macAddressProvider, _captureTargetSettingsService);
        _networkBindingGuard = new NetworkBindingGuard(_apiClient, _macAddressProvider);

        if (_authService.IsLoggedIn)
        {
            ShowMainWindow();
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
            ShowMainWindow();
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
                ShowMainWindow();
                loginWindow.Close();
            });
            changePasswordWindow.Owner = loginWindow;
            changePasswordWindow.ShowDialog();
        });
        return vm;
    }

    private void ShowMainWindow()
    {
        var main = new MainWindow();
        main.DataContext = CreateMainViewModel(main);
        main.Loaded += MainWindow_OnFirstLoaded;
        main.Show();
    }

    private async void MainWindow_OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow w)
            w.Loaded -= MainWindow_OnFirstLoaded;

        if (_authService is not { IsLoggedIn: true })
            return;

        try
        {
            await _authService.EnsureClientSettingsFromServerIfMissingAsync().ConfigureAwait(false);
        }
        catch
        {
            // Same as login path: user can add targetSettings.json manually.
        }
    }

    private MainViewModel CreateMainViewModel(MainWindow mainWindow)
    {
        var config = new AppConfig();
        var automationService = new UIAutomationService();
        var dailyCheckExeService = new DailyCheckExeService();
        var withdrawOnlySettingsService = new WithdrawOnlySettingsService();
        var exchangeScoreSettingsService = new ExchangeScoreSettingsService();
        var dailyCheckSettingsService = new DailyCheckSettingsService();
        var withdrawRecordsService = new WithdrawRecordsService();
        var withdrawDailyService = new WithdrawDailyService();
        var autoAcceptOrderSettingsService = new AutoAcceptOrderSettingsService();
        var stopPlatformOrderingSettingsService = new StopPlatformOrderingSettingsService();
        var startPlatformOrderingSettingsService = new StartPlatformOrderingSettingsService();
        var vm = new MainViewModel(
            _authService!,
            _networkBindingGuard!,
            config,
            automationService,
            _captureTargetSettingsService!,
            withdrawOnlySettingsService,
            exchangeScoreSettingsService,
            dailyCheckExeService,
            dailyCheckSettingsService,
            withdrawRecordsService,
            withdrawDailyService,
            autoAcceptOrderSettingsService,
            stopPlatformOrderingSettingsService,
            startPlatformOrderingSettingsService);
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
        return vm;
    }
}
