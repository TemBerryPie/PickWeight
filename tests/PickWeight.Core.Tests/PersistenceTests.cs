using System.Text.Json;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task StateStore_RoundTripsState_WithoutRuntimeWeight()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "state.json");
        try
        {
            var store = new JsonStateStore(path);
            var state = new AppState
            {
                Members =
                [
                    new Member
                    {
                        Name = "Alice",
                        Gender = MemberGender.Female,
                        ExplicitWeight = 88d,
                        ImplicitWeight = 2d,
                        CalledCount = 3,
                        IsActive = false
                    }
                ],
                Groups = [new Group { GroupName = "Team", Members = ["Alice"], IsDrawBoostEnabled = false }],
                DrawHistory =
                [
                    new DrawHistoryEntry
                    {
                        DrawnAt = new DateTimeOffset(2026, 6, 14, 10, 30, 0, TimeSpan.FromHours(8)),
                        MemberNames = ["Alice", "Bob"]
                    }
                ],
                Settings = new AppSettings
                {
                    IsFirstRunCompleted = true,
                    Theme = AppTheme.Dark,
                    DrawMode = DrawMode.SmartRandom,
                    DrawTargetCount = 3,
                    DrawIncludeGroupName = "Team",
                    DrawExcludeRecentDrawCount = 2,
                    DrawOnlyNeverDrawn = true,
                    ShowQuickDrawButton = false,
                    ShowDrawNotifications = false,
                    ShowClassIslandNotifications = false,
                    AutoResetWeights = true,
                    AutoResetDrawInterval = 5,
                    DrawsSinceLastAutoReset = 3,
                    BackgroundEffect = AppBackgroundEffect.Mica,
                    AcrylicBlurAmount = 0.55d,
                    AnimationSpeed = 1.5d,
                    AnimationMotionScale = 0.8d,
                    AnimationBounceScale = 1.2d
                }
            };

            await store.SaveAsync(state);
            var loaded = await store.LoadAsync();

            Assert.Equal(88d, loaded.Members[0].ExplicitWeight);
            Assert.False(loaded.Members[0].IsActive);
            Assert.Equal(MemberGender.Female, loaded.Members[0].Gender);
            Assert.Equal(1d, loaded.Members[0].ImplicitWeight);
            Assert.Equal("Alice", loaded.Groups[0].Members[0]);
            Assert.False(loaded.Groups[0].IsDrawBoostEnabled);
            Assert.Equal(["Alice", "Bob"], loaded.DrawHistory[0].MemberNames);
            Assert.Equal(2026, loaded.DrawHistory[0].DrawnAt.Year);
            Assert.Equal(AppTheme.Dark, loaded.Settings.Theme);
            Assert.True(loaded.Settings.IsFirstRunCompleted);
            Assert.Equal(DrawMode.SmartRandom, loaded.Settings.DrawMode);
            Assert.Equal(3, loaded.Settings.DrawTargetCount);
            Assert.Equal("Team", loaded.Settings.DrawIncludeGroupName);
            Assert.Equal(2, loaded.Settings.DrawExcludeRecentDrawCount);
            Assert.True(loaded.Settings.DrawOnlyNeverDrawn);
            Assert.False(loaded.Settings.ShowQuickDrawButton);
            Assert.False(loaded.Settings.ShowDrawNotifications);
            Assert.False(loaded.Settings.ShowClassIslandNotifications);
            Assert.True(loaded.Settings.AutoResetWeights);
            Assert.Equal(5, loaded.Settings.AutoResetDrawInterval);
            Assert.Equal(3, loaded.Settings.DrawsSinceLastAutoReset);
            Assert.Equal(AppBackgroundEffect.Mica, loaded.Settings.BackgroundEffect);
            Assert.Equal(0.55d, loaded.Settings.AcrylicBlurAmount);
            Assert.Equal(1.5d, loaded.Settings.AnimationSpeed);
            Assert.Equal(0.8d, loaded.Settings.AnimationMotionScale);
            Assert.Equal(1.2d, loaded.Settings.AnimationBounceScale);
            Assert.DoesNotContain("ImplicitWeight", await File.ReadAllTextAsync(path));
            Assert.Contains("\"Gender\": \"Female\"", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StateStore_OldState_DefaultsDrawHistoryToEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "state.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, """{"members":[{"name":"Alice"}],"groups":[]}""");

            var loaded = await new JsonStateStore(path).LoadAsync();

            Assert.Empty(loaded.DrawHistory);
            Assert.Null(loaded.Members[0].Gender);
            Assert.False(loaded.Settings.IsFirstRunCompleted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StateStore_NullCollectionsRecoverToEmptyCollections()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "state.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, """{"members":null,"groups":null,"drawHistory":null,"settings":null}""");

            var loaded = await new JsonStateStore(path).LoadAsync();

            Assert.Empty(loaded.Members);
            Assert.Empty(loaded.Groups);
            Assert.Empty(loaded.DrawHistory);
            Assert.NotNull(loaded.Settings);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StateStore_OldSettings_DefaultClassIslandNotificationsToEnabled()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "state.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, """{"settings":{"showDrawNotifications":false}}""");

            var loaded = await new JsonStateStore(path).LoadAsync();

            Assert.True(loaded.Settings.ShowClassIslandNotifications);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WeightProvider_IgnoresInvalidEntries()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Dictionary<string, double>
            {
                ["Alice"] = 1.25d,
                ["Zero"] = 0d,
                ["Negative"] = -1d
            }));

            var result = await new JsonImplicitWeightProvider(path).LoadAsync();
            var weights = result.Weights;

            Assert.Equal(1.25d, weights["alice"]);
            Assert.DoesNotContain("Zero", weights.Keys);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WeightProvider_CreatesMissingConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "implicit_config.json");
        try
        {
            var result = await new JsonImplicitWeightProvider(path).LoadAsync();

            Assert.True(result.WasCreated);
            Assert.True(File.Exists(path));
            Assert.Empty(result.Weights);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WeightProvider_Synchronize_PreservesMatchingAndRenamedValues_AndRemovesOthers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "implicit_config.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, """
                {
                  " Alice ": 2.5,
                  "alice": 9,
                  "Former Name": 4,
                  "Removed": 3,
                  "Invalid": 0
                }
                """);
            var provider = new JsonImplicitWeightProvider(path);

            var result = await provider.SynchronizeAsync(
                [
                    new Member { Name = "Alice" },
                    new Member { Name = "New Name" },
                    new Member { Name = "Brand New" }
                ],
                new Dictionary<string, string> { ["Former Name"] = "New Name" });

            Assert.Equal(2.5d, result.Weights["Alice"]);
            Assert.Equal(4d, result.Weights["New Name"]);
            Assert.Equal(1d, result.Weights["Brand New"]);
            Assert.Equal(2, result.InvalidEntryCount);
            Assert.Equal(3, result.Weights.Count);

            var saved = JsonSerializer.Deserialize<Dictionary<string, double>>(await File.ReadAllTextAsync(path))!;
            Assert.Equal(3, saved.Count);
            Assert.DoesNotContain("Former Name", saved.Keys);
            Assert.DoesNotContain("Removed", saved.Keys);
            Assert.Equal(4d, saved["New Name"]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WeightProvider_Synchronize_RecoversMalformedConfigAndCreatesDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PickWeight-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "implicit_config.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "{ this is not valid JSON");
            var provider = new JsonImplicitWeightProvider(path);

            var result = await provider.SynchronizeAsync([new Member { Name = "Alice" }]);

            Assert.Equal(1, result.InvalidEntryCount);
            Assert.Equal(1d, result.Weights["Alice"]);
            var saved = JsonSerializer.Deserialize<Dictionary<string, double>>(await File.ReadAllTextAsync(path));
            Assert.Equal(1d, saved!["Alice"]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
