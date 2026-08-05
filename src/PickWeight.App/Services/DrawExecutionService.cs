using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight_App.Services;

/// <summary>
/// Coordinates a prepared draw with its observable side effects.  Keeping the
/// draw service pure lets the main page and the quick-draw window use the same
/// candidate, history, count, reset and notification rules.
/// </summary>
public interface IDrawExecutionService
{
    Task<PreparedDraw> PrepareAsync(DrawExecutionRequest request, CancellationToken cancellationToken = default);

    Task<DrawCommitOutcome> CommitAsync(
        PreparedDraw prepared,
        IEnumerable<Member>? members = null,
        bool confirmSelection = false,
        bool notify = true,
        CancellationToken cancellationToken = default);

    Task RebuildCommittedStateAsync(
        PreparedDraw prepared,
        IEnumerable<Member> committedMembers,
        CancellationToken cancellationToken = default);

    Task DiscardAsync(PreparedDraw prepared, CancellationToken cancellationToken = default);
}

public sealed record DrawExecutionRequest(
    int TargetCount,
    DrawFilterOptions Filters,
    DrawMode? Mode = null,
    bool AutoCommitSelection = false);

public sealed class PreparedDraw
{
    internal PreparedDraw(
        DrawMode drawMode,
        DrawFilterOptions filters,
        DrawResult result,
        IReadOnlyList<string> candidateNames,
        IReadOnlyDictionary<string, DrawExecutionService.MemberSnapshot> snapshots,
        int drawsSinceLastAutoReset,
        bool autoCommitSelection)
    {
        DrawMode = drawMode;
        Filters = filters;
        Result = result;
        CandidateNames = candidateNames;
        Snapshots = snapshots;
        DrawsSinceLastAutoReset = drawsSinceLastAutoReset;
        AutoCommitSelection = autoCommitSelection;
    }

    public DrawMode DrawMode { get; }
    public DrawFilterOptions Filters { get; }
    public DrawResult Result { get; }
    public IReadOnlyList<Member> SelectedMembers => Result.SelectedMembers;
    public IReadOnlyList<string> CandidateNames { get; }
    public bool HasCandidates => CandidateNames.Count > 0;

    internal IReadOnlyDictionary<string, DrawExecutionService.MemberSnapshot> Snapshots { get; }
    internal int DrawsSinceLastAutoReset { get; }
    internal bool AutoCommitSelection { get; }
    internal DrawHistoryEntry? HistoryEntry { get; set; }
    internal bool AutoResetRecorded { get; set; }
    internal bool WasAutoReset { get; set; }
    internal HashSet<string> CommittedNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record DrawCommitOutcome(
    bool Succeeded,
    bool WasAutoReset,
    bool ClassIslandSucceeded,
    IReadOnlyList<string> CommittedNames);

public sealed class DrawExecutionService : IDrawExecutionService
{
    private readonly AppDataService _data;
    private readonly IDrawService _drawService;
    private readonly ISettingsService _settings;
    private readonly NotificationService _notifications;
    private readonly IClassIslandNotificationBridge _classIslandNotifications;
    private readonly ObsOverlayService _obsOverlay;
    private readonly AppLogService _log;

    public DrawExecutionService(
        AppDataService data,
        IDrawService drawService,
        ISettingsService settings,
        NotificationService notifications,
        IClassIslandNotificationBridge classIslandNotifications,
        ObsOverlayService obsOverlay,
        AppLogService log)
    {
        _data = data;
        _drawService = drawService;
        _settings = settings;
        _notifications = notifications;
        _classIslandNotifications = classIslandNotifications;
        _obsOverlay = obsOverlay;
        _log = log;
    }

    public async Task<PreparedDraw> PrepareAsync(
        DrawExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        await _data.ReloadImplicitWeightsAsync(cancellationToken);

        var mode = request.Mode ?? _settings.Current.DrawMode;
        var filteredMembers = DrawFilterService.Apply(
            _data.State.Members,
            _data.State.Groups,
            _data.State.DrawHistory,
            request.Filters);
        var preview = _drawService.PreviewProbabilities(
            filteredMembers,
            _data.State.Groups,
            new DrawOptions(mode));
        var result = _drawService.Draw(
            filteredMembers,
            _data.State.Groups,
            Math.Max(1, request.TargetCount),
            new DrawOptions(mode));

        return new PreparedDraw(
            mode,
            request.Filters,
            result,
            preview.Select(item => item.Name).ToList(),
            CaptureSnapshots(_data.State.Members),
            _data.State.Settings.DrawsSinceLastAutoReset,
            request.AutoCommitSelection);
    }

