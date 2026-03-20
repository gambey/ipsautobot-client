namespace IpspoolAutomation.Automation;

/// <summary>辅助软件表格中一行：选择列序号、用户名与可提收益（积分）。</summary>
public sealed record WithdrawCandidateRow(string Username, int Score, int RowId);
