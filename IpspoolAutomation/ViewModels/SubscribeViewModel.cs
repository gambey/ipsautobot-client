using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpspoolAutomation.Models.Auth;
using IpspoolAutomation.Services;

namespace IpspoolAutomation.ViewModels;

public sealed partial class SubscribeViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;

    [ObservableProperty] private string _qrCodeUrl = "";
    [ObservableProperty] private string _orderId = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showQrCode;

    public SubscribeViewModel(IApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [RelayCommand(CanExecute = nameof(CanCreateOrder))]
    private async Task CreateOrderAsync(CancellationToken cancellationToken)
    {
        StatusMessage = "";
        ShowQrCode = false;
        IsLoading = true;
        CreateOrderCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _apiClient.CreateSubscribeOrderAsync(
                new SubscribeOrderRequest("monthly", 9.9m),
                cancellationToken).ConfigureAwait(true);
            if (result.Success && !string.IsNullOrEmpty(result.QrCodeUrl))
            {
                OrderId = result.OrderId ?? "";
                QrCodeUrl = result.QrCodeUrl;
                ShowQrCode = true;
                StatusMessage = "请使用支付宝扫描上方二维码完成支付";
            }
            else
            {
                StatusMessage = result.Message ?? "创建订单失败";
            }
        }
        finally
        {
            IsLoading = false;
            CreateOrderCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCreateOrder() => !IsLoading;
}
