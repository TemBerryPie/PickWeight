# PickWeight 开发指导

## 目录

- [项目概述](#项目概述)
- [技术栈](#技术栈)
- [架构设计](#架构设计)
- [项目结构](#项目结构)
- [核心概念](#核心概念)
- [开发环境搭建](#开发环境搭建)
- [编码规范](#编码规范)
- [新增页面指南](#新增页面指南)
- [新增服务指南](#新增服务指南)
- [功能改进路线图](#功能改进路线图)
- [常见开发任务](#常见开发任务)

---

## 项目概述

PickWeight 是一个基于加权概率的桌面点名器。支持四种抽取模式、小组联动、名单导入导出、系统托盘常驻，并可作为 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 插件运行。

核心能力：

- **加权抽取**：按 70% 配置权重 + 30% 程序综合权重的混合概率不放回抽取
- **被点次数衰减**：已被多次点名的成员概率自动降低
- **小组联动**：同组成员在单人抽中后其余成员概率升高
- **多种模式**：加权、智能随机、完全随机、选择

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 8 |
| UI 框架 | WinUI 3 (Windows App SDK) |
| MVVM 框架 | CommunityToolkit.Mvvm 8.x |
| 依赖注入 | Microsoft.Extensions.DependencyInjection |
| 序列化 | System.Text.Json |
| 安全随机 | System.Security.Cryptography.RandomNumberGenerator |
| 桌面集成 | Windows App SDK + P/Invoke (user32, dwmapi) |
| 测试 | xUnit 2.x |
| ClassIsland 插件 | ClassIsland Core SDK + Avalonia UI |

## 架构设计

```
┌─────────────────────────────────────────────────────────┐
│                    PickWeight.App                       │
│  ┌──────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │  Pages/  │  │  ViewModels/ │  │    Services/     │  │
│  │  *.xaml  │◄─┤  *ViewModel  │──┤  AppDataService  │  │
│  │  *.cs    │  │  *RowVM      │  │  SettingsService │  │
│  └──────────┘  └──────────────┘  │  AnimationSvc    │  │
│                                   │  NotificationSvc │  │
│  ┌──────────────┐                 │  ThemeService    │  │
│  │ QuickDrawWin │                 │  TrayIconService │  │
│  │ TrayMenuWin  │                 │  StartupService  │  │
│  └──────────────┘                 └────────┬─────────┘  │
├────────────────────────────────────────────┼────────────┤
│                PickWeight.Core             │            │
│  ┌──────────┐  ┌──────────────┐  ┌───────┴─────────┐  │
│  │  Models/ │  │ Abstractions/│  │   Services/     │  │
│  │  Member  │  │ IDrawService │  │  DrawService    │  │
│  │  Group   │  │ IStateStore  │  │  JsonStateStore │  │
│  │  AppState│  │ ISettingsSvc │  │  MemberRules    │  │
│  │  Draw*   │  │ IImportExport│  │  AutoResetSvc   │  │
│  └──────────┘  └──────────────┘  └─────────────────┘  │
├─────────────────────────────────────────────────────────┤
│  PickWeight.Tray          PickWeight.ClassIsland.*      │
│  ┌──────────┐             ┌──────────────────────┐      │
│  │ TrayHost │             │ Plugin (ClassIsland)  │      │
│  │(WinForms)│             │ PipeListenerService   │      │
│  └──────────┘             │ NotificationProvider  │      │
│                           │ Protocol (named pipe) │      │
│                           └──────────────────────┘      │
└─────────────────────────────────────────────────────────┘
```

### 分层原则

1. **Core 层**：纯 C# 逻辑，不依赖任何 UI 框架。包含模型、抽象接口和服务实现
2. **App 层**：WinUI 3 界面、ViewModel、应用级服务（主题、动画、托盘等）
3. **Tray 层**：系统托盘宿主，极薄的 WinForms 封装
4. **ClassIsland 层**：ClassIsland 插件、命名管道协议

### 关键设计决策

| 决策 | 原因 |
|---|---|
| `AppState` 单例持有全部数据 | 避免多个数据副本同步问题 |
| 页面通过 ViewModel 操作草稿，保存时才写回 | 支持"放弃修改"和脏状态跟踪 |
| `DrawService` 可注入随机源 | 支持确定性测试 |
| `DrawSession` 记录抽取前快照 | 支持丢弃/部分提交后回滚 |
| 状态文件使用 `.tmp` 覆写 | 防止写入中断导致数据损坏 |
| 隐式权重由 `implicit_config.json` 派生 | 将外部配置与运行时权重解耦 |
| ClassIsland 使用命名管道通信 | 同机进程间低延迟通信 |

## 项目结构

```
Name Picker/
├── README.md
├── DEVELOPMENT.md                        ← 本文件
├── implicit_config.sample.json           ← 隐式权重配置示例
│
├── src/
│   ├── PickWeight.Core/                  ← 核心库（无 UI 依赖）
│   │   ├── Models/
│   │   │   ├── Member.cs                 ← 成员：姓名、权重、被点次数、启用状态
│   │   │   ├── Group.cs                  ← 小组：名称、成员列表、联动倍率
│   │   │   ├── AppState.cs               ← 应用根状态：成员+小组+历史+设置
│   │   │   ├── AppSettings.cs            ← 设置：主题、模式、动画、通知等
│   │   │   ├── DrawMode.cs               ← 抽取模式枚举 + DrawOptions
│   │   │   ├── DrawResult.cs             ← 抽取结果：选中成员、权重明细
│   │   │   ├── DrawHistoryEntry.cs       ← 历史记录条目
│   │   │   ├── ImportResult.cs           ← 导入结果统计
│   │   │   ├── ImplicitWeightLoadResult.cs ← 隐式权重加载结果
│   │   │   └── SettingsChangedEventArgs.cs ← 设置变更事件参数
│   │   ├── Abstractions/
│   │   │   ├── IDrawService.cs           ← 抽取服务接口
│   │   │   ├── IStateStore.cs            ← 状态持久化接口
│   │   │   ├── ISettingsService.cs       ← 设置服务接口
│   │   │   ├── IMemberImportExportService.cs ← 成员导入导出接口
│   │   │   ├── IGroupImportExportService.cs  ← 小组导入导出接口
│   │   │   └── IImplicitWeightProvider.cs    ← 隐式权重提供方接口
│   │   └── Services/
│   │       ├── DrawService.cs            ← 核心抽取算法
│   │       ├── MemberRules.cs            ← 成员规则：权重计算、次数增减、重置
│   │       ├── JsonStateStore.cs         ← JSON 文件持久化
│   │       ├── JsonImplicitWeightProvider.cs ← 隐式权重配置加载
│   │       ├── MemberImportExportService.cs  ← 成员 TXT 导入导出
│   │       ├── GroupImportExportService.cs   ← 小组 JSON 导入导出
│   │       └── AutoResetService.cs       ← 自动重置权重逻辑
│   │
│   ├── PickWeight.App/                   ← WinUI 3 主应用
│   │   ├── App.xaml / App.xaml.cs        ← 应用入口、DI 容器配置
│   │   ├── MainWindow.xaml / .cs         ← 主窗口：标题栏、Mica/Acrylic 背景
│   │   ├── MainPage.xaml / .cs           ← 导航壳层：NavigationView + Frame
│   │   ├── QuickDrawWindow.xaml / .cs    ← 浮动快捷点名按钮窗口
│   │   ├── TrayMenuWindow.xaml / .cs     ← 系统托盘右键弹出菜单
│   │   ├── Pages/
│   │   │   ├── DrawPage.xaml / .cs       ← 开始点名页
│   │   │   ├── MembersPage.xaml / .cs    ← 权重编辑器页
│   │   │   ├── GroupsPage.xaml / .cs     ← 小组绑定器页
│   │   │   ├── HistoryPage.xaml / .cs    ← 抽取历史页
│   │   │   └── SettingsPage.xaml / .cs   ← 设置页
│   │   ├── ViewModels/
│   │   │   ├── DrawPageViewModel.cs      ← 抽取逻辑、结果管理、会话回滚
│   │   │   ├── DrawResultItemViewModel.cs ← 单条抽取结果的行 VM
│   │   │   ├── MembersPageViewModel.cs   ← 成员列表编辑、导入导出
│   │   │   ├── MemberRowViewModel.cs     ← 单条成员的行 VM
│   │   │   ├── GroupsPageViewModel.cs    ← 小组列表编辑、导入导出
│   │   │   ├── GroupEditorViewModel.cs   ← 单个小组编辑器 VM
│   │   │   ├── HistoryPageViewModel.cs   ← 历史记录、清除
│   │   │   ├── SettingsPageViewModel.cs  ← 设置表单绑定
│   │   │   └── IEditablePageViewModel.cs ← 可编辑页面契约（IsDirty + SaveAsync）
│   │   └── Services/
│   │       ├── AppDataService.cs         ← 应用共享状态所有者
│   │       ├── SettingsService.cs        ← 设置读写 + 变更通知
│   │       ├── AnimationService.cs       ← WinUI Composition 入场/揭示动画
│   │       ├── ThemeService.cs           ← 主题/背景效果切换
│   │       ├── TrayIconService.cs        ← 系统托盘图标管理
│   │       ├── TrayMenuWindowService.cs  ← 托盘菜单弹出/隐藏
│   │       ├── QuickDrawWindowService.cs ← 快捷点名按钮窗口管理
│   │       ├── NotificationService.cs    ← Windows App SDK 原生通知
│   │       ├── ClassIslandNotificationBridge.cs ← ClassIsland 命名管道客户端
│   │       ├── DialogService.cs          ← ContentDialog 封装
│   │       ├── FileDialogService.cs      ← FileOpenPicker/FileSavePicker 封装
│   │       └── StartupService.cs         ← 注册表开机自启管理
│   │
│   ├── PickWeight.Tray/                  ← 系统托盘宿主
│   │   └── TrayHost.cs                   ← WinForms NotifyIcon 封装
│   │
│   └── PickWeight.ClassIsland.Protocol/  ← ClassIsland 通信协议库
│       └── ClassIslandProtocol.cs        ← 命名管道消息格式、读写方法、客户端
│
├── PickWeight ClassIsland/               ← ClassIsland 插件项目
│   ├── Plugin.cs                         ← 插件入口
│   ├── PickWeightNotificationProvider.cs ← 提醒提供方实现
│   ├── PickWeightNotificationSettings.cs ← 提醒设置模型
│   ├── PickWeightNotificationSettingsControl.axaml / .cs ← 设置 UI
│   ├── PickWeightPipeListenerService.cs  ← 命名管道服务端监听
│   ├── NotificationProviderAccessor.cs   ← 依赖注入桥接
│   └── README.md
│
├── tests/
│   ├── PickWeight.Core.Tests/
│   │   ├── DrawServiceTests.cs           ← 抽取算法测试
│   │   ├── MemberRulesTests.cs           ← 成员规则测试
│   │   ├── ImportExportTests.cs          ← 导入导出测试
│   │   └── PersistenceTests.cs           ← 持久化测试
│   └── PickWeight.ClassIsland.Protocol.Tests/
│       └── ClassIslandProtocolTests.cs   ← 协议序列化/通信测试
│
├── dist/                                 ← 发布产物目录
├── scripts/                              ← 构建脚本
└── MEMORY.md                             ← 记忆索引
```

## 核心概念

### 抽取算法

```
最终概率 = 配置权重归一化概率 × 70% + 程序综合权重归一化概率 × 30%

程序综合权重 = 显式权重 × 被点次数衰减系数 × 已触发小组倍率

被点次数衰减系数 = 1 / (1 + 被点次数 × 0.1)
```

每一轮抽取都是一步不放回选择：选中一人后立即移出候选池，并重新计算剩余候选人的临时权重。

**四种模式**：

| 模式 | 概率计算方式 | 是否计入被点次数 | 使用场景 |
|---|---|---|---|
| 加权 `Weighted` | 70%配置 + 30%程序 | ✅ 自动计入 | 日常课堂点名 |
| 智能随机 `SmartRandom` | 仅被点次数衰减 | ✅ 自动计入 | 均衡被点频率 |
| 完全随机 `FullRandom` | 仅配置隐式权重 | ❌ 不计入 | 纯随机抽选 |
| 选择 `Selection` | 70%配置 + 30%程序 | ⏸️ 手动选择 | 需要人工确认的场景 |

### 数据流

```
                    ┌─────────────┐
                    │  state.json │  ← 持久化存储
                    └──────┬──────┘
                           │
              ┌────────────▼────────────┐
              │     JsonStateStore       │
              │  LoadAsync / SaveAsync   │
              └────────────┬────────────┘
                           │
              ┌────────────▼────────────┐
              │     AppDataService       │  ← 唯一状态持有者
              │  State: AppState         │
              └──┬──────────┬───────────┘
                 │          │
    ┌────────────▼──┐  ┌───▼────────────┐
    │   ViewModels   │  │   Services     │
    │  操作草稿数据   │  │  执行副作用    │
    └───────┬────────┘  └───┬────────────┘
            │               │
    ┌───────▼────────┐  ┌───▼────────────┐
    │   Pages (.xaml) │  │ 外部系统       │
    │   数据绑定展示   │  │ (通知/托盘等)  │
    └────────────────┘  └────────────────┘
```

### 脏状态与保存

"权重编辑器"和"小组绑定器"是可编辑页面，使用 `IEditablePageViewModel` 契约：

1. 页面加载时 ViewModel 基于 `AppDataService.State` 创建草稿副本
2. 用户在草稿上编辑，`IsDirty` 自动标记为 `true`
3. 离开页面时弹出 `DialogService.ConfirmUnsavedAsync()` 对话框
4. 保存时 ViewModel 验证并发数据 → 调用 `AppDataService.ReplaceMembersAsync()` / `ReplaceGroupsAsync()`
5. 成员重命名时，`ReplaceMembersAsync` 会同步更新所有小组中的成员姓名

### 抽取会话回滚

`DrawPageViewModel` 内部用私有类 `DrawSession` 管理单次抽取的生命周期：

- **开始时**：`CaptureSnapshots()` 快照所有成员的权重和次数
- **提交时**：调用 `MemberRules.IncrementCalledCount()` 并持久化
- **丢弃单条**：从结果列表移除，调用 `RebuildCurrentSessionState()` 从快照恢复并重新应用已提交的条目
- **丢弃全部**：清空结果列表，移除历史记录，从快照完整恢复

## 开发环境搭建

### 前置条件

- Windows 10 1809 或更高版本
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022（推荐）或 VS Code
- WinUI 3 / Windows App SDK 开发工作负载

### 构建命令

```powershell
# 运行核心库测试
dotnet test tests/PickWeight.Core.Tests/PickWeight.Core.Tests.csproj

# 运行协议测试
dotnet test tests/PickWeight.ClassIsland.Protocol.Tests/PickWeight.ClassIsland.Protocol.Tests.csproj

# 构建主应用 (Debug)
dotnet build src/PickWeight.App/PickWeight.App.csproj -p:Platform=x64

# 发布 (Release, 自包含)
dotnet publish src/PickWeight.App/PickWeight.App.csproj -p:Platform=x64 -p:PublishProfile=win-x64

# 生成全部分发包
.\scripts\package-release.ps1
```

### 调试

```powershell
# 直接运行（Debug 模式）
dotnet run --project src/PickWeight.App/PickWeight.App.csproj -p:Platform=x64
```

## 编码规范

### 命名约定

| 类别 | 规范 | 示例 |
|---|---|---|
| 类/记录 | PascalCase | `DrawService`, `AppState` |
| 接口 | `I` 前缀 | `IDrawService`, `IStateStore` |
| 方法 | PascalCase | `CalculateProbabilities()` |
| 私有字段 | `_camelCase` | `_stateStore`, `_nextUnitDouble` |
| 属性 | PascalCase | `IsDirty`, `StatusMessage` |
| 局部变量 | camelCase | `candidates`, `winner` |
| 常量 | PascalCase | `MaximumWeight`, `DefaultExplicitWeight` |

### MVVM 模式约定

ViewModel 使用 CommunityToolkit.Mvvm 的源生成器：

```csharp
// 可观察属性
[ObservableProperty]
private string statusMessage = "准备开始抽取";

// 命令
[RelayCommand]
private async Task DrawAsync() { ... }

// 带 CanExecute 的命令
[RelayCommand(CanExecute = nameof(CanDiscardCurrentDraw))]
private async Task DiscardCurrentDrawAsync() { ... }
```

### DI 注册

所有服务在 `App.xaml.cs` 的构造函数中注册：

```csharp
services.AddSingleton<IStateStore>(new JsonStateStore(...));
services.AddSingleton<IDrawService, DrawService>();
services.AddTransient<DrawPageViewModel>();
```

### 文件头注释

公共类型应使用 `<summary>` XML 文档注释。内部私有类型可以不写。

### 数据校验

使用 `MemberRules.Validate()` 和 `MemberRules.ValidateWeight()` 进行数据校验。导入时使用 `Math.Clamp()` 限制数值范围。

## 新增页面指南

### 1. 创建 XAML 页面

在 `src/PickWeight.App/Pages/` 下创建 `NewPage.xaml`：

```xml
<Page
    x:Class="PickWeight_App.Pages.NewPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid x:Name="PageRoot" Padding="28">
        <!-- 页面内容 -->
    </Grid>
</Page>
```

### 2. 创建 Code-Behind

```csharp
namespace PickWeight_App.Pages;

public sealed partial class NewPage : Page
{
    public NewPage()
    {
        InitializeComponent();
        DataContext = App.GetService<NewPageViewModel>();
        Loaded += (_, _) => PlayPageEntrance();
    }

    private void PlayPageEntrance()
    {
        var animations = App.GetService<AnimationService>();
        // 播放页面入场动画
    }
}
```

### 3. 创建 ViewModel

```csharp
namespace PickWeight_App.ViewModels;

public partial class NewPageViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusMessage = string.Empty;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));
}
```

如果页面可以编辑数据，实现 `IEditablePageViewModel`。

### 4. 注册导航

在 `App.xaml.cs` 注册 ViewModel：
```csharp
services.AddTransient<NewPageViewModel>();
```

在 `MainPage.xaml` 添加导航项：
```xml
<NavigationViewItem Content="新页面" Icon="..." Tag="new" />
```

在 `MainPage.xaml.cs:94-100` 添加路由：
```csharp
"new" => typeof(NewPage),
```

### 5. 注册 DI

在 `App.xaml.cs` 的构造函数中注册 ViewModel 和所需服务。

## 新增服务指南

### 1. 定义接口（在 Core 层）

```csharp
// src/PickWeight.Core/Abstractions/INewService.cs
namespace PickWeight.Core.Abstractions;

public interface INewService
{
    Task<SomeResult> DoSomethingAsync();
}
```

### 2. 实现服务（在 Core 或 App 层）

```csharp
// src/PickWeight.App/Services/NewService.cs
namespace PickWeight_App.Services;

public sealed class NewService : INewService
{
    public async Task<SomeResult> DoSomethingAsync()
    {
        // 实现逻辑
    }
}
```

### 3. 注册到 DI

在 `App.xaml.cs:59-70` 区域添加：
```csharp
services.AddSingleton<INewService, NewService>();
```

### 4. 测试

在 `tests/PickWeight.Core.Tests/` 下创建测试类。

## 功能改进路线图

### 🟢 第一阶段：快速收益（预计 1-2 周）

| 序号 | 功能/改进 | 涉及文件 | 要点 |
|---|---|---|---|
| 1 | 抽取历史导出 CSV | `HistoryPageViewModel.cs` | 新增 `ExportCsvCommand`，使用 `FileDialogService.SaveTextAsync` |
| 2 | 批量权重操作 | `MembersPageViewModel.cs` | 扩展多选操作，增加"统一设置权重"对话框 |
| 3 | 成员数量实时显示 | `DrawPage.xaml` | 在抽取面板上方添加 `TextBlock` 显示候选人数、活跃人数、小组数 |
| 4 | 窗口位置记忆 | `AppSettings.cs`, `MainWindow.xaml.cs` | 在 `AppSettings` 中添加 `WindowX/Y/Width/Height`，关闭时保存 |
| 5 | 魔法数字常量化 | `DrawService.cs`, `MemberRules.cs` | 提取 `ConfigWeightRatio`、`ProgramWeightRatio`、`CalledCountDecay`、`MaxGroupMultiplier` |
| 6 | 日志基础设施 | 全局 | 引入 `Microsoft.Extensions.Logging`，在关键路径添加 `ILogger<T>` |

### 🟡 第二阶段：功能增强（预计 2-4 周）

| 序号 | 功能/改进 | 涉及文件 | 要点 |
|---|---|---|---|
| 7 | 抽取前概率预览 | `DrawService.cs`, `DrawPageViewModel.cs` | 在 `DrawService` 中提取 `PreviewProbabilities()` 方法 |
| 8 | 可保存的抽取预设 | 新增 `DrawPreset` 模型, `DrawPageViewModel.cs` | JSON 持久化预设，提供下拉或 Chip 选择 |
| 9 | 全局快捷键 | 新增 `HotkeyService.cs` | 使用 `RegisterHotKey` P/Invoke，在主窗口不可见时仍可触发 |
| 10 | 成员统计仪表盘 | 新增 `StatisticsPage` | 计算被点次数分布、频率趋势 |
| 11 | 条件过滤抽取 | `DrawOptions.cs`, `DrawService.cs` | 新增 `MemberTags` 过滤、`MaxCalledCount` 过滤 |
| 12 | P/Invoke 代码去重 | `QuickDrawWindow.xaml.cs`, `TrayMenuWindow.xaml.cs` | 提取到 `NativeWindowHelper` 静态类 |

### 🔵 第三阶段：生态与体验（预计 4-8 周）

| 序号 | 功能/改进 | 涉及文件 | 要点 |
|---|---|---|---|
| 13 | 编辑器撤销/重做 | `MembersPageViewModel.cs`, `GroupsPageViewModel.cs` | 实现 Command 栈 + `Ctrl+Z` / `Ctrl+Y` |
| 14 | REST API | 新增 `ApiHostService` | 嵌入 Kestrel，暴露 `/draw`、`/members` 等端点 |
| 15 | OBS 叠加层 | 新增 `ObsOverlayService` | 将最近结果写入固定路径文本文件 |
| 16 | Excel 导入 | `MemberImportExportService.cs` | 引入 `ClosedXML` 或 `MiniExcel` |
| 17 | 抽取动画增强 | `AnimationService.cs`, `DrawPage.xaml.cs` | 实现"名字滚动"效果 |
| 18 | 国际化 | 所有 .xaml/.cs 文件 | 引入 `.resw` 资源，使用 `x:Uid` 标记 |

## 常见开发任务

### 添加新的抽取模式

1. 在 `DrawMode.cs` 的 `DrawMode` 枚举中添加新值
2. 在 `DrawService.CalculateProbabilities()` 中添加对应的分支
3. 在 `DrawService.IsEligible()` 中添加资格检查
4. 在 `DrawService.ShouldUpdateCalledCount()` 中添加计数更新策略
5. 在 `DrawPage.xaml` 和 `SettingsPage.xaml` 的 ComboBox 中添加选项
6. 在 `DrawPageViewModel.DrawAsync()` 中处理新模式的前置逻辑
7. 在 `DrawResultItemViewModel.WeightText` 中添加新模式的权重展示文本
8. 添加单元测试

### 添加新的设置项

1. 在 `AppSettings.cs` 中添加属性
2. 在 `AppSettings.Clone()` 中添加复制逻辑
3. 在 `SettingsChangedEventArgs.cs` 中添加变更检测
4. 在 `SettingsPageViewModel.cs` 中添加绑定属性
5. 在 `SettingsPage.xaml` 中添加 UI 控件
6. 在服务中订阅 `ISettingsService.Changed` 事件响应该设置变更

### 为现有页面添加按钮/操作

1. 在页面对应 ViewModel 中添加 `[RelayCommand]` 方法
2. 在 XAML 中添加绑定 `Command="{Binding NewCommand}"` 的按钮
3. 如需确认对话框，注入 `DialogService`
4. 如需文件操作，注入 `FileDialogService`
5. 数据变更后调用 `await _data.SaveAsync()` 持久化
6. 添加相应测试

### 调试 ViewModel

ViewModel 不直接操作 UI 元素。通过以下方式调试：

```csharp
// 在 ViewModel 中
System.Diagnostics.Debug.WriteLine($"调试信息: {value}");

// 设置 StatusMessage 在 UI 上显示
StatusMessage = $"调试: {someValue}";
```

---

*最后更新：2026-06-27*
