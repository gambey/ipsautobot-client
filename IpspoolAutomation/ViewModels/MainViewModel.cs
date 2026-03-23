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
using System.Windows.Threading;

namespace IpspoolAutomation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAppConfig _config;
    private readonly IAutomationService _automationService;
    private readonly ICaptureTargetSettingsService _captureTargetSettingsService;
    private readonly IDailyCheckExeService _dailyCheckExeService;
    private readonly IDailyCheckSettingsService _dailyCheckSettingsService;
    private readonly IWithdrawRecordsService _withdrawRecordsService;
    private readonly IWithdrawDailyService _withdrawDailyService;
    private CancellationTokenSource? _cts;
    private Action? _onLogout;
    private readonly DispatcherTimer _checkinScheduler = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTime? _nextScheduledCheckinAt;
    private DateOnly? _lastAutoCheckinDate;

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
    [ObservableProperty] private string _checkinMode = "Cancel";
    [ObservableProperty] private bool _isImmediateCheckinMode;
    [ObservableProperty] private bool _isScheduledCheckinMode;
    [ObservableProperty] private bool _isCancelCheckinMode = true;
    [ObservableProperty] private string _checkinStartTime = "07:00";
    [ObservableProperty] private string _checkinStatusMessage = "";
    [ObservableProperty] private string _checkinLogText = "";
    [ObservableProperty] private string _nextCheckinAtText = "--";
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
    public ObservableCollection<CaptureTargetItem> DailyCheckTargetList { get; } = new();
    public ObservableCollection<string> CheckinLogLines { get; } = new();
    public IReadOnlyList<string> CheckinTimeOptions { get; } =
        Enumerable.Range(7, 16).Select(h => $"{h:00}:00").ToList();
    private static readonly string LocalSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation",
        "ui-settings.json");

    public MainViewModel(
        IAuthService authService,
        IAppConfig config,
        IAutomationService automationService,
        ICaptureTargetSettingsService captureTargetSettingsService,
        IDailyCheckExeService dailyCheckExeService,
        IDailyCheckSettingsService dailyCheckSettingsService,
        IWithdrawRecordsService withdrawRecordsService,
        IWithdrawDailyService withdrawDailyService)
    {
        _authService = authService;
        _config = config;
        _automationService = automationService;
        _captureTargetSettingsService = captureTargetSettingsService;
        _dailyCheckExeService = dailyCheckExeService;
        _dailyCheckSettingsService = dailyCheckSettingsService;
        _withdrawRecordsService = withdrawRecordsService;
        _withdrawDailyService = withdrawDailyService;
        UserInfo = _authService.UserName ?? "用户";
        MerchantPath = _config.XunjieMerchantPath;
        HelperPath = _config.XunjieHelperPath;
        LoadLocalSettings();
        LoadCaptureTargetSettings();
        LoadDailyCheckExeSettings();
        LoadDailyCheckSettings();
        LoadWithdrawRecords();
        LoadWithdrawDaily();
        if (string.IsNullOrWhiteSpace(CheckinMode))
            CheckinMode = "Cancel";
        _checkinScheduler.Tick += OnCheckinSchedulerTick;
        _checkinScheduler.Start();
        RefreshCheckinSchedule();
    }

    public void SetOnLogout(Action callback) => _onLogout = callback;

    public bool IsWithdrawDetailPage => CurrentPage == "提现详情";
    public bool IsWithdrawPage => CurrentPage == "提现";
    public bool IsWithdrawRecordsPage => CurrentPage == "提现记录";
    public bool IsCheckinPage => CurrentPage == "签到";
    public bool IsSettingsPage => CurrentPage == "设置";
    public bool IsExecuteEnabled => IsImmediateCheckinMode;
    public bool IsScheduleEnabled => IsScheduledCheckinMode;

    [RelayCommand]
    private void GoWithdraw() => SetCurrentPage("提现");

    [RelayCommand]
    private void GoWithdrawDetail() => SetCurrentPage("提现详情");

    [RelayCommand]
    private void GoWithdrawRecords() => SetCurrentPage("提现记录");

    [RelayCommand]
    private void GoCheckin() => SetCurrentPage("签到");

    [RelayCommand]
    private void GoSettings() => SetCurrentPage("设置");

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

        CheckinStatusMessage = "正在执行签到流程…";
        await RunDailyCheckInternalAsync(isManual: true).ConfigureAwait(true);
    }

    private async Task RunDailyCheckInternalAsync(bool isManual)
    {
        IsRunning = true;
        StartWithdrawCommand.NotifyCanExecuteChanged();
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
                    _lastAutoCheckinDate = DateOnly.FromDateTime(DateTime.Now);
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
            StopCommand.NotifyCanExecuteChanged();
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
    private void SaveDailyCheckSettings()
    {
        try
        {
            _dailyCheckSettingsService.Save(new DailyCheckSettingsData
            {
                Mode = CheckinMode,
                StartTime = CheckinStartTime
            });
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

    private bool CanRun() => !IsRunning;
    private bool CanStop() => IsRunning;

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsWithdrawDetailPage));
        OnPropertyChanged(nameof(IsWithdrawPage));
        OnPropertyChanged(nameof(IsWithdrawRecordsPage));
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
            RefreshCheckinSchedule();
        }
        catch (Exception ex)
        {
            CheckinStatusMessage = $"读取签到配置失败：{ex.Message}";
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
