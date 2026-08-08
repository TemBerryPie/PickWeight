using System.Text.Json;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class HistoryArchiveTests
{
    private static string CreateTempDirectory() =>
        Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");

    private static DrawHistoryEntry Entry(int year, int month, int day) => new()
    {
        DrawnAt = new DateTimeOffset(year, month, day, 10, 0, 0, TimeSpan.FromHours(8)),
        MemberNames = [$"{year}-{month}-{day}"]
    };

    // ---------- 归档 ----------

    [Fact]
    public async Task Archive_OlderEntries_MovesToMonthlyFileAndKeepsRecentInPlace()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var service = new HistoryArchiveService(directory);
            var history = new List<DrawHistoryEntry>
            {
                Entry(2026, 1, 5),
                Entry(2026, 2, 10),
                Entry(2026, 7, 20)
            };
            // 截止到 2026-06-01：1 月和 2 月应归档，7 月保留
            var cutoff = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

            var archived = await service.ArchiveOlderThanAsync(history, cutoff);

            Assert.Equal(2, archived.Count);
            Assert.Single(history);
            Assert.Equal(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.FromHours(8)), history[0].DrawnAt);
            Assert.True(File.Exists(Path.Combine(directory, "archive", "202601.json")));
            Assert.True(File.Exists(Path.Combine(directory, "archive", "202602.json")));

            var january = await ReadArchiveAsync(directory, "202601.json");
            Assert.Single(january);
            Assert.Equal("2026-1-5", january[0].MemberNames[0]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Archive_NothingExpired_DoesNotCreateFolder()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var service = new HistoryArchiveService(directory);
            var history = new List<DrawHistoryEntry> { Entry(2026, 7, 20) };

            var archived = await service.ArchiveOlderThanAsync(history, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.Empty(archived);
            Assert.Single(history);
            Assert.False(Directory.Exists(Path.Combine(directory, "archive")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Archive_MergesIntoExistingMonthlyFile_WithoutDuplicates()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var service = new HistoryArchiveService(directory);
            // 第一次归档：1 月 5 日
            var history = new List<DrawHistoryEntry> { Entry(2026, 1, 5), Entry(2026, 7, 1) };
            await service.ArchiveOlderThanAsync(history, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

            // 第二次归档：1 月 15 日，应合并进同一个文件
            var history2 = new List<DrawHistoryEntry> { Entry(2026, 1, 15), Entry(2026, 7, 2) };
            await service.ArchiveOlderThanAsync(history2, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

            var january = await ReadArchiveAsync(directory, "202601.json");
            Assert.Equal(2, january.Count);
            Assert.Equal(["2026-1-5", "2026-1-15"], january.Select(entry => entry.MemberNames[0]).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Archive_DefaultCutoff_DoesNotArchiveRecentEntries()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var service = new HistoryArchiveService(directory);
            var history = new List<DrawHistoryEntry>
            {
                Entry(DateTime.Now.Year, DateTime.Now.Month, 1),
                Entry(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day)
            };

            var archived = await service.ArchiveOlderThanAsync(
                history,
                DateTimeOffset.Now.AddDays(-30).UtcDateTime);

            Assert.Empty(archived);
            Assert.Equal(2, history.Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    // ---------- 读取归档 ----------

    [Fact]
    public async Task LoadAll_ReturnsAllMonthlyFiles_InChronologicalOrder()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "archive"));
            var service = new HistoryArchiveService(directory);
            await WriteArchiveAsync(directory, "202601.json",
            [
                new DrawHistoryEntry { DrawnAt = new DateTimeOffset(2026, 1, 3, 9, 0, 0, TimeSpan.FromHours(8)), MemberNames = ["一月"] }
            ]);
            await WriteArchiveAsync(directory, "202602.json",
            [
                new DrawHistoryEntry { DrawnAt = new DateTimeOffset(2026, 2, 3, 9, 0, 0, TimeSpan.FromHours(8)), MemberNames = ["二月"] }
            ]);

            var all = await service.LoadAllAsync();

            Assert.Equal(2, all.Count);
            Assert.Equal(["一月", "二月"], all.Select(entry => entry.MemberNames[0]).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LoadAll_NoArchiveFolder_ReturnsEmpty()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var service = new HistoryArchiveService(directory);

            var all = await service.LoadAllAsync();

            Assert.Empty(all);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LoadAll_CorruptFile_IsSkippedAndDeleted()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "archive"));
            var corruptPath = Path.Combine(directory, "archive", "202501.json");
            await File.WriteAllTextAsync(corruptPath, "{ not valid json");
            var service = new HistoryArchiveService(directory);

            var all = await service.LoadAllAsync();

            Assert.Empty(all);
            Assert.False(File.Exists(corruptPath), "损坏的归档文件应被删除，避免反复解析失败");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    // ---------- 公平性指数 ----------

    [Fact]
    public void FairnessIndex_PerfectlyEven_Is100()
    {
        Assert.Equal(100d, FairnessService.ComputeFairnessIndex([5, 5, 5, 5]));
    }

    [Fact]
    public void FairnessIndex_SingleMember_Is100()
    {
        Assert.Equal(100d, FairnessService.ComputeFairnessIndex([7]));
    }

    [Fact]
    public void FairnessIndex_EmptyOrAllZero_Is100()
    {
        Assert.Equal(100d, FairnessService.ComputeFairnessIndex([]));
        Assert.Equal(100d, FairnessService.ComputeFairnessIndex([0, 0, 0]));
    }

    [Fact]
    public void FairnessIndex_OneMemberDominates_IsLow()
    {
        // [100, 0, 0]：基尼系数为 (n-1)/(2n) = 1/3，指数约 66.7
        var index = FairnessService.ComputeFairnessIndex([100, 0, 0]);
        Assert.InRange(index, 60d, 75d);

        // 四人场景一人独点：基尼系数 (n-1)/(2n) = 0.375，指数 62.5
        var four = FairnessService.ComputeFairnessIndex([100, 0, 0, 0]);
        Assert.Equal(62.5d, four, 3);
    }

    [Fact]
    public void FairnessIndex_ModerateImbalance_SitsInMiddleRange()
    {
        var index = FairnessService.ComputeFairnessIndex([10, 2, 1]);
        Assert.InRange(index, 40d, 90d);
    }

    [Fact]
    public void FairnessIndex_IncludesNeverDrawnMembersAsZero()
    {
        // B2 回归：公平性指数必须把从未被点过的成员按 0 计入，否则只统计
        // 「至少被点过一次」的成员会让指数严重虚高（50 人只点一半仍接近 100）。
        var drawnOnly = FairnessService.ComputeFairnessIndex([4, 4]);
        var fullRoster = FairnessService.ComputeFairnessIndex(
            Enumerable.Range(0, 50).Select(index => index < 25 ? 4 : 0).ToArray());

        Assert.Equal(100d, drawnOnly, 3);
        Assert.InRange(fullRoster, 60d, 85d);
        Assert.True(fullRoster < drawnOnly, "包含 0 次成员的公平性指数应显著低于只统计已点成员的指数。");
    }

    [Fact]
    public void FairnessDescription_MapsThresholds()
    {
        Assert.Contains("分布均匀", FairnessService.DescribeFairnessIndex(90d));
        Assert.Contains("略有偏向", FairnessService.DescribeFairnessIndex(65d));
        Assert.Contains("偏向少数人", FairnessService.DescribeFairnessIndex(30d));
    }

    private static async Task<List<DrawHistoryEntry>> ReadArchiveAsync(string directory, string fileName)
    {
        var path = Path.Combine(directory, "archive", fileName);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<DrawHistoryEntry>>(stream) ?? [];
    }

    private static async Task WriteArchiveAsync(string directory, string fileName, List<DrawHistoryEntry> entries)
    {
        var path = Path.Combine(directory, "archive", fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entries));
    }
}
