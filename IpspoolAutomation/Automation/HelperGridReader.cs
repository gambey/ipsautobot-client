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

    internal static bool TryMapColumnIndices(AutomationElement grid, out int userCol, out int scoreCol, out int selectCol, out int coinCol, out int acceptedOrderCol)
    {
        userCol = -1;
        scoreCol = -1;
        selectCol = -1;
        coinCol = -1;
        acceptedOrderCol = -1;
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
                        if (n.Contains("讯币", StringComparison.Ordinal))
                            coinCol = i;
                        if (n.Contains("已接订单", StringComparison.Ordinal))
                            acceptedOrderCol = i;
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
                    if (t.Contains("讯币", StringComparison.Ordinal))
                        coinCol = i;
                    if (t.Contains("已接订单", StringComparison.Ordinal))
                        acceptedOrderCol = i;
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

        if (!TryMapColumnIndices(grid, out var userCol, out var scoreCol, out var selectCol, out _, out _))
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

    /// <summary>与 <see cref="CollectCandidates"/> 相同，但保留「可提收益」≥ <paramref name="minScoreInclusive"/> 的行。</summary>
    internal static List<WithdrawCandidateRow> CollectCandidatesWithMinScoreInclusive(
        AutomationElement helperRoot,
        IProgress<string>? progress,
        int minScoreInclusive)
    {
        var result = new List<WithdrawCandidateRow>();
        var grid = FindMainGrid(helperRoot);
        if (grid == null)
        {
            progress?.Report("未在辅助窗口中找到表格/列表控件。");
            return result;
        }

        if (!TryMapColumnIndices(grid, out var userCol, out var scoreCol, out var selectCol, out _, out _))
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
            if (score < minScoreInclusive)
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

    /// <summary>
    /// 「仅提现不兑换」：可提收益 &gt; <paramref name="minScoreExclusive"/>，且「讯币」列 ≥ 提现额度（自动=该行计算讯币，固定=<paramref name="fixedWithdrawQuotaCoins"/>）。
    /// </summary>
    internal static List<WithdrawCandidateRow> CollectCandidatesForWithdrawOnly(
        AutomationElement helperRoot,
        IProgress<string>? progress,
        int minScoreExclusive,
        bool useAutoWithdrawQuota,
        int fixedWithdrawQuotaCoins,
        Func<int, int> computeCoinsForScore)
    {
        var result = new List<WithdrawCandidateRow>();
        var grid = FindMainGrid(helperRoot);
        if (grid == null)
        {
            progress?.Report("未在辅助窗口中找到表格/列表控件。");
            return result;
        }

        if (!TryMapColumnIndices(grid, out var userCol, out var scoreCol, out var selectCol, out var coinCol, out _))
        {
            progress?.Report("警告：未能通过列头映射「用户名」「可提收益」，将按行内单元格顺序猜测列索引。");
            userCol = 0;
            scoreCol = -1;
            selectCol = -1;
            coinCol = -1;
        }

        var skipCoinFilter = false;
        if (coinCol < 0)
        {
            progress?.Report("警告：未映射到「讯币」列，将仅按可提收益筛选，无法校验讯币≥提现额度。");
            skipCoinFilter = true;
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

            if (!skipCoinFilter)
            {
                var coinText = coinCol >= 0 && coinCol < cells.Count ? cells[coinCol] : "";
                if (!TryParseScore(coinText, out var helperCoins))
                    continue;

                int minCoinInclusive;
                if (useAutoWithdrawQuota)
                {
                    var computed = computeCoinsForScore(score);
                    if (computed <= 0)
                        continue;
                    minCoinInclusive = computed;
                }
                else
                    minCoinInclusive = fixedWithdrawQuotaCoins;

                if (helperCoins < minCoinInclusive)
                    continue;
            }

            var rowId = 0;
            if (selectCol >= 0 && selectCol < cells.Count && TryParseScore(cells[selectCol].Trim(), out var rid))
                rowId = rid;

            result.Add(new WithdrawCandidateRow(user, score, rowId));
        }

        return result;
    }

    /// <summary>签到：筛选「讯币」列数值 ≥ <paramref name="minCoinInclusive"/> 的账号；可提收益仍写入 <see cref="WithdrawCandidateRow.Score"/>（若列缺失则为 0）。</summary>
    internal static List<WithdrawCandidateRow> CollectCandidatesForDailyCheck(
        AutomationElement helperRoot,
        IProgress<string>? progress,
        int minCoinInclusive = 100)
    {
        var result = new List<WithdrawCandidateRow>();
        var grid = FindMainGrid(helperRoot);
        if (grid == null)
        {
            progress?.Report("未在辅助窗口中找到表格/列表控件。");
            return result;
        }

        _ = TryMapColumnIndices(grid, out var userCol, out var scoreCol, out var selectCol, out var coinCol, out _);
        if (coinCol < 0)
        {
            progress?.Report($"无法进行签到筛选：未识别「讯币」列（要求讯币≥{minCoinInclusive}）。");
            return result;
        }

        if (userCol < 0)
        {
            progress?.Report("警告：未能映射「用户名」，将按第 0 列作为用户名。");
            userCol = 0;
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

            if (string.IsNullOrWhiteSpace(user))
                continue;

            var coinText = coinCol >= 0 && coinCol < cells.Count ? cells[coinCol] : "";
            if (!TryParseScore(coinText, out var coins) || coins < minCoinInclusive)
                continue;

            var score = 0;
            if (scoreCol >= 0 && scoreCol < cells.Count)
                _ = TryParseScore(cells[scoreCol], out score);

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
        if (!TryMapColumnIndices(grid, out var userCol, out _, out _, out _, out _))
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

    /// <summary>自动接单：仅筛选「已接订单」列解析值 &lt; 2 的账号；可提收益仅作附带读取（不参与过滤）。</summary>
    internal static List<AutoAcceptHelperCandidate> CollectCandidatesForAutoAccept(
        AutomationElement helperRoot,
        IProgress<string>? progress)
    {
        var result = new List<AutoAcceptHelperCandidate>();
        var grid = FindMainGrid(helperRoot);
        if (grid == null)
        {
            progress?.Report("未在辅助窗口中找到表格/列表控件。");
            return result;
        }

        if (!TryMapColumnIndices(grid, out var userCol, out var scoreCol, out var selectCol, out _, out var acceptedOrderCol))
        {
            progress?.Report("警告：未能通过列头映射「用户名」「可提收益」，将按行内单元格顺序猜测列索引。");
            userCol = 0;
            scoreCol = -1;
            selectCol = -1;
            acceptedOrderCol = -1;
        }

        if (acceptedOrderCol < 0)
            progress?.Report("警告：未映射到「已接订单」列，无法筛选已接订单&lt;2 的账号。");

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

            var score = 0;
            _ = TryParseScore(scoreText, out score);
            if (string.IsNullOrWhiteSpace(user))
                continue;
           
            if (acceptedOrderCol >= 0 && acceptedOrderCol < cells.Count)
            {
                var aoText = cells[acceptedOrderCol].Trim().Replace(",", "").Replace("，", "");
                int LEAST_ORDER_COUNT = 2; //最小接单数量
                if (!int.TryParse(aoText, out var ao) || ao >= LEAST_ORDER_COUNT)
                    continue;
            }
            else
                continue;

            var rowId = 0;
            if (selectCol >= 0 && selectCol < cells.Count && TryParseScore(cells[selectCol].Trim(), out var rid))
                rowId = rid;

            result.Add(new AutoAcceptHelperCandidate(user, score, rowId));
        }

        return result;
    }
}
