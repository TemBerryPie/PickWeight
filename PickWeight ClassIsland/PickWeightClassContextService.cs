using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;
using PickWeight.ClassIsland.Protocol;

namespace PickWeight.ClassIsland.Plugin;

/// <summary>
/// 从 ClassIsland 读取当前课时上下文（科目、第几节、是否上课、下课时间）。
/// 供主程序写入抽取历史与下课收尾。ClassIsland 未就绪或非上课时间时返回空上下文。
/// </summary>
public sealed class PickWeightClassContextService
{
    private readonly IServiceProvider _services;

    public PickWeightClassContextService(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>查询当前课时上下文。所有获取动作都容错，任何失败返回 null 上下文。</summary>
    public ClassContext? QueryCurrentContext()
    {
        try
        {
            var lessons = _services.GetService(typeof(ILessonsService));
            if (lessons is null)
            {
                return null;
            }

            var plan = ((ILessonsService)lessons).GetClassPlanByDate(DateTime.Now, out _);
            if (plan is null)
            {
                return null;
            }

            var now = DateTime.Now;
            var nowSeconds = (int)(now - DateTime.Today).TotalSeconds;

            // 找到当前时刻所在的课节
            ClassInfo? currentClass = null;
            TimeLayoutItem? currentItem = null;
            var periodIndex = 0;
            var index = 0;
            foreach (var info in plan.Classes)
            {
                var item = plan.TimeLayout?.Layouts.ElementAtOrDefault(info.Index);
                if (item is null)
                {
                    index++;
                    continue;
                }

                if (info.IsEnabled && !info.IsEmpty && IsWithinLesson(item, nowSeconds))
                {
                    currentClass = info;
                    currentItem = item;
                    periodIndex = index;
                    break;
                }

                index++;
            }

            if (currentClass is null || currentItem is null)
            {
                return null;
            }

            var endsAt = DateTime.Today.AddSeconds(GetEndSeconds(currentItem));
            // 科目字典位于 IProfileService 内部，插件 API 未公开读取入口，暂返回 null；
            // 课节序号与下课时间不受影响。
            var subjectName = ResolveSubjectName(currentClass.SubjectId);

            return new ClassContext(
                Subject: subjectName,
                PeriodIndex: periodIndex + 1, // 1-based 第几节
                IsOnClass: true,
                PeriodEndsAt: endsAt);
        }
        catch
        {
            // ClassIsland 服务未就绪或数据异常时静默降级
            return null;
        }
    }

    private static bool IsWithinLesson(TimeLayoutItem item, int nowSeconds)
    {
        var start = GetStartSeconds(item);
        var end = GetEndSeconds(item);
        return nowSeconds >= start && nowSeconds < end;
    }

    /// <summary>解析课节开始秒数。StartTime 为 TimeSpan。</summary>
    private static int GetStartSeconds(TimeLayoutItem item) => (int)item.StartTime.TotalSeconds;

    private static int GetEndSeconds(TimeLayoutItem item) => (int)item.EndTime.TotalSeconds;

    /// <summary>
    /// 解析科目名。科目字典位于 ClassIsland 内部服务中，插件 API 未公开读取入口，
    /// 当前返回 null；后续 SDK 开放后可补全。
    /// </summary>
    private static string? ResolveSubjectName(Guid subjectId) => null;
}
