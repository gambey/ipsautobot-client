using System.Windows.Automation;

namespace IpspoolAutomation.Automation;

internal static class HelperGridReader
{
    internal const int MinWithdrawableScore = 105000;

    internal static AutomationElement? FindMainGrid(AutomationElement root)
    {
        return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Table))
            ?? root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid))
            ?? root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
    }

    internal static bool TryMapColumnIndices(AutomationElement grid, out int userCol, out int scoreCol, out int selectCol)
    {
        userCol = -1;
        scoreCol = -1;
        selectCol = -1;
        try
        {
            var headers = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.HeaderItem));
            if (headers != null && headers.Count > 0)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    try
                    {
                        var n = headers[i].Current.Name ?? "";
                        if (n.Contains("用户名", StringComparison.Ordinal))
                            userCol = i;
                        if (n.Contains("可提收益", StringComparison.Ordinal))
                            scoreCol = i;
                        if (n.Contains("选择", StringComparison.Ordinal))
                            selectCol = i;
                    }
                    catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }

        // 备用：从首行 Text 子节点推断列名（部分 WinForms 表格）
        try
        {
            var firstRow = grid.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
            if (firstRow != null)
            {
                var texts = CollectRowCellTexts(firstRow);
                for (var i = 0; i < texts.Count; i++)
                {
                    var t = texts[i];
                    if (t.Contains("用户名", StringComparison.Ordinal))
                        userCol = i;
                    if (t.Contains("可提收益", StringComparison.Ordinal))
                        scoreCol = i;
                    if (t.Contains("选择", StringComparison.Ordinal))
                        selectCol = i;
                }
            }
        }
        catch { /* ignore */ }

        return userCol >= 0 && scoreCol >= 0;
    }

    internal static List<string> CollectRowCellTexts(AutomationElement row)
    {
        var list = new List<(double Left, string Text)>();
        CollectLeafValues(row, list);
        list.Sort((a, b) => a.Left.CompareTo(b.Left));
        return list.Select(x => x.Text).ToList();
    }

    private static void CollectLeafValues(AutomationElement node, List<(double Left, string Text)> acc)
    {
        AutomationElementCollection? children = null;
        try
        {
            children = node.FindAll(TreeScope.Children, Condition.TrueCondition);
        }
        catch
        {
            return;
        }
        if (children == null || children.Count == 0)
        {
            TryAddLeaf(node, acc);
            return;
        }
        foreach (AutomationElement ch in children)
            CollectLeafValues(ch, acc);
    }

    private static void TryAddLeaf(AutomationElement el, List<(double Left, string Text)> acc)
    {
        try
        {
            var ct = el.Current.ControlType;
            double left = 0;
            try
            {
                left = el.Current.BoundingRectangle.Left;
            }
            catch
            {
                left = 0;
            }
            if (ct == ControlType.Edit)
            {
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vo) && vo is ValuePattern vp)
                {
                    acc.Add((left, vp.Current.Value ?? ""));
                    return;
                }
            }
            if (ct == ControlType.Text)
            {
                var name = el.Current.Name ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                    acc.Add((left, name));
            }
        }
        catch { /* ignore */ }
    }

    internal static bool TryParseScore(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        s = s.Trim().Replace(",", "").Replace("，", "");
        return int.TryParse(s, out value);
    }

    internal static List<WithdrawCandidateRow> CollectCandidates(
        AutomationElement helperRoot,
        IProgress<string>? progress,
        int minScoreExclusive = MinWithdrawableScore)
    {
        var result = new List<WithdrawCandidateRow>();
        var grid = FindMainGrid(helperRoot);
        if (grid == null)
        {
            progress?.Report("未在辅助窗口中找到表格/列表控件。");
            return result;
        }

        if (!TryMapColumnIndices(grid, out var userCol, out var scoreCol, out var selectCol))
        {
            progress?.Report("警告：未能通过列头映射「用户名」「可提收益」，将按行内单元格顺序猜测列索引。");
            userCol = 0;
            scoreCol = -1;
            selectCol = -1;
        }
        if (selectCol < 0)
            progress?.Report("提示：未映射到「选择」列，rowID 将保持为 0。");

        AutomationElementCollection? dataItems = null;
        try
        {
            dataItems = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        }
        catch
        {
            progress?.Report("读取表格行失败。");
            return result;
        }
        if (dataItems == null || dataItems.Count == 0)
        {
            progress?.Report("表格中未找到数据行。");
            return result;
        }

        for (var i = 0; i < dataItems.Count; i++)
        {
            AutomationElement row;
            try
            {
                row = dataItems[i];
            }
            catch
            {
                continue;
            }
            var cells = CollectRowCellTexts(row);
            if (cells.Count == 0)
                continue;

            string user;
            if (userCol >= 0 && userCol < cells.Count)
                user = cells[userCol].Trim();
            else
                user = cells[0].Trim();

            string scoreText;
            if (scoreCol >= 0 && scoreCol < cells.Count)
                scoreText = cells[scoreCol];
            else
            {
                scoreText = "";
                for (var j = cells.Count - 1; j >= 0; j--)
                {
                    if (TryParseScore(cells[j], out _))
                    {
                        scoreText = cells[j];
                        break;
                    }
                }
            }

            if (!TryParseScore(scoreText, out var score))
                continue;
            if (score <= minScoreExclusive)
                continue;
            if (string.IsNullOrWhiteSpace(user))
                continue;

            var rowId = 0;
            if (selectCol >= 0 && selectCol < cells.Count && TryParseScore(cells[selectCol].Trim(), out var rid))
                rowId = rid;

            result.Add(new WithdrawCandidateRow(user, score, rowId));
        }

        return result;
    }

    internal static AutomationElement? FindDataItemRowByUsername(AutomationElement helperRoot, string username)
    {
        var grid = FindMainGrid(helperRoot);
        if (grid == null)
            return null;
        if (!TryMapColumnIndices(grid, out var userCol, out _, out _))
            userCol = 0;

        AutomationElementCollection? dataItems = null;
        try
        {
            dataItems = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        }
        catch
        {
            return null;
        }
        if (dataItems == null)
            return null;

        foreach (AutomationElement row in dataItems)
        {
            var cells = CollectRowCellTexts(row);
            if (cells.Count == 0)
                continue;
            var u = userCol >= 0 && userCol < cells.Count ? cells[userCol].Trim() : cells[0].Trim();
            if (string.Equals(u, username.Trim(), StringComparison.Ordinal))
                return row;
        }
        return null;
    }
}
