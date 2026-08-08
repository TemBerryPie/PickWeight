using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

public sealed class AnimationService
{
    private const string OffsetAnimation = "Offset";
    private const string OpacityAnimation = "Opacity";
    private const string ScaleAnimation = "Scale";

    private readonly ISettingsService _settings;
    private readonly Dictionary<UIElement, int> _activeRuns = [];
    private int _nextRunId;

    public AnimationService(ISettingsService settings)
    {
        _settings = settings;
    }

    public void PlayResultReveal(UIElement element, int rowIndex) =>
        Play(element, rowIndex * 80d, 760d, new Vector2(0f, 18f), 0.04d);

    public void PlayEntrance(UIElement element) =>
        Play(element, 0d, 300d, new Vector2(0f, 12f), 0.025d);

    public void PlayPopupEntrance(UIElement element) =>
        Play(element, 0d, 480d, new Vector2(0f, 20f), 0.075d);

    /// <summary>
    /// 快速名字滚动动画：仅轻微缩放脉冲，不启动 Opacity 动画、不把 Opacity 置零，
    /// 避免抽取滚动阶段快速节拍下每拍淡入淡出造成的闪烁。完成后由 Reset 复位。
    /// </summary>
    public void PlayNameTick(UIElement element)
    {
        Stop(element);

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var speed = Math.Clamp(_settings.Current.AnimationSpeed, 0.5d, 2d);
        var motion = Math.Clamp(_settings.Current.AnimationMotionScale, 0d, 2d);
        var bounce = Math.Clamp(_settings.Current.AnimationBounceScale, 0d, 2d);
        var duration = TimeSpan.FromMilliseconds(70d / speed);
        var runId = ++_nextRunId;

        visual.CenterPoint = new Vector3(element.ActualSize.X / 2f, element.ActualSize.Y / 2f, 0f);
        _activeRuns[element] = runId;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(ScaleAnimation, CreateScaleAnimation(
            compositor,
            (float)(0.035d * motion),
            (float)bounce,
            duration,
            TimeSpan.Zero));
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (_activeRuns.TryGetValue(element, out var activeRunId) && activeRunId == runId)
            {
                _activeRuns.Remove(element);
                Reset(visual);
            }
        };
    }

    public void PlayPageSection(UIElement element, int sectionIndex)
    {
        var horizontalDirection = sectionIndex % 2 == 0 ? -1f : 1f;
        Play(element, sectionIndex * 65d, 560d, new Vector2(14f * horizontalDirection, 10f), 0.025d);
    }

    public void PlayListItem(UIElement element, int itemIndex) =>
        Play(element, Math.Min(itemIndex, 8) * 45d, 480d, new Vector2(10f, 6f), 0.018d);

    public void Stop(UIElement element)
    {
        _activeRuns.Remove(element);
        Reset(ElementCompositionPreview.GetElementVisual(element));
    }

    private void Play(
        UIElement element,
        double baseDelayMilliseconds,
        double baseDurationMilliseconds,
        Vector2 baseOffset,
        double baseInitialScaleReduction)
    {
        Stop(element);

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var speed = Math.Clamp(_settings.Current.AnimationSpeed, 0.5d, 2d);
        var motion = Math.Clamp(_settings.Current.AnimationMotionScale, 0d, 2d);
        var bounce = Math.Clamp(_settings.Current.AnimationBounceScale, 0d, 2d);
        var delay = TimeSpan.FromMilliseconds(baseDelayMilliseconds / speed);
        var duration = TimeSpan.FromMilliseconds(baseDurationMilliseconds / speed);
        var originalOffset = visual.Offset;
        var runId = ++_nextRunId;

        visual.CenterPoint = new Vector3(element.ActualSize.X / 2f, element.ActualSize.Y / 2f, 0f);
        visual.Opacity = 0f;
        _activeRuns[element] = runId;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(OpacityAnimation, CreateOpacityAnimation(compositor, duration, delay));
        visual.StartAnimation(OffsetAnimation, CreateOffsetAnimation(
            compositor,
            originalOffset,
            baseOffset * (float)motion,
            duration,
            delay));
        visual.StartAnimation(ScaleAnimation, CreateScaleAnimation(
            compositor,
            (float)(baseInitialScaleReduction * motion),
            (float)bounce,
            duration,
            delay));
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (_activeRuns.TryGetValue(element, out var activeRunId) && activeRunId == runId)
            {
                _activeRuns.Remove(element);
                Reset(visual);
            }
        };
    }

    private static ScalarKeyFrameAnimation CreateOpacityAnimation(
        Compositor compositor,
        TimeSpan duration,
        TimeSpan delay)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, 0f);
        animation.InsertKeyFrame(0.28f, 1f, EaseOut(compositor));
        animation.InsertKeyFrame(1f, 1f);
        return animation;
    }

    private static Vector3KeyFrameAnimation CreateOffsetAnimation(
        Compositor compositor,
        Vector3 originalOffset,
        Vector2 offset,
        TimeSpan duration,
        TimeSpan delay)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, originalOffset + new Vector3(offset, 0f));
        animation.InsertKeyFrame(0.62f, originalOffset, EaseOut(compositor));
        animation.InsertKeyFrame(1f, originalOffset);
        return animation;
    }

    private static Vector3KeyFrameAnimation CreateScaleAnimation(
        Compositor compositor,
        float initialReduction,
        float bounce,
        TimeSpan duration,
        TimeSpan delay)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, UniformScale(1f - initialReduction));
        animation.InsertKeyFrame(0.48f, UniformScale(1f + 0.025f * bounce), EaseOut(compositor));
        animation.InsertKeyFrame(0.7f, UniformScale(1f - 0.012f * bounce), EaseInOut(compositor));
        animation.InsertKeyFrame(0.88f, UniformScale(1f + 0.005f * bounce), EaseInOut(compositor));
        animation.InsertKeyFrame(1f, Vector3.One, EaseInOut(compositor));
        return animation;
    }

    private static CubicBezierEasingFunction EaseOut(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

    private static CubicBezierEasingFunction EaseInOut(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));

    private static Vector3 UniformScale(float value) => new(value, value, 1f);

    private static void Reset(Visual visual)
    {
        visual.StopAnimation(OpacityAnimation);
        visual.StopAnimation(OffsetAnimation);
        visual.StopAnimation(ScaleAnimation);
        visual.Opacity = 1f;
        visual.Scale = Vector3.One;
    }
}
