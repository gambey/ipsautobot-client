using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace IpspoolAutomation.Automation;

/// <summary>
/// 从对话框 UIA 树中收集可见文本，解析简单二元整数算式（如 5 + 31）并求值。
/// </summary>
public static class AutomationMathExpression
{
    private static readonly Regex ArithmeticRegex = new(
        @"(-?\d+)\s*([+\-*/])\s*(-?\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// 在对话框子树中查找形如 a op b 的表达式并计算；支持 + - * /（整除）。
    /// </summary>
    public static bool TryEvaluateFromDialogSubtree(AutomationElement dialogRoot, out int result)
    {
        return TryEvaluateFromDialogSubtree(dialogRoot, out result, out _);
    }

    public static bool TryEvaluateFromDialogSubtree(AutomationElement dialogRoot, out int result, out string diagnostics)
    {
        result = 0;
        diagnostics = "算式诊断：未采集到可解析文本。";
        if (dialogRoot == null)
        {
            diagnostics = "算式诊断：dialogRoot为空。";
            return false;
        }

        try
        {
            AutomationElementCollection? all = null;
            try
            {
                all = dialogRoot.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            }
            catch
            {
                diagnostics = "算式诊断：无法枚举dialog子树。";
                return false;
            }

            var sampled = new List<string>();
            var tokenTexts = new List<string>();
            foreach (AutomationElement el in all)
            {
                try
                {
                    var name = el.Current.Name ?? "";
                    if (!string.IsNullOrWhiteSpace(name) && sampled.Count < 8)
                        sampled.Add(name.Trim());
                    if (!string.IsNullOrWhiteSpace(name))
                        tokenTexts.Add(name);
                    if (TryEvaluateFromText(name, out result))
                    {
                        diagnostics = $"算式诊断：从Name命中，表达式结果={result}。";
                        return true;
                    }
                }
                catch
                {
                    /* ignore */
                }
            }

            foreach (AutomationElement el in all)
            {
                try
                {
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj) && tpObj is TextPattern tp)
                    {
                        string? doc;
                        try
                        {
                            doc = tp.DocumentRange.GetText(-1);
                        }
                        catch
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(doc) && TryEvaluateFromText(doc, out result))
                        {
                            diagnostics = $"算式诊断：从TextPattern命中，表达式结果={result}。";
                            return true;
                        }
                        if (!string.IsNullOrWhiteSpace(doc))
                        {
                            if (sampled.Count < 8)
                                sampled.Add(doc.Trim().Replace("\r", " ").Replace("\n", " "));
                            tokenTexts.Add(doc);
                        }
                    }
                }
                catch
                {
                    /* ignore */
                }
            }

            if (TryEvaluateFromTokenTexts(tokenTexts, out result, out var tokenExpr))
            {
                diagnostics = $"算式诊断：从分词重组命中（{tokenExpr}），结果={result}。";
                return true;
            }

            diagnostics = sampled.Count == 0
                ? "算式诊断：未读到Name/TextPattern文本。"
                : $"算式诊断：已采样文本={string.Join(" | ", sampled)}";
        }
        catch
        {
            diagnostics = "算式诊断：解析过程中发生异常。";
            return false;
        }

        return false;
    }

    private static bool TryEvaluateFromText(string raw, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var normalized = NormalizeMathSymbols(raw);
        foreach (Match m in ArithmeticRegex.Matches(normalized))
        {
            if (!m.Success)
                continue;
            if (!int.TryParse(m.Groups[1].Value, out var a))
                continue;
            if (!int.TryParse(m.Groups[3].Value, out var b))
                continue;
            var op = m.Groups[2].Value;
            if (op.Length != 1)
                continue;
            result = op[0] switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b != 0 ? a / b : 0,
                _ => 0
            };
            return true;
        }

        return false;
    }

    private static bool TryEvaluateFromTokenTexts(IReadOnlyList<string> texts, out int result, out string expression)
    {
        result = 0;
        expression = "";
        if (texts == null || texts.Count == 0)
            return false;

        var tokens = new List<string>();
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t))
                continue;
            var normalized = NormalizeMathSymbols(t);
            foreach (Match m in MathTokenRegex.Matches(normalized))
            {
                if (!m.Success)
                    continue;
                tokens.Add(m.Value);
            }
        }

        if (tokens.Count < 3)
            return false;

        // 扫描 token 序列，允许界面把算式拆成多个控件：6 | + | 28 | =
        for (var i = 0; i <= tokens.Count - 3; i++)
        {
            if (!int.TryParse(tokens[i], out var a))
                continue;
            var op = tokens[i + 1];
            if (op.Length != 1 || "+-*/".IndexOf(op[0]) < 0)
                continue;
            if (!int.TryParse(tokens[i + 2], out var b))
                continue;

            result = op[0] switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b != 0 ? a / b : 0,
                _ => 0
            };
            expression = $"{a}{op}{b}";
            return true;
        }

        return false;
    }

    private static string NormalizeMathSymbols(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '＋': sb.Append('+'); break;
                case '－': sb.Append('-'); break;
                case '×':
                case '＊':
                    sb.Append('*');
                    break;
                case '÷':
                case '／':
                    sb.Append('/');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static readonly Regex MathTokenRegex = new(
        @"-?\d+|[+\-*/=]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
