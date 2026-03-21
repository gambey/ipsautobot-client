using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Automation;
using IpspoolAutomation.Models;
using IpspoolAutomation.Models.Capture;
using IpspoolAutomation.Services;
using IpspoolAutomation.Views;
using Microsoft.Win32;

namespace IpspoolAutomation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAppConfig _config;
    private readonly IAutomationService _automationService;
    private readonly ICaptureTargetSettingsService _captureTargetSettingsService;
    private readonly IWithdrawRecordsService _withdrawRecordsService;
    private readonly IWithdrawDailyService _withdrawDailyService;
    private CancellationTokenSource? _cts;
    private Action? _onLogout;
    private Action? _onOpenSubscribe;

    [ObservableProperty] private string _userInfo = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _currentPage = "提现";

    [ObservableProperty] private string _subscriptionStatusText = "未订阅服务";
    [ObservableProperty] private bool _isSubscribed;
    [ObservableProperty] private string _expireAtText = "--";

    /// <summary>提现详情页：成功行的已兑讯币合计。</summary>
    [ObservableProperty] private decimal _totalExchangedCoins;

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
    [ObservableProperty] private string _withdrawRecordsTotalDisplay = "合计：0";

    /// <summary>提现详情列表是否为空（用于显示「当前无提现记录」）。</summary>
    [ObservableProperty] private bool _isWithdrawDetailEmpty = true;

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<WithdrawDetailItem> WithdrawDetailItems { get; } = new();
    public ObservableCollection<WithdrawRecordItem> WithdrawRecords { get; } = new();
    public ObservableCollection<CaptureTargetItem> CaptureTargetList { get; } = new();
    private static readonly string LocalSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "ui-settings.json");

    public MainViewModel(
        IAuthService authService,
        IAppConfig config,
        IAutomationService automationService,
        ICaptureTargetSettingsService captureTargetSettingsService,
        IWithdrawRecordsService withdrawRecordsService,
        IWithdrawDailyService withdrawDailyService)
    {
        _authService = authService;
        _config = config;
        _automationService = automationService;
        _captureTargetSettingsService = captureTargetSettingsService;
        _withdrawRecordsService = withdrawRecordsService;
        _withdrawDailyService = withdrawDailyService;
        UserInfo = _authService.UserName ?? "用户";
        MerchantPath = _config.XunjieMerchantPath;
        HelperPath = _config.XunjieHelperPath;
        LoadLocalSettings();
        LoadCaptureTargetSettings();
        LoadWithdrawRecords();
        LoadWithdrawDaily();
    }

    public void SetOnLogout(Action callback) => _onLogout = callback;
    public void SetOnOpenSubscribe(Action callback) => _onOpenSubscribe = callback;

    public bool IsWithdrawDetailPage => CurrentPage == "提现详情";
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
    private void GoWithdraw() => SetCurrentPage("提现");

    [RelayCommand]
    private void GoWithdrawDetail() => SetCurrentPage("提现详情");

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
    private async Task StartWithdraw()
    {
        if (string.IsNullOrWhiteSpace(HelperPath) || string.IsNullOrWhiteSpace(MerchantPath))
        {
            WithdrawStatusMessage = "请先在「设置」中配置辅助路径与商家路径。";
            return;
        }
        if (string.IsNullOrWhiteSpace(AlipayAccount))
        {
            WithdrawStatusMessage = "请先在「设置」中填写收款人手机号。";
            return;
        }

        WithdrawStatusMessage = "正在执行自动提现…";
        IsRunning = true;
        StartWithdrawCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {s}");
            LogText = string.Join(Environment.NewLine, LogLines);
        });
        try
        {
            var workflow = new XunjieAutomationWorkflow(_automationService, HelperPath, MerchantPath);
            var runResult = await workflow.RunWithdrawAsync(
                progress,
                AlipayAccount,
                WithdrawName,
                AlipayAccount,
                CaptureTargetList.ToList(),
                _cts.Token).ConfigureAwait(true);
            WithdrawStatusMessage = "自动提现流程已结束。";
            if (runResult != null)
            {
                ApplyWithdrawDaily(runResult);
                AppendWithdrawRecord(runResult);
            }
        }
        catch (OperationCanceledException)
        {
            WithdrawStatusMessage = "已取消自动提现。";
        }
        catch (Exception ex)
        {
            WithdrawStatusMessage = $"自动提现异常：{ex.Message}";
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] 异常：{ex}");
            LogText = string.Join(Environment.NewLine, LogLines);
        }
        finally
        {
            IsRunning = false;
            StartWithdrawCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
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

    [RelayCommand]
    private void OpenCaptureSettings()
    {
        var vm = new CaptureSettingsViewModel(_captureTargetSettingsService, CaptureTargetList);
        var window = new CaptureSettingsWindow
        {
            DataContext = vm
        };
        vm.SetCloseAction(() => window.Close());
        window.ShowDialog();
        LoadCaptureTargetSettings();
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
        OnPropertyChanged(nameof(IsWithdrawDetailPage));
        OnPropertyChanged(nameof(IsWithdrawPage));
        OnPropertyChanged(nameof(IsWithdrawRecordsPage));
        OnPropertyChanged(nameof(IsSubscribePage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    private void SetCurrentPage(string page)
    {
        CurrentPage = page;
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

    private void LoadCaptureTargetSettings()
    {
        try
        {
            var settings = _captureTargetSettingsService.Load();
            CaptureTargetList.Clear();
            foreach (var item in settings.CaptureTargetList.OrderBy(x => x.TargetID))
                CaptureTargetList.Add(item);
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"读取捕捉设置失败：{ex.Message}";
        }
    }

    private void LoadWithdrawRecords()
    {
        try
        {
            var data = _withdrawRecordsService.Load();
            WithdrawRecords.Clear();
            foreach (var item in data.Records.OrderByDescending(r => r.RecordedAt).Take(WithdrawRecordsService.MaxRecords))
                WithdrawRecords.Add(item);
            AssignWithdrawRecordRowIndices();
            RefreshWithdrawRecordsTotal();
        }
        catch
        {
            RefreshWithdrawRecordsTotal();
        }
    }

    private void LoadWithdrawDaily()
    {
        try
        {
            var data = _withdrawDailyService.Load();
            WithdrawDetailItems.Clear();
            foreach (var x in data.Items)
                WithdrawDetailItems.Add(x);
            AssignWithdrawDetailRowIndices();
            RecalculateWithdrawDetailTotal();
        }
        catch
        {
            RecalculateWithdrawDetailTotal();
        }
        finally
        {
            RefreshWithdrawDetailEmptyState();
        }
    }

    private void ApplyWithdrawDaily(WithdrawRunResult result)
    {
        WithdrawDetailItems.Clear();
        foreach (var d in result.Details)
            WithdrawDetailItems.Add(d);
        try
        {
            _withdrawDailyService.Save(new WithdrawDailyData { Items = result.Details.ToList() });
        }
        catch { /* ignore */ }
        RecalculateWithdrawDetailTotal();
        RefreshWithdrawDetailEmptyState();
    }

    private void RefreshWithdrawDetailEmptyState()
    {
        IsWithdrawDetailEmpty = WithdrawDetailItems.Count == 0;
    }

    private void AssignWithdrawRecordRowIndices()
    {
        for (var i = 0; i < WithdrawRecords.Count; i++)
            WithdrawRecords[i].RowIndex = i + 1;
    }

    private void AssignWithdrawDetailRowIndices()
    {
        for (var i = 0; i < WithdrawDetailItems.Count; i++)
            WithdrawDetailItems[i].RowIndex = i + 1;
    }

    private void AppendWithdrawRecord(WithdrawRunResult result)
    {
        WithdrawRecords.Insert(0, new WithdrawRecordItem
        {
            RecordedAt = DateTime.Now,
            ProcessedAccountCount = result.ProcessedCount,
            Amount = result.TotalCoins
        });
        while (WithdrawRecords.Count > WithdrawRecordsService.MaxRecords)
            WithdrawRecords.RemoveAt(WithdrawRecords.Count - 1);
        AssignWithdrawRecordRowIndices();
        try
        {
            _withdrawRecordsService.Save(new WithdrawRecordsData { Records = WithdrawRecords.ToList() });
        }
        catch { /* ignore */ }
        RefreshWithdrawRecordsTotal();
    }

    private void RefreshWithdrawRecordsTotal()
    {
        var sum = WithdrawRecords.Sum(x => x.Amount);
        WithdrawRecordsTotalDisplay = $"合计：{sum:F0}";
    }

    private void RecalculateWithdrawDetailTotal()
    {
        TotalExchangedCoins = WithdrawDetailItems
            .Where(x => string.Equals(x.Status, "成功", StringComparison.Ordinal))
            .Sum(x => x.ExchangedCoins);
    }
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
