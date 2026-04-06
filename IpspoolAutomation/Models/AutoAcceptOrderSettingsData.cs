using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Models;

/// <summary>自动接单配置（<c>autoAcceptOrder.json</c>）。</summary>
public sealed class AutoAcceptOrderSettingsData
{
    /// <summary>检查迅捷助手列表的间隔（分钟），默认 10。</summary>
    public int PollIntervalMinutes { get; set; } = 10;

    /// <summary>订单市场行退款率超过该值则跳过（0–100，与列解析单位一致）。</summary>
    public decimal MaxRefundRatePercent { get; set; } = 100m;

    /// <summary>旧版单列表字段；若存在且 <see cref="NavigationSteps"/> 为空，加载时迁移到 NavigationSteps。</summary>
    public List<CaptureTargetItem>? Steps { get; set; }

    /// <summary>附着商家后进入订单市场首页前的步骤（订单管理、订单市场等）。</summary>
    public List<CaptureTargetItem> NavigationSteps { get; set; } = new();

    /// <summary>订单市场翻页；每页采集后执行一次，共 9 页。若为 null 则尝试点击名称含「下一页」的按钮。</summary>
    public CaptureTargetItem? NextPageStep { get; set; }

    /// <summary>选中目标页码后的步骤：下拉、确定、右键签约等；<c>InputValue</c>/<c>TargetText</c> 可用占位符 <c>{pageIndex}</c>、<c>{itemIndex}</c>。</summary>
    public List<CaptureTargetItem> SignOrderSteps { get; set; } = new();
}
