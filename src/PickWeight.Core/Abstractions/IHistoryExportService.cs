using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface IHistoryExportService
{
    IReadOnlyList<DrawHistoryEntry> Filter(
        IReadOnlyList<DrawHistoryEntry> history,
        IReadOnlyList<Group> groups,
        HistoryFilterOptions options);

    string ExportCsv(
        IReadOnlyList<DrawHistoryEntry> history,
        IReadOnlyList<Group> groups);

    string ExportJson(
        IReadOnlyList<DrawHistoryEntry> history,
        IReadOnlyList<Group> groups,
        HistoryFilterOptions options,
        DateTimeOffset exportedAt);
}
