using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public static class AutoResetService
{
    /// <summary>
    /// 记录一次成功抽取。达到间隔时重置传入名单的权重与被点次数。
    /// 设置是全局的，名单则来自当前班级。
    /// </summary>
    public static bool RecordSuccessfulDraw(AppSettings settings, IEnumerable<Member> members)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(members);

        if (!settings.AutoResetWeights)
        {
            return false;
        }

        var interval = Math.Max(1, settings.AutoResetDrawInterval);
        settings.AutoResetDrawInterval = interval;
        settings.DrawsSinceLastAutoReset = Math.Max(0, settings.DrawsSinceLastAutoReset) + 1;
        if (settings.DrawsSinceLastAutoReset < interval)
        {
            return false;
        }

        foreach (var member in members)
        {
            MemberRules.Reset(member);
        }

        settings.DrawsSinceLastAutoReset = 0;
        return true;
    }
}
