namespace IpspoolAutomation.Automation;

/// <summary>自动接单：辅助列表中「已接订单」&lt; 2 的候选行。</summary>
public sealed record AutoAcceptHelperCandidate(string Username, int Score, int RowId);
