using System.Globalization;
using System.Text.RegularExpressions;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

/// <summary>
/// 公平性提醒：找出长期未被点到的成员，并把粘贴导入的文本行预处理成
/// 成员导入服务可以识别的「姓名 权重」格式。
/// </summary>
public static class FairnessService
{
    /// <summary>
    /// 判断成员是否已在历史中出现过；未出现在历史中的成员视为从未被点。
    /// </summary>
    public static bool HasAppearedInHistory(IReadOnlyList<DrawHistoryEntry> history, string name)
    {
        ArgumentNullException.ThrowIfNull(history);
        var normalized = name.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return history
            .SelectMany(entry => entry.MemberNames)
            .Any(drawn => string.Equals(drawn.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 计算成员最近一次出现在抽取历史中的天数。从未被点过的成员返回 int.MaxValue，
    /// 便于「长期未点」判定直接与阈值比较。
    /// </summary>
    public static int DaysSinceLastDraw(IReadOnlyList<DrawHistoryEntry> history, string name)
    {
        ArgumentNullException.ThrowIfNull(history);
        var normalized = name.Trim();
        if (normalized.Length == 0)
        {
            return int.MaxValue;
        }

        var lastDrawn = history
            .Where(entry => entry.MemberNames.Any(drawn =>
                string.Equals(drawn.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.DrawnAt)
            .OrderByDescending(entry => entry)
            .FirstOrDefault();
        if (lastDrawn == default)
        {
            return int.MaxValue;
        }

        var days = (DateTime.Now.Date - lastDrawn.LocalDateTime.Date).Days;
        return Math.Max(0, days);
    }

    /// <summary>
    /// 找出在 <paramref name="thresholdDays"/> 天内没有出现在抽取历史中的成员姓名。
    /// 未出现在历史中的成员（从未被点）也会被提醒。
    /// </summary>
    public static IReadOnlyList<string> FindLongUncalled(
        IReadOnlyList<Member> members,
        IReadOnlyList<DrawHistoryEntry> history,
        int thresholdDays)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(history);

        var threshold = Math.Max(1, thresholdDays);

        return members
            .Where(member => member.IsActive && !string.IsNullOrWhiteSpace(member.Name))
            .Select(member => (Member: member, LastDrawnAt: history
                .Where(entry => entry.MemberNames.Any(name =>
                    string.Equals(name.Trim(), member.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Select(entry => entry.DrawnAt)
                .OrderByDescending(entry => entry)
                .FirstOrDefault()))
            .Where(item => item.LastDrawnAt == default
                || (DateTime.Now.Date - item.LastDrawnAt.LocalDateTime.Date).Days >= threshold)
            .Select(item => item.Member.Name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly Regex NumberingPrefix = new(
        @"^\s*\(\s*\d+\s*\)|^\s*\d+\s*[\.、）\)]|^\s*\d+\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// 公平性指数（0-100）：基于被点次数分布的基尼系数，
    /// 公式为 1 - Σ|xi-xj| / (2n²μ)。完全均匀时为 100，一人独点时接近 0。
    /// </summary>
    public static double ComputeFairnessIndex(IReadOnlyCollection<int> drawCounts)
    {
        ArgumentNullException.ThrowIfNull(drawCounts);
        var counts = drawCounts
            .Where(count => count >= 0)
            .OrderBy(count => count)
            .ToList();
        if (counts.Count == 0)
        {
            return 100d;
        }

        var total = (long)counts.Sum(count => (long)count);
        if (total <= 0)
        {
            // 全部为零：无法衡量差异，视为均匀
            return 100d;
        }

        var n = counts.Count;
        var mean = total / (double)n;

        // 累加每对被点次数之差：排序后 Σ|xi-xj| = Σ(2i - n + 1)·xᵢ
        long pairDifferenceSum = 0;
        for (var index = 0; index < n; index++)
        {
            pairDifferenceSum += (long)(2 * index - n + 1) * counts[index];
        }

        // 基尼系数 = Σ|xi-xj| / (2n²μ)；完全均匀时为 0，一人独点时为 (n-1)/(2n)
        var gini = n == 1 ? 0d : pairDifferenceSum / (2d * n * n * mean);
        return Math.Clamp(100d * (1d - gini), 0d, 100d);
    }

    /// <summary>公平性指数的一句话解读。</summary>
    public static string DescribeFairnessIndex(double fairnessIndex) => fairnessIndex switch
    {
        > 80 => "分布均匀，每位成员的机会基本一致。",
        >= 50 => "略有偏向，部分成员被点次数偏多。",
        _ => "偏向少数人，建议调整权重或点名方式。"
    };

    /// <summary>
    /// 剥离粘贴名单行首的编号：`1.`、`1、`、`01`、`(1)`、`1)` 等。
    /// 姓名保留原样，权重数字紧随其后时不剥离（例如「张三 100」保持不动）。
    /// </summary>
    public static string StripLineNumbering(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        var match = NumberingPrefix.Match(line);
        if (!match.Success)
        {
            return line;
        }

        var remainder = line[match.Length..];
        var trimmed = remainder.TrimStart();
        // 「1. 张三」编号后跟姓名：剥离；「张三 100」这类行首无编号，不受影响。
        return trimmed.Length == 0 ? line : trimmed;
    }

    /// <summary>
    /// 把粘贴导入的文本预处理为成员导入服务能识别的「姓名 权重」格式：
    /// 按行剥离编号、切分分隔符（空格/制表/逗号/顿号/分号），
    /// 每行只保留第一个姓名与行尾可选权重，供 <see cref="IMemberImportExportService.PreviewImport"/> 复用。
    /// </summary>
    public static string NormalizePastedLines(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var line in content.ReplaceLineEndings("\n").Split('\n'))
        {
            var normalized = StripLineNumbering(line).Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var segments = SeparatorRegex.Split(normalized)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToList();

            // 行尾可选权重：最后一段是纯数字才当权重，否则整行按姓名处理
            var weight = segments.Count > 1
                && double.TryParse(segments[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWeight)
                && parsedWeight >= 0d
                ? segments[^1]
                : null;
            var nameSegments = weight is null ? segments : segments.Take(segments.Count - 1).ToList();
            var name = string.Join(" ", nameSegments);
            if (name.Length == 0)
            {
                continue;
            }

            lines.Add(weight is null ? name : $"{name} {weight}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static readonly Regex SeparatorRegex = new(
        @"[\s,，、;；]+",
        RegexOptions.Compiled);
}