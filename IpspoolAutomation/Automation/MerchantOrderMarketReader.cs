using System.Windows.Automation;

namespace IpspoolAutomation.Automation;

/// <summary>商家端「订单市场」表格解析（列名关键字匹配）。</summary>
internal static class MerchantOrderMarketReader
{
    internal static List<OrderMarketEntry> ReadFilteredPage(
        AutomationElement merchantRoot,
        int pageIndex,
        decimal targetRefundRatePercent,
        IProgress<string>? progress)
    {
        var list = new List<OrderMarketEntry>();
        var grid = HelperGridReader.FindMainGrid(merchantRoot);
        if (grid == null)
        {
            progress?.Report("商家窗口中未找到订单表格。");
            return list;
        }

        if (!TryMapOrderMarketColumns(grid, out var idCol, out var bidCol, out var durCol, out var refundCol))
        {
            progress?.Report("警告：未能映射订单市场列（订单号/出价/时长/退款率），尝试按索引 0,1,2,3。");
            idCol = 0;
            bidCol = 1;
            durCol = 2;
            refundCol = 3;
        }

        AutomationElementCollection? dataItems = null;
        try
        {
            dataItems = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        }
        catch
        {
            return list;
        }

        if (dataItems == null || dataItems.Count == 0)
            return list;

        var item = 0;
        foreach (AutomationElement row in dataItems)
        {
            item++;
            var cells = HelperGridReader.CollectRowCellTexts(row);
            if (cells.Count == 0)
                continue;

            string id = GetCell(cells, idCol, "");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!TryParseDecimalFlexible(GetCell(cells, bidCol, ""), out var bid) || bid <= 0)
                continue;
            if (!TryParseDecimalFlexible(GetCell(cells, durCol, ""), out var hours) || hours <= 0)
                continue;
            if (!TryParseDecimalFlexible(GetCell(cells, refundCol, ""), out var refundPct))
                refundPct = 0;

            if (refundPct > targetRefundRatePercent)
                continue;

            var unit = bid / hours;
            list.Add(new OrderMarketEntry(id.Trim(), unit, refundPct, pageIndex, item));
        }

        return list;
    }

    private static string GetCell(IReadOnlyList<string> cells, int col, string fallback)
    {
        if (col >= 0 && col < cells.Count)
            return cells[col].Trim();
        return fallback;
    }

    internal static bool TryMapOrderMarketColumns(AutomationElement grid, out int orderIdCol, out int bidCol, out int durationCol, out int refundCol)
    {
        orderIdCol = -1;
        bidCol = -1;
        durationCol = -1;
        refundCol = -1;
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
                        if (n.Contains("订单", StringComparison.Ordinal) && (n.Contains("号", StringComparison.Ordinal) || n.Contains("编号", StringComparison.Ordinal)))
                            orderIdCol = i;
                        if (n.Contains("出价", StringComparison.Ordinal) || n.Contains("客单价", StringComparison.Ordinal) ||
                            (n.Contains("客户", StringComparison.Ordinal) && n.Contains("积分", StringComparison.Ordinal)))
                            bidCol = i;
                        if (n.Contains("时长", StringComparison.Ordinal) || n.Contains("租赁", StringComparison.Ordinal))
                            durationCol = i;
                        if (n.Contains("退款", StringComparison.Ordinal))
                            refundCol = i;
                    }
                    catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }

        return orderIdCol >= 0 && bidCol >= 0 && durationCol >= 0 && refundCol >= 0;
    }

    /// <summary>与采集时一致：当前页内第 <paramref name="oneBasedIndex"/> 个 <see cref="ControlType.DataItem"/>（从 1 起）。</summary>
    internal static AutomationElement? GetDataItemByOneBasedIndex(AutomationElement merchantRoot, int oneBasedIndex)
    {
        var grid = HelperGridReader.FindMainGrid(merchantRoot);
        if (grid == null || oneBasedIndex < 1)
            return null;
        AutomationElementCollection? dataItems = null;
        try
        {
            dataItems = grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        }
        catch
        {
            return null;
        }

        if (dataItems == null || oneBasedIndex > dataItems.Count)
            return null;
        return dataItems[oneBasedIndex - 1];
    }

    internal static bool TryReadOrderIdForRow(AutomationElement grid, AutomationElement row, out string orderId)
    {
        orderId = "";
        var idCol = 0;
        if (!TryMapOrderMarketColumns(grid, out var mappedId, out _, out _, out _))
            idCol = 0;
        else
            idCol = mappedId;

        var cells = HelperGridReader.CollectRowCellTexts(row);
        if (idCol < 0 || idCol >= cells.Count)
            return false;
        orderId = cells[idCol].Trim();
        return !string.IsNullOrWhiteSpace(orderId);
    }

    internal static AutomationElement? FindDataItemByOrderId(AutomationElement merchantRoot, string expectedOrderId)
    {
        var grid = HelperGridReader.FindMainGrid(merchantRoot);
        if (grid == null)
            return null;
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
        var want = (expectedOrderId ?? "").Trim();
        if (want.Length == 0)
            return null;
        foreach (AutomationElement row in dataItems)
        {
            if (!TryReadOrderIdForRow(grid, row, out var id))
                continue;
            if (string.Equals(id.Trim(), want, StringComparison.Ordinal))
                return row;
        }

        return null;
    }

    private static bool TryParseDecimalFlexible(string s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        s = s.Trim().Replace(",", "").Replace("，", "").Replace("%", "").Replace("％", "");
        return decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out value);
    }
}
