using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Automation;
using IpspoolAutomation.Services;
using Microsoft.Win32;

namespace IpspoolAutomation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAppConfig _config;
    private readonly XunjieAutomationWorkflow _workflow;
    private readonly IAutomationService _automationService;
    private CancellationTokenSource? _cts;
    private Action? _onLogout;
    private Action? _onOpenSubscribe;

    [ObservableProperty] private string _userInfo = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _currentPage = "兑换讯币";

    [ObservableProperty] private string _subscriptionStatusText = "未订阅服务";
    [ObservableProperty] private bool _isSubscribed;
    [ObservableProperty] private string _expireAtText = "--";

    [ObservableProperty] private string _exchangeAmount = "100";
    [ObservableProperty] private string _exchangePoints = "100000";
    [ObservableProperty] private bool _autoWithdrawAfterExchange;
    [ObservableProperty] private decimal _totalExchangedCoins;
    [ObservableProperty] private decimal _equivalentAmount;
    [ObservableProperty] private string _exchangeSettingsHint = "";
    [ObservableProperty] private bool _isExchangeSettingsHintVisible;

    [ObservableProperty] private string _withdrawStatusMessage = "提现流程待接入自动化步骤。";
    [ObservableProperty] private string _subscribePlan = "Monthly";
    [ObservableProperty] private string _subscribeStatusMessage = "";
    [ObservableProperty] private string _merchantPath = "";
    [ObservableProperty] private string _helperPath = "";
    [ObservableProperty] private string _withdrawName = "";
    [ObservableProperty] private string _alipayAccount = "";
    [ObservableProperty] private bool _isAgentPayMode = true;
    [ObservableProperty] private bool _isRegionalAgentMode;
    [ObservableProperty] private bool _isFeeFromPoints = true;
    [ObservableProperty] private string _settingsSaveStatus = "";

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ExchangeRecordItem> ExchangeRecords { get; } = new();
    public ObservableCollection<WithdrawRecordItem> WithdrawRecords { get; } = new();
    private static readonly string LocalSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "ui-settings.json");

    public MainViewModel(
        IAuthService authService,
        IAppConfig config,
        IAutomationService automationService)
    {
        _authService = authService;
        _config = config;
        _automationService = automationService;
        _workflow = new XunjieAutomationWorkflow(automationService, _config.XunjieHelperPath, _config.XunjieMerchantPath);
        UserInfo = _authService.UserName ?? "用户";
        MerchantPath = _config.XunjieMerchantPath;
        HelperPath = _config.XunjieHelperPath;
        SeedMockRecords();
        LoadLocalSettings();
    }

    public void SetOnLogout(Action callback) => _onLogout = callback;
    public void SetOnOpenSubscribe(Action callback) => _onOpenSubscribe = callback;

    public bool IsExchangePage => CurrentPage == "兑换讯币";
    public bool IsWithdrawPage => CurrentPage == "提现";
    public bool IsWithdrawRecordsPage => CurrentPage == "提现记录";
    public bool IsSubscribePage => CurrentPage == "订阅";
    public bool IsSettingsPage => CurrentPage == "设置";

    [RelayCommand]
    private void OpenSubscribe()
    {
        _onOpenSubscribe?.Invoke();
    }

    [RelayCommand]
    private void GoExchange() => SetCurrentPage("兑换讯币");

    [RelayCommand]
    private void GoWithdraw() => SetCurrentPage("提现");

    [RelayCommand]
    private void GoWithdrawRecords() => SetCurrentPage("提现记录");

    [RelayCommand]
    private void GoSubscribe() => SetCurrentPage("订阅");

    [RelayCommand]
    private void GoSettings() => SetCurrentPage("设置");

    [RelayCommand]
    private void GoSubscribeNow()
    {
        SetCurrentPage("Subscribe");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task AutoExchange()
    {
        ExchangeSettingsHint = "";

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(MerchantPath)) missing.Add("商家路径");
        if (string.IsNullOrWhiteSpace(HelperPath)) missing.Add("辅助路径");
        if (string.IsNullOrWhiteSpace(WithdrawName)) missing.Add("姓名");
        if (string.IsNullOrWhiteSpace(AlipayAccount)) missing.Add("支付宝");
        if (!IsAgentPayMode && !IsRegionalAgentMode) missing.Add("打款方式");
        if (!IsFeeFromPoints) missing.Add("手续费扣除（从积分扣5%）");

        if (missing.Count > 0)
        {
            ExchangeSettingsHint = $"请先在“设置”里填写：{string.Join("、", missing)}";
            return;
        }

        var amount = decimal.TryParse(ExchangeAmount, out var a) ? a : 0m;
        var points = int.TryParse(ExchangePoints, out var p) ? p : 0;
        var exchangedCoins = points / 1000m;
        var remainingPoints = Math.Max(0, points - (int)(exchangedCoins * 1000m));
        var now = DateTime.Now;
        ExchangeRecords.Insert(0, new ExchangeRecordItem
        {
            Account = UserInfo,
            Points = points,
            RemainingPoints = remainingPoints,
            ExchangedCoins = exchangedCoins,
            Status = "成功"
        });
        RecalculateExchangeSummary();
        LogLines.Add($"[{now:HH:mm:ss}] 自动兑换已触发，金额: {amount:0.##}，积分: {points}");
        if (AutoWithdrawAfterExchange)
        {
            LogLines.Add($"[{now:HH:mm:ss}] 勾选了兑换后自动提现，将在兑换结束后执行提现。");
        }
        LogText = string.Join(Environment.NewLine, LogLines);

        // 启动两个软件（使用设置中的路径）
        await RunAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private void StartWithdraw()
    {
        WithdrawStatusMessage = "已触发提现流程（自动化步骤待补充）。";
    }

    [RelayCommand]
    private void ConfirmSubscribe()
    {
        SubscribeStatusMessage = SubscribePlan == "Yearly"
            ? "已选择按年订阅，待接入支付二维码逻辑。"
            : "已选择按月订阅，待接入支付二维码逻辑。";
        _onOpenSubscribe?.Invoke();
    }

    [RelayCommand]
    private void BrowseMerchantPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择商家程序路径",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            MerchantPath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseHelperPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择辅助程序路径",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            HelperPath = dialog.FileName;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(LocalSettingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var payload = new LocalUiSettings
            {
                MerchantPath = MerchantPath,
                HelperPath = HelperPath,
                WithdrawName = WithdrawName,
                AlipayAccount = AlipayAccount,
                PaymentMode = IsRegionalAgentMode ? "RegionalAgent" : "AgentPay",
                FeeMode = IsFeeFromPoints ? "FromPoints5Percent" : "None"
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LocalSettingsPath, json);
            SettingsSaveStatus = $"保存成功！";
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadSettings()
    {
        LoadLocalSettings();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        IsRunning = true;
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = new Progress<string>(s =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {s}");
            LogText = string.Join(Environment.NewLine, LogLines);
        });
        try
        {
            var workflow = new XunjieAutomationWorkflow(_automationService, HelperPath, MerchantPath);
            await workflow.RunAsync(progress, _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            RunCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.Logout();
        _onLogout?.Invoke();
    }

    private bool CanRun() => !IsRunning;
    private bool CanStop() => IsRunning;

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsExchangePage));
        OnPropertyChanged(nameof(IsWithdrawPage));
        OnPropertyChanged(nameof(IsWithdrawRecordsPage));
        OnPropertyChanged(nameof(IsSubscribePage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    private void SetCurrentPage(string page)
    {
        CurrentPage = page;
    }

    private void SeedMockRecords()
    {
        ExchangeRecords.Add(new ExchangeRecordItem { Account = "abc", Points = 100000, RemainingPoints = 123, ExchangedCoins = 100m, Status = "成功" });
        ExchangeRecords.Add(new ExchangeRecordItem { Account = "abc2", Points = 200000, RemainingPoints = 1324, ExchangedCoins = 200m, Status = "失败" });
        RecalculateExchangeSummary();

        WithdrawRecords.Add(new WithdrawRecordItem { Time = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss"), Amount = 150m, Status = "已到账" });
        WithdrawRecords.Add(new WithdrawRecordItem { Time = DateTime.Now.AddHours(-3).ToString("yyyy-MM-dd HH:mm:ss"), Amount = 80m, Status = "处理中" });
    }

    partial void OnIsAgentPayModeChanged(bool value)
    {
        if (value) IsRegionalAgentMode = false;
    }

    partial void OnIsRegionalAgentModeChanged(bool value)
    {
        if (value) IsAgentPayMode = false;
        if (!IsAgentPayMode && !IsRegionalAgentMode)
            IsAgentPayMode = true;
    }

    private void LoadLocalSettings()
    {
        try
        {
            if (!File.Exists(LocalSettingsPath))
            {
                SettingsSaveStatus = "未找到本地设置文件，使用默认参数。";
                return;
            }

            var json = File.ReadAllText(LocalSettingsPath);
            var data = JsonSerializer.Deserialize<LocalUiSettings>(json);
            if (data == null)
            {
                SettingsSaveStatus = "读取设置失败：文件内容为空。";
                return;
            }

            MerchantPath = data.MerchantPath ?? MerchantPath;
            HelperPath = data.HelperPath ?? HelperPath;
            WithdrawName = data.WithdrawName ?? "";
            AlipayAccount = data.AlipayAccount ?? "";
            IsRegionalAgentMode = string.Equals(data.PaymentMode, "RegionalAgent", StringComparison.OrdinalIgnoreCase);
            IsAgentPayMode = !IsRegionalAgentMode;
            IsFeeFromPoints = !string.Equals(data.FeeMode, "None", StringComparison.OrdinalIgnoreCase);
            SettingsSaveStatus = $"已加成功载配置！";
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"读取设置失败：{ex.Message}";
        }
    }

    private void RecalculateExchangeSummary()
    {
        TotalExchangedCoins = ExchangeRecords.Where(x => string.Equals(x.Status, "成功", StringComparison.Ordinal)).Sum(x => x.ExchangedCoins);
        EquivalentAmount = TotalExchangedCoins;
    }

    partial void OnExchangeSettingsHintChanged(string value)
    {
        IsExchangeSettingsHintVisible = !string.IsNullOrWhiteSpace(value);
    }
}

public sealed class ExchangeRecordItem
{
    public string Account { get; set; } = "";
    public int Points { get; set; }
    public int RemainingPoints { get; set; }
    public decimal ExchangedCoins { get; set; }
    public string Status { get; set; } = "";
}

public sealed class WithdrawRecordItem
{
    public string Time { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
}

public sealed class LocalUiSettings
{
    public string? MerchantPath { get; set; }
    public string? HelperPath { get; set; }
    public string? WithdrawName { get; set; }
    public string? AlipayAccount { get; set; }
    public string? PaymentMode { get; set; }
    public string? FeeMode { get; set; }
}
