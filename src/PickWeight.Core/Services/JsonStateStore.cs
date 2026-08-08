using System.Text.Json;
using System.Text.Json.Serialization;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

/// <summary>
/// 全局状态与各班级数据的持久化。班级按文件夹隔离，文件夹名即班级名。
/// 支持从 v1 单文件结构自动迁移到 v2 多文件夹结构。
/// </summary>
public sealed class JsonStateStore : IStateStore
{
    private readonly string _dataDirectory;
    private readonly string _stateFilePath;
    private readonly string _classroomsDirectory;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonStateStore(string stateFilePath)
    {
        _stateFilePath = stateFilePath;
        _dataDirectory = Path.GetDirectoryName(stateFilePath)
            ?? throw new ArgumentException("状态文件路径无效。", nameof(stateFilePath));
        _classroomsDirectory = Path.Combine(_dataDirectory, "classes");
    }

    public async Task<AppState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            // 全新安装，创建初始状态
            var initialState = new AppState
            {
                SchemaVersion = AppState.CurrentSchemaVersion,
                ClassroomNames = [Classroom.DefaultName]
            };
            await EnsureDefaultClassroomAsync(cancellationToken);
            return initialState;
        }

        // 把整个文件读进内存后立刻关闭句柄。迁移时会对原文件做备份与覆盖，
        // 若保持读取流打开会导致 File.Move 报"访问被拒绝"。
        byte[] raw;
        AppState state;
        try
        {
            raw = await File.ReadAllBytesAsync(_stateFilePath, cancellationToken);
            state = JsonSerializer.Deserialize<AppState>(raw, _options)
                ?? new AppState();
        }
        catch (JsonException)
        {
            // 无法解析的损坏文件不能静默丢弃：进入只读模式，让用户决定是否回退。
            state = new AppState
            {
                IsReadOnly = true,
                StartupNotice = "状态文件已损坏，应用已进入只读模式。请用备份文件替换后重新启动。"
            };
            return state;
        }

        // 检测 v1 结构：顶层有 Members 字段且 SchemaVersion 缺失或为 0
        if (IsV1StructureAsync(raw))
        {
            return await MigrateFromV1Async(raw, state, cancellationToken);
        }

        // v2 结构，与磁盘同步班级列表
        var originalActiveName = state.ActiveClassroomName;
        state.ClassroomNames = (await ListClassroomNamesAsync(cancellationToken)).ToList();
        var wasEmpty = state.ClassroomNames.Count == 0;
        if (wasEmpty)
        {
            state.ClassroomNames = [Classroom.DefaultName];
            await EnsureDefaultClassroomAsync(cancellationToken);
        }

        // 激活的班级若不存在（用户误删文件夹等），回退到第一个可用班级
        if (wasEmpty || !state.ClassroomNames.Contains(originalActiveName))
        {
            state.ActiveClassroomName = state.ClassroomNames[0];
            state.StartupNotice = wasEmpty
                ? "没有找到任何班级数据，已重建默认班级。"
                : $"上次激活的班级“{originalActiveName}”已不存在，已切换到“{state.ClassroomNames[0]}”。";
        }

        return state;
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        if (state.IsReadOnly)
        {
            throw new InvalidOperationException("数据迁移失败，应用处于只读模式，无法保存。");
        }

        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = _stateFilePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, _options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _stateFilePath, true);
    }

    public async Task<Classroom> LoadClassroomAsync(string name, CancellationToken cancellationToken = default)
    {
        var directory = GetClassroomDirectory(name);
        var filePath = Path.Combine(directory, "classroom.json");

        if (!File.Exists(filePath))
        {
            return new Classroom { Name = name };
        }

        await using var stream = File.OpenRead(filePath);
        var classroom = await JsonSerializer.DeserializeAsync<Classroom>(stream, _options, cancellationToken)
            ?? new Classroom { Name = name };

        // 确保姓名已裁剪、隐式权重初始化、历史记录补齐
        foreach (var member in classroom.Members)
        {
            member.Name = member.Name.Trim();
            member.ImplicitWeight = 1d;
            MemberRules.Validate(member);
        }

        foreach (var entry in classroom.DrawHistory)
        {
            entry.MemberNames ??= [];
            entry.Records ??= [];
            entry.EnsureRecords();
        }

        if (classroom.SeatMap is not null)
        {
            foreach (var column in classroom.SeatMap.Columns)
            {
                // 确保 MemberNames 长度与 SeatCount 对齐
                while (column.MemberNames.Count < column.SeatCount)
                {
                    column.MemberNames.Add(null);
                }

                if (column.MemberNames.Count > column.SeatCount)
                {
                    column.MemberNames = column.MemberNames.Take(column.SeatCount).ToList();
                }
            }
        }

        return classroom;
    }

    public async Task SaveClassroomAsync(Classroom classroom, CancellationToken cancellationToken = default)
    {
        foreach (var member in classroom.Members)
        {
            MemberRules.Validate(member);
        }

        var directory = GetClassroomDirectory(classroom.Name);
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, "classroom.json");
        var temporaryPath = filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, classroom, _options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, filePath, true);
    }

    public Task<IReadOnlyList<string>> ListClassroomNamesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_classroomsDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var names = Directory.GetDirectories(_classroomsDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public async Task CreateClassroomAsync(Classroom classroom, CancellationToken cancellationToken = default)
    {
        var validationError = ClassroomNameValidator.Validate(classroom.Name);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var directory = GetClassroomDirectory(classroom.Name);
        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException($"班级“{classroom.Name}”已存在。");
        }

        Directory.CreateDirectory(directory);

        // 创建空的 implicit_config.json
        var configPath = Path.Combine(directory, "implicit_config.json");
        await File.WriteAllTextAsync(configPath, "{}", cancellationToken);

        await SaveClassroomAsync(classroom, cancellationToken);
    }

    public async Task RenameClassroomAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var validationError = ClassroomNameValidator.Validate(newName);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var oldDirectory = GetClassroomDirectory(oldName);
        var newDirectory = GetClassroomDirectory(newName);

        if (!Directory.Exists(oldDirectory))
        {
            throw new InvalidOperationException($"班级“{oldName}”不存在。");
        }

        if (Directory.Exists(newDirectory))
        {
            throw new InvalidOperationException($"班级“{newName}”已存在，无法重命名。");
        }

        // 关键操作：Directory.Move 是原子的，要么成功要么失败，不会留下半个状态
        Directory.Move(oldDirectory, newDirectory);

        // 更新班级内部的 Name 字段
        var classroom = await LoadClassroomAsync(newName, cancellationToken);
        classroom.Name = newName;
        await SaveClassroomAsync(classroom, cancellationToken);
    }

    public Task DeleteClassroomAsync(string name, CancellationToken cancellationToken = default)
    {
        var directory = GetClassroomDirectory(name);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public string GetClassroomDirectory(string name) => Path.Combine(_classroomsDirectory, name);

    private static bool IsV1StructureAsync(byte[] raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var hasMembers = root.TryGetProperty("Members", out _) || root.TryGetProperty("members", out _);
            var schemaVersion = root.TryGetProperty("SchemaVersion", out var versionProp) ? versionProp.GetInt32() : 0;

            return hasMembers && schemaVersion == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<AppState> MigrateFromV1Async(byte[] raw, AppState oldState, CancellationToken cancellationToken)
    {
        try
        {
            // 1. 备份原文件
            var backupPath = _stateFilePath + ".v1.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(_stateFilePath, backupPath, overwrite: false);
            }

            // 2. 解析 v1 完整数据（oldState 可能不完整）
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;

            var members = DeserializeArray<Member>(root, "Members") ?? [];
            var groups = DeserializeArray<Group>(root, "Groups") ?? [];
            var history = DeserializeArray<DrawHistoryEntry>(root, "DrawHistory") ?? [];
            var presets = DeserializeArray<DrawPreset>(root, "DrawPresets") ?? [];
            var settings = TryGetPropertyIgnoreCase(root, "Settings", out var settingsProp)
                ? JsonSerializer.Deserialize<AppSettings>(settingsProp.GetRawText(), _options) ?? new AppSettings()
                : new AppSettings();

            // 3. 创建默认班级文件夹
            var defaultClassroom = new Classroom
            {
                Name = Classroom.DefaultName,
                Members = members,
                Groups = groups,
                DrawHistory = history,
                DrawPresets = presets
            };

            var defaultDirectory = GetClassroomDirectory(Classroom.DefaultName);
            Directory.CreateDirectory(defaultDirectory);

            // 4. 复制 implicit_config.json（如果存在）
            var oldImplicitConfigPath = Path.Combine(AppContext.BaseDirectory, "Data", "implicit_config.json");
            var newImplicitConfigPath = Path.Combine(defaultDirectory, "implicit_config.json");
            if (File.Exists(oldImplicitConfigPath))
            {
                File.Copy(oldImplicitConfigPath, newImplicitConfigPath, overwrite: true);
            }
            else
            {
                await File.WriteAllTextAsync(newImplicitConfigPath, "{}", cancellationToken);
            }

            // 5. 保存默认班级
            await SaveClassroomAsync(defaultClassroom, cancellationToken);

            // 6. 重写 state.json 为 v2 结构
            var newState = new AppState
            {
                SchemaVersion = AppState.CurrentSchemaVersion,
                Settings = settings,
                ActiveClassroomName = Classroom.DefaultName,
                ClassroomNames = [Classroom.DefaultName],
                StartupNotice = $"数据已升级到 v2 结构。原文件已备份为 {Path.GetFileName(backupPath)}。"
            };

            await SaveAsync(newState, cancellationToken);

            return newState;
        }
        catch (Exception ex)
        {
            // 迁移失败，进入只读模式
            oldState.IsReadOnly = true;
            oldState.StartupNotice = $"数据迁移失败：{ex}。应用已进入只读模式，请联系技术支持。";
            return oldState;
        }
    }

    private List<T>? DeserializeArray<T>(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var arrayProp))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<T>>(arrayProp.GetRawText(), _options);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private async Task EnsureDefaultClassroomAsync(CancellationToken cancellationToken)
    {
        var directory = GetClassroomDirectory(Classroom.DefaultName);
        if (!Directory.Exists(directory))
        {
            await CreateClassroomAsync(new Classroom { Name = Classroom.DefaultName }, cancellationToken);
        }
    }
}

