using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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
using System.Windows.Threading;

namespace IpspoolAutomation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly INetworkBindingGuard _networkBindingGuard;
    private readonly IAppConfig _config;
    private readonly IAutomationService _automationService;
    private readonly ICaptureTargetSettingsService _captureTargetSettingsService;
    private readonly IWithdrawOnlySettingsService _withdrawOnlySettingsService;
    private readonly IExchangeScoreSettingsService _exchangeScoreSettingsService;
    private readonly IDailyCheckExeService _dailyCheckExeService;
    private readonly IDailyCheckSettingsService _dailyCheckSettingsService;
    private readonly IWithdrawRecordsService _withdrawRecordsService;
    private readonly IWithdrawDailyService _withdrawDailyService;
    private CancellationTokenSource? _cts;
    private Action? _onLogout;
    private bool _suppressUiSettingsPersist;
    private readonly DispatcherTimer _checkinScheduler = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTime? _nextScheduledCheckinAt;
    private DateOnly? _lastAutoCheckinDate;

    [ObservableProperty] private string _userInfo = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _currentPage = "提现";
    [ObservableProperty] private string _windowTitle = "智灵科技";

    [ObservableProperty] private string _subscriptionStatusText = "未订阅服务";
    [ObservableProperty] private bool _isSubscribed;
    [ObservableProperty] private bool _isVipUser;
    [ObservableProperty] private string _expireAtText = "--";

    /// <summary>提现详情页：成功行的已兑讯币合计。</summary>
    [ObservableProperty] private decimal _totalExchangedCoins;

    [ObservableProperty] private string _withdrawStatusMessage = "提现流程待接入自动化步骤。";
    [ObservableProperty] private string _exchangeStatusMessage = "";
    [ObservableProperty] private string _exchangeLogText = "";
    [ObservableProperty] private string _checkinMode = "Cancel";
    [ObservableProperty] private bool _isImmediateCheckinMode;
    [ObservableProperty] private bool _isScheduledCheckinMode;
    [ObservableProperty] private bool _isCancelCheckinMode = true;
    [ObservableProperty] private string _checkinStartTime = "07:00";
    [ObservableProperty] private string _checkinStatusMessage = "";
    [ObservableProperty] private string _checkinLogText = "";
    [ObservableProperty] private string _nextCheckinAtText = "--";
    [ObservableProperty] private string _todayAutoCheckinStatusText = "今日自动签到：未执行";
    [ObservableProperty] private bool _isTodayAutoCheckinDone;
    [ObservableProperty] private string _merchantPath = "";
    [ObservableProperty] private string _helperPath = "";
    [ObservableProperty] private string _withdrawName = "";
    [ObservableProperty] private string _alipayAccount = "";
    [ObservableProperty] private bool _keepReserve235000;

    /// <summary>提现额度下拉：<c>Auto</c>、<c>100</c>、<c>200</c>、<c>300</c>、<c>500</c>、<c>1000</c>。</summary>
    [ObservableProperty] private string _withdrawCoinPreset = "Auto";

    /// <summary>兑换页「兑换额度」下拉，与提现额度独立。</summary>
    [ObservableProperty] private string _exchangeCoinPreset = "Auto";
    [ObservableProperty] private bool _isAgentPayMode = true;
    [ObservableProperty] private bool _isRegionalAgentMode;
    [ObservableProperty] private bool _isFeeFromPoints = true;
    [ObservableProperty] private string _settingsSaveStatus = "";
    [ObservableProperty] private string _withdrawRecordsTotalDisplay = "合计：0";

    /// <summary>提现详情列表是否为空（用于显示「当前无提现记录」）。</summary>
    [ObservableProperty] private bool _isWithdrawDetailEmpty = true;

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<string> ExchangeLogLines { get; } = new();
    public ObservableCollection<WithdrawDetailItem> WithdrawDetailItems { get; } = new();
    public ObservableCollection<WithdrawRecordItem> WithdrawRecords { get; } = new();
    public ObservableCollection<CaptureTargetItem> CaptureTargetList { get; } = new();
    public ObservableCollection<CaptureTargetItem> WithdrawOnlyTargetList { get; } = new();
    public ObservableCollection<CaptureTargetItem> ExchangeScoreTargetList { get; } = new();
    public ObservableCollection<CaptureTargetItem> DailyCheckTargetList { get; } = new();
    public ObservableCollection<string> CheckinLogLines { get; } = new();
    public IReadOnlyList<string> CheckinTimeOptions { get; } =
        Enumerable.Range(7, 16).Select(h => $"{h:00}:00").ToList();
    private static readonly string LocalSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "ui-settings.json");

    /// <summary>运行时用程序集元数据判断是否为 Debug 构建，避免 #if / DefineConstants 与设计时不一致导致按钮永远不显示。</summary>
    private static readonly bool IsDebugBuildComputed = ComputeIsDebugBuild();

    private static bool ComputeIsDebugBuild()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var cfg = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration?.Trim();
        if (!string.IsNullOrEmpty(cfg))
        {
            if (string.Equals(cfg, "Debug", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(cfg, "Release", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 无配置名时：Debug 构建通常带 JIT 跟踪；Release 优化版一般为 false
        var dbg = assembly.GetCustomAttribute<DebuggableAttribute>();
        return dbg is { IsJITTrackingEnabled: true };
    }

    public MainViewModel(
        IAuthService authService,
        INetworkBindingGuard networkBindingGuard,
        IAppConfig config,
        IAutomationService automationService,
        ICaptureTargetSettingsService captureTargetSettingsService,
        IWithdrawOnlySettingsService withdrawOnlySettingsService,
        IExchangeScoreSettingsService exchangeScoreSettingsService,
        IDailyCheckExeService dailyCheckExeService,
        IDailyCheckSettingsService dailyCheckSettingsService,
        IWithdrawRecordsService withdrawRecordsService,
        IWithdrawDailyService withdrawDailyService)
    {
        _authService = authService;
        _networkBindingGuard = networkBindingGuard;
        _config = config;
        _automationService = automationService;
        _captureTargetSettingsService = captureTargetSettingsService;
        _withdrawOnlySettingsService = withdrawOnlySettingsService;
        _exchangeScoreSettingsService = exchangeScoreSettingsService;
        _dailyCheckExeService = dailyCheckExeService;
        _dailyCheckSettingsService = dailyCheckSettingsService;
        _withdrawRecordsService = withdrawRecordsService;
        _withdrawDailyService = withdrawDailyService;
        WindowTitle = string.IsNullOrWhiteSpace(_config.AppVersion)
            ? "智灵自动"
            : $"智灵自动 {_config.AppVersion}";
        UserInfo = _authService.UserName ?? "用户";
        ApplySubscriptionState(_authService.MemberExpireAt, _authService.MemberType);
        MerchantPath = _config.XunjieMerchantPath;
        HelperPath = _config.XunjieHelperPath;
        LoadLocalSettings();
        LoadCaptureTargetSettings();
        LoadWithdrawOnlySettings();
        LoadExchangeScoreSettings();
        LoadDailyCheckExeSettings();
        LoadDailyCheckSettings();
        LoadWithdrawRecords();
        LoadWithdrawDaily();
        if (string.IsNullOrWhiteSpace(CheckinMode))
            CheckinMode = "Cancel";
        _checkinScheduler.Tick += OnCheckinSchedulerTick;
        _checkinScheduler.Start();
        RefreshCheckinSchedule();
        RefreshProfileOnStartup();
        // 确保设置页「打开路径」等依赖 IsDebugBuild 的绑定在 DataContext 就绪后刷新一次
        OnPropertyChanged(nameof(IsDebugBuild));
    }

    public void SetOnLogout(Action callback) => _onLogout = callback;

    public bool IsWithdrawDetailPage => CurrentPage == "提现详情";
    public bool IsWithdrawPage => CurrentPage == "提现";
    public bool IsExchangePage => CurrentPage == "兑换";
    public bool IsWithdrawRecordsPage => CurrentPage == "提现记录";
    public bool IsCheckinPage => CurrentPage == "签到";
    public bool IsSettingsPage => CurrentPage == "设置";

    public bool IsDebugBuild => IsDebugBuildComputed;

    public bool IsExecuteEnabled => IsImmediateCheckinMode && !IsRunning && CanUseAutomationFeatures();
    public bool IsScheduleEnabled => IsScheduledCheckinMode;

    [RelayCommand]
    private void GoWithdraw() => SetCurrentPage("提现");

    [RelayCommand]
    private void GoExchange() => SetCurrentPage("兑换");

    [RelayCommand]
    private void GoWithdrawDetail() => SetCurrentPage("提现详情");

    [RelayCommand]
    private void GoWithdrawRecords() => SetCurrentPage("提现记录");

    [RelayCommand]
    private void GoCheckin() => SetCurrentPage("签到");

    [RelayCommand]
    private void GoSettings() => SetCurrentPage("设置");

    [RelayCommand]
    private void ClearWithdrawLog()
    {
        LogLines.Clear();
        LogText = "";
    }

    [RelayCommand]
    private void ClearExchangeLog()
    {
        ExchangeLogLines.Clear();
        ExchangeLogText = "";
    }

    [RelayCommand]
    private async Task ExecuteCheckin()
    {
        if (IsCancelCheckinMode)
        {
            CheckinStatusMessage = "已选择取消签到，不执行任何签到动作。";
            return;
        }
        if (!IsImmediateCheckinMode)
        {
            CheckinStatusMessage = "仅在立刻签到模式下可执行。";
            return;
        }

        if (IsRunning)
            return;
        if (string.IsNullOrWhiteSpace(HelperPath) || string.IsNullOrWhiteSpace(MerchantPath))
        {
            CheckinStatusMessage = "请先在「设置」中配置辅助路径与商家路径。";
            return;
        }
        if (DailyCheckTargetList.Count == 0)
        {
            CheckinStatusMessage = "请先配置签到设置步骤。";
            return;
        }
        if (!await EnsureLatestVipEligibleAsync(
                message =>
                {
                    CheckinStatusMessage = message;
                    CheckinLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    CheckinLogText = string.Join(Environment.NewLine, CheckinLogLines);
                }).ConfigureAwait(true))
        {
            return;
        }
        if (!await EnsureAutomationPlatformAllowedAsync(
                message =>
                {
                    CheckinStatusMessage = message;
                    CheckinLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    CheckinLogText = string.Join(Environment.NewLine, CheckinLogLines);
                },
                CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        CheckinStatusMessage = "正在执行签到流程…";
        await RunDailyCheckInternalAsync(isManual: true).ConfigureAwait(true);
    }

    private async Task RunDailyCheckInternalAsync(bool isManual)
    {
        IsRunning = true;
        StartWithdrawCommand.NotifyCanExecuteChanged();
        StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
        StartExchangeScoreCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        if (isManual)
        {
            CheckinLogLines.Clear();
            CheckinLogText = "";
        }
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s =>
        {
            var line = FormatCheckinLogLine(s);
            if (string.IsNullOrWhiteSpace(line))
                return;
            CheckinLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            CheckinLogText = string.Join(Environment.NewLine, CheckinLogLines);
        });
        try
        {
            var workflow = new XunjieAutomationWorkflow(_automationService, HelperPath, MerchantPath);
            var success = await workflow.RunDailyCheckAsync(progress, DailyCheckTargetList.ToList(), _cts.Token).ConfigureAwait(true);
            CheckinStatusMessage = $"签到流程结束，成功处理账号：{success}。";
            if (IsScheduledCheckinMode)
            {
                _nextScheduledCheckinAt = BuildNextScheduledRun(DateTime.Now, CheckinStartTime);
                NextCheckinAtText = _nextScheduledCheckinAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
                CheckinStatusMessage += $" 下次计划时间：{_nextScheduledCheckinAt:HH:mm:ss}";
                if (!isManual)
                {
                    _lastAutoCheckinDate = DateOnly.FromDateTime(DateTime.Now);
                    PersistLastAutoCheckinDate();
                    RefreshTodayAutoCheckinStatus();
                }
            }
        }
        catch (OperationCanceledException)
        {
            CheckinStatusMessage = "已取消签到流程。";
        }
        catch (Exception ex)
        {
            CheckinStatusMessage = $"签到流程异常：{ex.Message}";
            CheckinLogLines.Add($"[{DateTime.Now:HH:mm:ss}] 异常：{ex.Message}");
            CheckinLogText = string.Join(Environment.NewLine, CheckinLogLines);
        }
        finally
        {
            IsRunning = false;
            StartWithdrawCommand.NotifyCanExecuteChanged();
            StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
            StartExchangeScoreCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            if (isManual)
                RefreshTodayAutoCheckinStatus();
        }
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
        if (!await EnsureLatestVipEligibleAsync(
                message =>
                {
                    WithdrawStatusMessage = message;
                    LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    LogText = string.Join(Environment.NewLine, LogLines);
                }).ConfigureAwait(true))
        {
            return;
        }
        if (!await EnsureAutomationPlatformAllowedAsync(
                message =>
                {
                    WithdrawStatusMessage = message;
                    LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    LogText = string.Join(Environment.NewLine, LogLines);
                }).ConfigureAwait(true))
        {
            return;
        }

        WithdrawStatusMessage = "正在执行自动提现…";
        IsRunning = true;
        StartWithdrawCommand.NotifyCanExecuteChanged();
        StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
        StartExchangeScoreCommand.NotifyCanExecuteChanged();
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
            var withdrawMinScoreExclusive = KeepReserve235000
                ? 235000 + HelperGridReader.MinWithdrawableScore
                : HelperGridReader.MinWithdrawableScore;
            var fixedCoins = TryGetFixedWithdrawCoinsFromPreset(out _);
            var runResult = await workflow.RunWithdrawAsync(
                progress,
                AlipayAccount,
                WithdrawName,
                AlipayAccount,
                withdrawMinScoreExclusive,
                CaptureTargetList.ToList(),
                fixedCoins,
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
            StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
            StartExchangeScoreCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task StartWithdrawOnly()
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
        if (!await EnsureLatestVipEligibleAsync(
                message =>
                {
                    WithdrawStatusMessage = message;
                    LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    LogText = string.Join(Environment.NewLine, LogLines);
                }).ConfigureAwait(true))
        {
            return;
        }
        if (!await EnsureAutomationPlatformAllowedAsync(
                message =>
                {
                    WithdrawStatusMessage = message;
                    LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    LogText = string.Join(Environment.NewLine, LogLines);
                }).ConfigureAwait(true))
        {
            return;
        }

        var wo = _withdrawOnlySettingsService.Load();
        if (wo.CaptureTargetList == null || wo.CaptureTargetList.Count == 0)
        {
            WithdrawStatusMessage = "请先在「设置」中配置「仅兑换不提现」（withdraw_only.json）。";
            return;
        }

        WithdrawStatusMessage = "正在执行仅提现不兑换…";
        IsRunning = true;
        StartWithdrawCommand.NotifyCanExecuteChanged();
        StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
        StartExchangeScoreCommand.NotifyCanExecuteChanged();
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
            var withdrawMinScoreExclusive = KeepReserve235000
                ? 235000 + HelperGridReader.MinWithdrawableScore
                : HelperGridReader.MinWithdrawableScore;
            var useAuto = string.Equals(WithdrawCoinPreset, "Auto", StringComparison.OrdinalIgnoreCase);
            var fixedCoinsOpt = TryGetFixedWithdrawCoinsFromPreset(out _);
            var runResult = await workflow.RunWithdrawOnlyAsync(
                progress,
                AlipayAccount,
                WithdrawName,
                AlipayAccount,
                withdrawMinScoreExclusive,
                useAuto,
                fixedCoinsOpt ?? 0,
                wo.CaptureTargetList,
                fixedCoinsOpt,
                _cts.Token).ConfigureAwait(true);
            WithdrawStatusMessage = "仅提现不兑换流程已结束。";
            if (runResult != null)
            {
                ApplyWithdrawDaily(runResult);
                AppendWithdrawRecord(runResult);
            }
        }
        catch (OperationCanceledException)
        {
            WithdrawStatusMessage = "已取消仅提现不兑换。";
        }
        catch (Exception ex)
        {
            WithdrawStatusMessage = $"仅提现不兑换异常：{ex.Message}";
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] 异常：{ex}");
            LogText = string.Join(Environment.NewLine, LogLines);
        }
        finally
        {
            IsRunning = false;
            StartWithdrawCommand.NotifyCanExecuteChanged();
            StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
            StartExchangeScoreCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task StartExchangeScore()
    {
        if (string.IsNullOrWhiteSpace(HelperPath) || string.IsNullOrWhiteSpace(MerchantPath))
        {
            ExchangeStatusMessage = "请先在「设置」中配置辅助路径与商家路径。";
            return;
        }
        if (string.IsNullOrWhiteSpace(AlipayAccount))
        {
            ExchangeStatusMessage = "请先在「设置」中填写收款人手机号。";
            return;
        }
        if (!await EnsureLatestVipEligibleAsync(
                message =>
                {
                    ExchangeStatusMessage = message;
                    ExchangeLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    ExchangeLogText = string.Join(Environment.NewLine, ExchangeLogLines);
                }).ConfigureAwait(true))
        {
            return;
        }
        if (!await EnsureAutomationPlatformAllowedAsync(
                message =>
                {
                    ExchangeStatusMessage = message;
                    ExchangeLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                    ExchangeLogText = string.Join(Environment.NewLine, ExchangeLogLines);
                }).ConfigureAwait(true))
        {
            return;
        }

        var settings = _exchangeScoreSettingsService.Load();
        if (settings.CaptureTargetList == null || settings.CaptureTargetList.Count == 0)
        {
            ExchangeStatusMessage = "请先在「设置」中配置「仅兑换设置」（exchange_score.json）。";
            return;
        }

        var useAutoExchange = string.Equals(ExchangeCoinPreset, "Auto", StringComparison.OrdinalIgnoreCase);
        int? fixedExchangeCoins = null;
        if (!useAutoExchange)
        {
            if (!int.TryParse(ExchangeCoinPreset, out var eq) || eq <= 0 || !IsValidWithdrawCoinPreset(ExchangeCoinPreset))
            {
                ExchangeStatusMessage = "兑换额度无效，请选择 100～1000 或自动计算。";
                return;
            }
            fixedExchangeCoins = eq;
        }

        ExchangeStatusMessage = "正在执行仅兑换流程…";
        ExchangeLogLines.Clear();
        ExchangeLogText = "";
        IsRunning = true;
        StartWithdrawCommand.NotifyCanExecuteChanged();
        StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
        StartExchangeScoreCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s =>
        {
            ExchangeLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {s}");
            ExchangeLogText = string.Join(Environment.NewLine, ExchangeLogLines);
        });
        try
        {
            var workflow = new XunjieAutomationWorkflow(_automationService, HelperPath, MerchantPath);
            var minScoreExclusive = KeepReserve235000
                ? 235000 + HelperGridReader.MinWithdrawableScore
                : HelperGridReader.MinWithdrawableScore;
            await workflow.RunExchangeScoreAsync(
                progress,
                AlipayAccount,
                WithdrawName,
                AlipayAccount,
                minScoreExclusive,
                settings.CaptureTargetList,
                useAutoExchange,
                fixedExchangeCoins,
                KeepReserve235000,
                _cts.Token).ConfigureAwait(true);
            ExchangeStatusMessage = "仅兑换流程已结束。";
        }
        catch (OperationCanceledException)
        {
            ExchangeStatusMessage = "已取消仅兑换流程。";
        }
        catch (Exception ex)
        {
            ExchangeStatusMessage = $"仅兑换异常：{ex.Message}";
            ExchangeLogLines.Add($"[{DateTime.Now:HH:mm:ss}] 异常：{ex}");
            ExchangeLogText = string.Join(Environment.NewLine, ExchangeLogLines);
        }
        finally
        {
            IsRunning = false;
            StartWithdrawCommand.NotifyCanExecuteChanged();
            StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
            StartExchangeScoreCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void SaveDailyCheckSettings()
    {
        try
        {
            var persisted = _dailyCheckSettingsService.Load();
            persisted.Mode = CheckinMode;
            persisted.StartTime = CheckinStartTime;
            persisted.LastAutoCheckinDate = _lastAutoCheckinDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _dailyCheckSettingsService.Save(persisted);
            RefreshCheckinSchedule();

            if (IsScheduledCheckinMode)
            {
                var scheduled = _nextScheduledCheckinAt ?? BuildNextScheduledRun(DateTime.Now, CheckinStartTime);
                CheckinStatusMessage = $"签到设置已保存：定时签到，基础时间 {CheckinStartTime}，本次计划时间 {scheduled:HH:mm:ss}。";
            }
            else if (IsCancelCheckinMode)
            {
                CheckinStatusMessage = "签到设置已保存：取消签到。";
            }
            else
            {
                CheckinStatusMessage = "签到设置已保存：立刻签到。";
            }
        }
        catch (Exception ex)
        {
            CheckinStatusMessage = $"保存签到设置失败：{ex.Message}";
        }
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
            WriteLocalUiSettingsFile();
            SettingsSaveStatus = $"保存成功！";
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"保存失败：{ex.Message}";
        }
    }

    private LocalUiSettings BuildLocalUiSettingsPayload() => new()
    {
        MerchantPath = MerchantPath,
        HelperPath = HelperPath,
        WithdrawName = WithdrawName,
        AlipayAccount = AlipayAccount,
        PaymentMode = IsRegionalAgentMode ? "RegionalAgent" : "AgentPay",
        FeeMode = IsFeeFromPoints ? "FromPoints5Percent" : "None",
        WithdrawCoinPreset = WithdrawCoinPreset,
        ExchangeCoinPreset = ExchangeCoinPreset
    };

    private void WriteLocalUiSettingsFile()
    {
        var dir = Path.GetDirectoryName(LocalSettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var payload = BuildLocalUiSettingsPayload();
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(LocalSettingsPath, json);
    }

    private void PersistCoinPresetsToUiSettingsQuietly()
    {
        try
        {
            WriteLocalUiSettingsFile();
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"保存额度偏好失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenUserSettingsFolder()
    {
        try
        {
            var dir = ClientSettingsPaths.LegacyAppDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"无法打开设置目录：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdateClientSettingsFromServer()
    {
        SettingsSaveStatus = "设置更新中…";
        var result = await _authService.RefreshClientSettingsFromServerAsync().ConfigureAwait(true);
        if (result.Success)
        {
            SettingsSaveStatus = "设置更新成功！";
            LoadCaptureTargetSettings();
            LoadWithdrawOnlySettings();
            LoadExchangeScoreSettings();
            LoadDailyCheckExeSettings();
        }
        else
        {
            SettingsSaveStatus = result.Message ?? "更新失败。";
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

    [RelayCommand]
    private void OpenWithdrawOnlySettings()
    {
        var vm = new CaptureSettingsViewModel(_withdrawOnlySettingsService, WithdrawOnlyTargetList);
        var window = new CaptureSettingsWindow("仅提现设置 - 自动化设置")
        {
            DataContext = vm
        };
        vm.SetCloseAction(() => window.Close());
        window.ShowDialog();
        LoadWithdrawOnlySettings();
    }

    [RelayCommand]
    private void OpenExchangeScoreSettings()
    {
        var vm = new CaptureSettingsViewModel(_exchangeScoreSettingsService, ExchangeScoreTargetList);
        var window = new CaptureSettingsWindow("仅兑换设置 - 自动化设置")
        {
            DataContext = vm
        };
        vm.SetCloseAction(() => window.Close());
        window.ShowDialog();
        LoadExchangeScoreSettings();
    }

    [RelayCommand]
    private void OpenDailyCheckSettings()
    {
        var vm = new DailyCheckSettingsViewModel(_dailyCheckExeService, DailyCheckTargetList);
        var window = new DailyCheckSettingsWindow
        {
            DataContext = vm
        };
        vm.SetCloseAction(() => window.Close());
        window.ShowDialog();
        LoadDailyCheckExeSettings();
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

    [RelayCommand]
    private async Task RefreshUserProfile()
    {
        var refreshed = await _authService.RefreshCurrentUserProfileAsync().ConfigureAwait(true);
        if (!refreshed)
            return;
        UserInfo = _authService.UserName ?? UserInfo;
        ApplySubscriptionState(_authService.MemberExpireAt, _authService.MemberType);
        NotifyAutomationEligibilityChanged();
    }

    private bool CanRun() => !IsRunning && CanUseAutomationFeatures();
    private bool CanStop() => IsRunning;

    private bool CanUseAutomationFeatures()
    {
        // Centralized extensible rule list for feature-level enablement.
        var rules = new List<bool>
        {
            !string.IsNullOrWhiteSpace(_authService.BoundMac),
            IsVipUser,
            IsSubscribed
        };
        return rules.All(x => x);
    }

    private void NotifyAutomationEligibilityChanged()
    {
        OnPropertyChanged(nameof(IsExecuteEnabled));
        StartWithdrawCommand.NotifyCanExecuteChanged();
        StartWithdrawOnlyCommand.NotifyCanExecuteChanged();
        StartExchangeScoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>非 <see cref="WithdrawCoinPreset"/> Auto 时返回固定讯币档位；否则 <c>null</c>（自动计算）。</summary>
    private int? TryGetFixedWithdrawCoinsFromPreset(out bool isAuto)
    {
        isAuto = string.Equals(WithdrawCoinPreset, "Auto", StringComparison.OrdinalIgnoreCase);
        if (isAuto)
            return null;
        if (int.TryParse(WithdrawCoinPreset, out var v) && v > 0)
            return v;
        return null;
    }

    private async Task<bool> EnsureAutomationPlatformAllowedAsync(Action<string> report, CancellationToken cancellationToken = default)
    {
        var check = await _networkBindingGuard.CheckCurrentMachineAsync(cancellationToken).ConfigureAwait(true);
        if (check.Allowed)
            return true;

        var message = check.FailureReason == NetworkBindingFailureReason.NotMatched
            ? NetworkBindingGuard.NotBoundPlatformMessage
            : (check.Message ?? "网卡校验失败，请稍后再试。");
        report(message);
        return false;
    }

    private async Task<bool> EnsureLatestVipEligibleAsync(Action<string> report, CancellationToken cancellationToken = default)
    {
        var refreshed = await _authService.RefreshCurrentUserProfileAsync(cancellationToken).ConfigureAwait(true);
        if (!refreshed)
        {
            report("获取最新用户信息失败，请稍后重试。");
            return false;
        }

        UserInfo = _authService.UserName ?? UserInfo;
        ApplySubscriptionState(_authService.MemberExpireAt, _authService.MemberType);
        if (!IsSubscribed)
        {
            report("当前非VIP，无法执行自动化操作。");
            return false;
        }

        return true;
    }

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsWithdrawDetailPage));
        OnPropertyChanged(nameof(IsWithdrawPage));
        OnPropertyChanged(nameof(IsWithdrawRecordsPage));
        OnPropertyChanged(nameof(IsExchangePage));
        OnPropertyChanged(nameof(IsCheckinPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    partial void OnCheckinModeChanged(string value)
    {
        var immediate = string.Equals(value, "Immediate", StringComparison.Ordinal);
        var scheduled = string.Equals(value, "Scheduled", StringComparison.Ordinal);
        var cancel = string.Equals(value, "Cancel", StringComparison.Ordinal);
        if (IsImmediateCheckinMode != immediate) IsImmediateCheckinMode = immediate;
        if (IsScheduledCheckinMode != scheduled) IsScheduledCheckinMode = scheduled;
        if (IsCancelCheckinMode != cancel) IsCancelCheckinMode = cancel;
        OnPropertyChanged(nameof(IsExecuteEnabled));
        OnPropertyChanged(nameof(IsScheduleEnabled));
        RefreshCheckinSchedule();
    }

    partial void OnIsRunningChanged(bool value)
    {
        NotifyAutomationEligibilityChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsImmediateCheckinModeChanged(bool value)
    {
        if (value) CheckinMode = "Immediate";
    }

    partial void OnIsScheduledCheckinModeChanged(bool value)
    {
        if (value) CheckinMode = "Scheduled";
    }

    partial void OnIsCancelCheckinModeChanged(bool value)
    {
        if (value) CheckinMode = "Cancel";
    }

    partial void OnCheckinStartTimeChanged(string value)
    {
        RefreshCheckinSchedule();
    }

    private void SetCurrentPage(string page)
    {
        CurrentPage = page;
    }

    private static bool IsValidWithdrawCoinPreset(string? p)
    {
        if (string.IsNullOrWhiteSpace(p))
            return false;
        if (string.Equals(p, "Auto", StringComparison.OrdinalIgnoreCase))
            return true;
        return p is "100" or "200" or "300" or "500" or "1000";
    }

    partial void OnWithdrawCoinPresetChanged(string value)
    {
        if (_suppressUiSettingsPersist)
            return;
        if (!IsValidWithdrawCoinPreset(value))
            return;
        PersistCoinPresetsToUiSettingsQuietly();
    }

    partial void OnExchangeCoinPresetChanged(string value)
    {
        if (_suppressUiSettingsPersist)
            return;
        if (!IsValidWithdrawCoinPreset(value))
            return;
        PersistCoinPresetsToUiSettingsQuietly();
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
        _suppressUiSettingsPersist = true;
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
            if (IsValidWithdrawCoinPreset(data.WithdrawCoinPreset))
                WithdrawCoinPreset = data.WithdrawCoinPreset!;
            if (IsValidWithdrawCoinPreset(data.ExchangeCoinPreset))
                ExchangeCoinPreset = data.ExchangeCoinPreset!;
            SettingsSaveStatus = $"已加成功载配置！";
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"读取设置失败：{ex.Message}";
        }
        finally
        {
            _suppressUiSettingsPersist = false;
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

    private void LoadWithdrawOnlySettings()
    {
        try
        {
            var settings = _withdrawOnlySettingsService.Load();
            WithdrawOnlyTargetList.Clear();
            foreach (var item in settings.CaptureTargetList.OrderBy(x => x.TargetID))
                WithdrawOnlyTargetList.Add(item);
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"读取仅兑换不提现设置失败：{ex.Message}";
        }
    }

    private void LoadExchangeScoreSettings()
    {
        try
        {
            var settings = _exchangeScoreSettingsService.Load();
            ExchangeScoreTargetList.Clear();
            foreach (var item in settings.CaptureTargetList.OrderBy(x => x.TargetID))
                ExchangeScoreTargetList.Add(item);
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = $"读取仅兑换设置失败：{ex.Message}";
        }
    }

    private void LoadDailyCheckExeSettings()
    {
        try
        {
            var settings = _dailyCheckExeService.Load();
            DailyCheckTargetList.Clear();
            foreach (var item in settings.CaptureTargetList.OrderBy(x => x.TargetID))
                DailyCheckTargetList.Add(item);
        }
        catch (Exception ex)
        {
            CheckinStatusMessage = $"读取签到流程设置失败：{ex.Message}";
        }
    }

    private void LoadDailyCheckSettings()
    {
        try
        {
            var data = _dailyCheckSettingsService.Load();
            var mode = data.Mode?.Trim() ?? "";
            if (string.Equals(mode, "Scheduled", StringComparison.OrdinalIgnoreCase))
                CheckinMode = "Scheduled";
            else if (string.Equals(mode, "Cancel", StringComparison.OrdinalIgnoreCase))
                CheckinMode = "Cancel";
            else
                CheckinMode = "Cancel";

            if (!string.IsNullOrWhiteSpace(data.StartTime) && CheckinTimeOptions.Contains(data.StartTime))
                CheckinStartTime = data.StartTime;
            else if (string.IsNullOrWhiteSpace(CheckinStartTime))
                CheckinStartTime = "07:00";

            if (!string.IsNullOrWhiteSpace(data.LastAutoCheckinDate) &&
                DateOnly.TryParse(data.LastAutoCheckinDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastAuto))
                _lastAutoCheckinDate = lastAuto;
            else
                _lastAutoCheckinDate = null;

            RefreshCheckinSchedule();
            RefreshTodayAutoCheckinStatus();
        }
        catch (Exception ex)
        {
            CheckinStatusMessage = $"读取签到配置失败：{ex.Message}";
        }
    }

    private void RefreshTodayAutoCheckinStatus()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        IsTodayAutoCheckinDone = _lastAutoCheckinDate == today;
        TodayAutoCheckinStatusText = IsTodayAutoCheckinDone ? "今日自动签到：已执行" : "今日自动签到：未执行";
    }

    private void PersistLastAutoCheckinDate()
    {
        try
        {
            var data = _dailyCheckSettingsService.Load();
            data.LastAutoCheckinDate = _lastAutoCheckinDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _dailyCheckSettingsService.Save(data);
        }
        catch
        {
            // 持久化失败不影响本次签到逻辑
        }
    }

    private void RefreshCheckinSchedule()
    {
        if (!IsScheduledCheckinMode)
        {
            _nextScheduledCheckinAt = null;
            NextCheckinAtText = "--";
            return;
        }

        _nextScheduledCheckinAt = BuildNextScheduledRun(DateTime.Now, CheckinStartTime);
        NextCheckinAtText = _nextScheduledCheckinAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
    }

    private async void OnCheckinSchedulerTick(object? sender, EventArgs e)
    {
        RefreshTodayAutoCheckinStatus();
        if (!IsScheduledCheckinMode || _nextScheduledCheckinAt == null)
            return;
        if (IsRunning)
            return;
        if (DateTime.Now < _nextScheduledCheckinAt.Value)
            return;
        if (_lastAutoCheckinDate.HasValue && _lastAutoCheckinDate.Value == DateOnly.FromDateTime(DateTime.Now))
        {
            _nextScheduledCheckinAt = BuildNextScheduledRun(DateTime.Now.AddDays(1), CheckinStartTime);
            NextCheckinAtText = _nextScheduledCheckinAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
            CheckinStatusMessage = $"今日已自动签到一次，下一次计划时间：{_nextScheduledCheckinAt:yyyy-MM-dd HH:mm:ss}";
            return;
        }
        if (string.IsNullOrWhiteSpace(HelperPath) || string.IsNullOrWhiteSpace(MerchantPath))
            return;
        if (DailyCheckTargetList.Count == 0)
            return;
        if (!await EnsureLatestVipEligibleAsync(message => CheckinStatusMessage = message).ConfigureAwait(true))
            return;

        CheckinStatusMessage = $"到达计划时间（{_nextScheduledCheckinAt:HH:mm:ss}），开始自动签到…";
        await RunDailyCheckInternalAsync(isManual: false).ConfigureAwait(true);
    }

    private static DateTime BuildNextScheduledRun(DateTime now, string startTime)
    {
        var hhmm = (startTime ?? "").Trim();
        if (!TimeSpan.TryParse(hhmm, out var ts))
            ts = TimeSpan.FromHours(7);
        var baseTime = new DateTime(now.Year, now.Month, now.Day, ts.Hours, ts.Minutes, 0, now.Kind);
        if (baseTime <= now)
            baseTime = baseTime.AddDays(1);
        var offsetSeconds = Random.Shared.Next(60, 1801);
        return baseTime.AddSeconds(offsetSeconds);
    }

    private static string? FormatCheckinLogLine(string raw)
    {
#if DEBUG
        return raw;
#else
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        const string prefix = "正在处理账号：";
        var idx = raw.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var name = raw[(idx + prefix.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return $"正在处理账号：{name}";
#endif
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

    private async void RefreshProfileOnStartup()
    {
        try
        {
            var refreshed = await _authService.RefreshCurrentUserProfileAsync().ConfigureAwait(true);
            if (!refreshed)
                return;
            UserInfo = _authService.UserName ?? UserInfo;
            ApplySubscriptionState(_authService.MemberExpireAt, _authService.MemberType);
            NotifyAutomationEligibilityChanged();
        }
        catch
        {
            // ignore refresh failures to avoid breaking startup
        }
    }

    private void ApplySubscriptionState(string? memberExpireAtRaw, int? memberType)
    {
        IsVipUser = false;
        if (string.IsNullOrWhiteSpace(memberExpireAtRaw))
        {
            ExpireAtText = "--";
            IsSubscribed = false;
            SubscriptionStatusText = "非VIP用户";
            NotifyAutomationEligibilityChanged();
            return;
        }

        if (!DateTime.TryParse(memberExpireAtRaw, out var expireAt))
        {
            ExpireAtText = memberExpireAtRaw;
            IsSubscribed = false;
            SubscriptionStatusText = "VIP状态未知";
            NotifyAutomationEligibilityChanged();
            return;
        }

        ExpireAtText = expireAt.ToString("yyyy-MM-dd HH:mm:ss");
        var isActive = expireAt > DateTime.Now;
        IsVipUser = true;
        IsSubscribed = isActive;
        SubscriptionStatusText = isActive ? "VIP" : "VIP已过期";
        NotifyAutomationEligibilityChanged();
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
    /// <summary>Auto / 100 / 200 / 300 / 500 / 1000</summary>
    public string? WithdrawCoinPreset { get; set; }

    /// <summary>兑换页额度，取值同 <see cref="WithdrawCoinPreset"/>。</summary>
    public string? ExchangeCoinPreset { get; set; }
}
