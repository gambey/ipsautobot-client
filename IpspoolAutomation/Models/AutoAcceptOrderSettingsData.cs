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

    /// <summary>从末页回到目标签约页时执行；重复次数由程序计算。若为 null 则点击名称含「上一页」的按钮。</summary>
    public CaptureTargetItem? PreviousPageStep { get; set; }

    /// <summary>订单市场表格行右键菜单中「签约」项的 UIA Name（须与界面一致）。</summary>
    public string OrderMarketSignMenuItemText { get; set; } = "签约该订单(订单市场)";

    /// <summary>
    /// 可选：签约成功后追加步骤（如「签约成功！」对话框点确定）。示例一步：
    /// <c>TargetType=dialog</c>，<c>TargetText</c>=标题子串（如迅捷云商家版），<c>AnchorType=button</c>，<c>AnchorText=确定</c>，<c>Action=click</c>。
    /// <c>TargetText</c>/<c>InputValue</c> 可用占位符 <c>{pageIndex}</c>、<c>{itemIndex}</c>。
    /// </summary>
    public List<CaptureTargetItem> SignOrderSteps { get; set; } = new();
}
