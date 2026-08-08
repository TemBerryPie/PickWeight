using System.Text.Json;
using System.Text.Json.Serialization;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

/// <summary>
/// 抽取历史归档。超期条目按「班级文件夹/archive/yyyyMM.json」存放，
/// 主文件 classroom.json 只保留近期记录，避免历史无限增长拖慢加载与保存。
/// </summary>
public sealed class HistoryArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _classroomDirectory;

    public HistoryArchiveService(string classroomDirectory)
    {
        _classroomDirectory = classroomDirectory;
    }

    /// <summary>归档目录：与 classroom.json 同级，固定名为 archive。</summary>
    public string ArchiveDirectory => Path.Combine(_classroomDirectory, "archive");

    /// <summary>
    /// 把 <paramref name="history"/> 中早于 <paramref name="cutoffUtc"/> 的条目
    /// 移入按月归档文件。返回被归档的条目（原对象未克隆）。
    /// </summary>
    public async Task<IReadOnlyList<DrawHistoryEntry>> ArchiveOlderThanAsync(
        List<DrawHistoryEntry> history,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        if (cutoffUtc == default)
        {
            return [];
        }

        // 从最旧开始取，保持归档文件内部按时间升序
        var expired = history
            .Where(entry => entry.DrawnAt < cutoffUtc)
            .OrderBy(entry => entry.DrawnAt)
            .ToList();
        if (expired.Count == 0)
        {
            return [];
        }

        var byMonth = expired.GroupBy(entry => entry.DrawnAt.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var group in byMonth)
        {
            var archivePath = Path.Combine(ArchiveDirectory, $"{group.Key}.json");
            var entries = new List<DrawHistoryEntry>();
            if (File.Exists(archivePath))
            {
                try
                {
                    await using var stream = File.OpenRead(archivePath);
                    var existing = await JsonSerializer.DeserializeAsync<List<DrawHistoryEntry>>(stream, JsonOptions, cancellationToken);
                    if (existing is not null)
                    {
                        entries.AddRange(existing);
                    }
                }
                catch (JsonException)
                {
                    // 归档文件损坏时保留原文件，不覆盖也不丢失
                    entries.Clear();
                }
            }

            entries.AddRange(group.Select(entry => entry.Clone()));
            Directory.CreateDirectory(ArchiveDirectory);
            var temporaryPath = archivePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, archivePath, true);
        }

        foreach (var entry in expired)
        {
            history.Remove(entry);
        }

        return expired;
    }

    /// <summary>
    /// 读取全部归档历史，按时间升序返回。损坏的文件跳过并删除，
    /// 避免统计页每次打开都解析失败。
    /// </summary>
    public async Task<IReadOnlyList<DrawHistoryEntry>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ArchiveDirectory))
        {
            return [];
        }

        var all = new List<DrawHistoryEntry>();
        foreach (var archivePath in Directory.GetFiles(ArchiveDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await using var stream = File.OpenRead(archivePath);
                var entries = await JsonSerializer.DeserializeAsync<List<DrawHistoryEntry>>(stream, JsonOptions, cancellationToken);
                if (entries is null)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    entry.MemberNames ??= [];
                    entry.Records ??= [];
                    entry.EnsureRecords();
                }

                all.AddRange(entries);
            }
            catch (JsonException)
            {
                // 归档文件损坏时删除并继续，避免阻塞统计页
                try
                {
                    File.Delete(archivePath);
                }
                catch (IOException)
                {
                    // 文件可能被其他进程占用，忽略即可
                }
            }
        }

        return all;
    }
}