    public async Task<DrawCommitOutcome> CommitAsync(
        PreparedDraw prepared,
        IEnumerable<Member>? members = null,
        bool confirmSelection = false,
        bool notify = true,
        CancellationToken cancellationToken = default)
    {
        var selected = (members ?? prepared.SelectedMembers)
            .Where(member => prepared.SelectedMembers.Contains(member))
            .DistinctBy(member => Normalize(member.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0)
        {
            return new DrawCommitOutcome(false, prepared.WasAutoReset, true, []);
        }

        var shouldIncrement = prepared.DrawMode switch
        {
            DrawMode.FullRandom => false,
            DrawMode.Selection => confirmSelection || prepared.AutoCommitSelection,
            _ => true
        };

        if (shouldIncrement)
        {
            foreach (var member in selected.Where(member => prepared.CommittedNames.Add(Normalize(member.Name))))
            {
                MemberRules.IncrementCalledCount(member, prepared.DrawMode);
            }
        }
        else
        {
            foreach (var member in selected)
            {
                prepared.CommittedNames.Add(Normalize(member.Name));
            }
        }

        UpdateHistory(prepared);
        if (!prepared.AutoResetRecorded && prepared.CommittedNames.Count > 0 && ShouldRecordAutoReset(prepared.DrawMode))
        {
            prepared.AutoResetRecorded = true;
            prepared.WasAutoReset = AutoResetService.RecordSuccessfulDraw(_data.State);
        }

        await _data.SaveAsync(cancellationToken);

        var names = selected.Select(member => member.Name).ToList();
        var classIslandSucceeded = true;
        if (notify)
        {
            _notifications.ShowDrawResult(names);
            classIslandSucceeded = (await _classIslandNotifications.ShowDrawResultAsync(names, cancellationToken)).Succeeded;
        }

        if (names.Count > 0)
        {
            await _obsOverlay.WriteLatestDrawAsync(names, cancellationToken);
            await _log.InfoAsync($"Committed draw: {names.Count} member(s); ClassIsland={classIslandSucceeded}.", cancellationToken);
        }
        return new DrawCommitOutcome(true, prepared.WasAutoReset, classIslandSucceeded, names);
    }

    public async Task RebuildCommittedStateAsync(
        PreparedDraw prepared,
        IEnumerable<Member> committedMembers,
        CancellationToken cancellationToken = default)
    {
        RestoreSnapshots(prepared.Snapshots);
        _data.State.Settings.DrawsSinceLastAutoReset = prepared.DrawsSinceLastAutoReset;
        if (prepared.HistoryEntry is not null)
        {
            _data.State.DrawHistory.Remove(prepared.HistoryEntry);
            prepared.HistoryEntry = null;
        }

        prepared.CommittedNames.Clear();
        prepared.AutoResetRecorded = false;
        prepared.WasAutoReset = false;

        var remaining = committedMembers.ToList();
        if (remaining.Count > 0)
        {
            await CommitAsync(
                prepared,
                remaining,
                confirmSelection: prepared.DrawMode == DrawMode.Selection,
                notify: false,
                cancellationToken);
            return;
        }

        await _data.SaveAsync(cancellationToken);
    }

    public Task DiscardAsync(PreparedDraw prepared, CancellationToken cancellationToken = default) =>
        RebuildCommittedStateAsync(prepared, [], cancellationToken);

    private void UpdateHistory(PreparedDraw prepared)
    {
        var names = prepared.SelectedMembers
            .Where(member => prepared.CommittedNames.Contains(Normalize(member.Name)))
            .Select(member => member.Name)
            .ToList();
        if (names.Count == 0)
        {
            return;
        }

        if (prepared.HistoryEntry is null)
        {
            prepared.HistoryEntry = new DrawHistoryEntry
            {
                DrawnAt = DateTimeOffset.Now,
                MemberNames = names
            };
            _data.State.DrawHistory.Add(prepared.HistoryEntry);
            return;
        }

        prepared.HistoryEntry.MemberNames = names;
    }

    private static bool ShouldRecordAutoReset(DrawMode mode) =>
        mode is DrawMode.Weighted or DrawMode.SmartRandom or DrawMode.Selection;

    private static Dictionary<string, MemberSnapshot> CaptureSnapshots(IEnumerable<Member> members) =>
        members
            .GroupBy(member => Normalize(member.Name), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => MemberSnapshot.Capture(group.First()),
                StringComparer.OrdinalIgnoreCase);

    private void RestoreSnapshots(IReadOnlyDictionary<string, MemberSnapshot> snapshots)
    {
        foreach (var member in _data.State.Members)
        {
            if (snapshots.TryGetValue(Normalize(member.Name), out var snapshot))
            {
                snapshot.Restore(member);
            }
        }
    }

    private static string Normalize(string value) => value.Trim();

    internal sealed record MemberSnapshot(double ExplicitWeight, int CalledCount, bool IsWeightLinked)
    {
        public static MemberSnapshot Capture(Member member) =>
            new(member.ExplicitWeight, member.CalledCount, member.IsWeightLinked);

        public void Restore(Member member)
        {
            member.ExplicitWeight = ExplicitWeight;
            member.CalledCount = CalledCount;
            member.IsWeightLinked = IsWeightLinked;
        }
    }
}
