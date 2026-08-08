# PickWeight 课堂增强开发 Handoff

> 日期：2026-08-08
> 范围：课堂增强计划 P1–P6 全部完成，源码已构建、测试通过。
> 测试状态：`PickWeight.App.Tests` **65/65** 通过，`PickWeight.Core.Tests` **90/90** 通过。

---

## 1. 本轮完成总览

| 阶段 | 主题 | 状态 |
|------|------|------|
| P1 | 结果按钮收敛与权重微调 | 完成 |
| P2 | 删除点名页迷你座位图 | 完成 |
| P3 | 精简成员列表项 | 完成 |
| P4 | 小组添加成员弹窗增加可勾选列表 | 完成 |
| P5 | 左侧导航折叠常驻与返回按钮 | 完成 |
| P6 | 设置页二级菜单改为二级页面 | 完成 |

---

## 2. 各问题/功能详细改动

### 2.1 P1：结果按钮收敛与权重微调

**需求**
被点成员后的操作按钮过多，仅保留「换一个」「丢弃」「良好」「一般」，并相应调整权重计算。

**关键改动**
- `src/PickWeight.Core/Models/AppSettings.cs`
  - 移除 `RatingVolunteeredMultiplier`，新增 `RatingFairMultiplier = 0.9d`。
- `src/PickWeight.Core/Services/MemberRules.cs`
  - `RatingMultiplier`：良好 → 0.7，一般 → 0.9，未答上/主动 → 1.0。
  - `ApplyRatingAdjustment` 仅对良好、一般生效。
- `src/PickWeight.App/Services/DrawExecutionService.cs`
  - `RateAsync` 返回 `Task<bool>`，只有实际写入记录时才返回 `true`。
- `src/PickWeight.App/ViewModels/DrawResultItemViewModel.cs`
  - 移除「未答上」「主动」按钮与命令，仅保留 `RateGoodAsync`（良好）与 `RateFairAsync`（一般）。
  - 评价结果文本映射：`Excellent` → "已评价：良好"，`Fair` → "已评价：一般"。
- `src/PickWeight.App/ViewModels/DrawPageViewModel.cs`
  - 修复关键生命周期 bug：自动提交后不再清空 `_currentDraw`，使结果卡片按钮在加权/智能/完全随机模式下可用。
  - 移除 `ReplaceResultAsync` 中 `item.IsCommitted` 的替换限制。
  - `RateResultAsync` 在查找记录前调用 `prepared.HistoryEntry?.EnsureRecords()`，确保自动提交记录可被评价。
- `src/PickWeight.App/Pages/DrawPage.xaml`
  - 结果卡片按钮精简为 4 个：换一个、丢弃、良好、一般。
- `src/PickWeight.App/Pages/SettingsPage.xaml` / `ViewModels/SettingsPageViewModel.cs`
  - 评价设置仅保留「良好权重倍率」「一般权重倍率」。

**测试**
- `tests/PickWeight.App.Tests/DrawPageViewModelTests.cs`：新增评价行为与按钮可用性测试。
- `tests/PickWeight.Core.Tests/MemberRulesTests.cs`：新增权重倍率与调整测试。

---

### 2.2 P2：删除点名页迷你座位图

**需求**
点名结果区内的迷你座位图没有实质作用，予以删除。

**关键改动**
- `src/PickWeight.App/Pages/DrawPage.xaml`
  - 删除结果区 Grid 内的迷你座位图卡片。
  - 结果区简化为单列，ListView 占满整行。
- `src/PickWeight.App/ViewModels/DrawPageViewModel.cs`
  - 删除 `SeatMapCells`、`RebuildSeatMapCells`、`HasSeatMap`、`SeatMapDrawnCountText`、`SeatMapCanvasWidth`、`SeatMapCanvasHeight`、`SeatMapCellViewModel` 及 `Results.CollectionChanged` 订阅。
  - `HasSeatMapVisibility` 改为基于 `ActiveClassroom.SeatMap` 是否存在有效列。
- `tests/PickWeight.App.Tests/DrawPageViewModelTests.cs`
  - 删除两个迷你座位图相关测试用例。

**保留项**
- 「查看所在座位」按钮及其 `HasSeatMapVisibility` 绑定。
- `SeatMapPage` 与座位区域筛选逻辑。
- 结果行中的 `seatPosition` 文本。

---

### 2.3 P3：精简成员列表项

**需求**
成员列表保持简约，仅保留编辑按钮、姓名、被点热度进度条、启用开关。

**关键改动**
- `src/PickWeight.App/Pages/MembersPage.xaml`
  - `DataTemplate` 精简为 2 行 × 3 列布局：左侧编辑按钮（RowSpan=2）、中间姓名与进度条、右侧启用开关（RowSpan=2）。
  - 删除学号、性别图标、被点次数/权重/金色徽标、座位文本、备注文本的可见元素。
  - 取消进度条固定宽度 130，避免进度条与开关拥挤。
  - 页面头部说明更新为与精简 UI 一致的文案。
- `src/PickWeight.App/ViewModels/MemberRowViewModel.cs`
  - 仅更新 XML 注释以匹配新 UI；所有 ViewModel 属性（`StudentId`、`Gender`、`CalledCount`、`Weight`、`SeatText`、`Note`、`HeatBarValue` 等）保留，供编辑对话框与排序逻辑继续使用。

---

### 2.4 P4：小组添加成员弹窗增加可勾选列表

**需求**
小组页面添加小组成员时，不要只能搜索，需增加可勾选的成员列表。

**关键改动**
- `src/PickWeight.App/ViewModels/GroupsPageViewModel.cs`
  - `RefreshMemberSearch` 移除空查询门控，空查询直接显示「全部成员排除当前组成员」的完整名单。
  - 新增 `SelectedAvailableMembers`（忽略大小写的 `HashSet<string>`）保存勾选状态。
  - 新增 `AvailableMembersRefreshed` 事件，供页面在搜索过滤后恢复 ListView 勾选。
  - `AddMembers` 改为以 `SelectedAvailableMembers` 为入组来源，并支持显式传入名单。
- `src/PickWeight.App/Pages/GroupsPage.xaml.cs`
  - 弹窗中的 `ListView` 改为 `SelectionMode.Multiple` 并启用 `IsMultiSelectCheckBoxEnabled`。
  - 同步用户勾选到 ViewModel 集合，并在列表刷新后恢复勾选状态。
  - 提示文案改为「勾选要加入的成员，或输入关键字过滤。」
- `tests/PickWeight.App.Tests/GroupsPageViewModelTests.cs`
  - 新增 7 个测试：空查询完整名单、搜索过滤、搜索后保留勾选、AddMembers 以勾选集合为准、大小写忽略与跳过已入组、显式名单参数、刷新事件触发。

---

### 2.5 P5：左侧导航折叠常驻与返回按钮

**需求**
左侧菜单栏可折叠、可常驻，并在「展开」按钮下方加入「返回」按钮，点击返回点名页。

**关键改动**
- `src/PickWeight.App/MainPage.xaml`
  - `NavigationView` 的 `PaneDisplayMode` 从 `LeftMinimal` 改为 `Left`。
  - 默认展开宽度 280px，折叠后显示 56px 图标栏并推动内容区。
- `src/PickWeight.App/MainPage.xaml.cs`
  - 新增 `IsNavigationPaneOpen` 持久化：`PaneOpened`/`PaneClosed` 时写入 `AppSettings`。
  - 启动时恢复导航栏展开/折叠状态。
  - 「返回」`NavigationViewItem` 在代码后台 `Insert` 到 `MenuItems` 首位，图标为 `Back`，点击后调用 `ConfirmPageExitAsync` 并导航到点名页。
  - 新增 `DrawMenuItem()` 按 `Tag="draw"` 定位菜单项，避免被返回项顶掉。
- `src/PickWeight.App/MainWindow.xaml.cs`
  - 最小窗口宽度从 640 调整为 800。
- `src/PickWeight.App/Pages/MembersPage.xaml.cs`
  - 「前往座位表」改为通过 `MainWindow.NavigateToSeatMapAsync` 跳转，同步导航选中态并触发未保存确认。
- `src/PickWeight.Core/Models/AppSettings.cs`
  - 新增 `IsNavigationPaneOpen = true`，并加入 `Clone()` 与 `CreateDefaultPreservingRuntime()`。

---

### 2.6 P6：设置页弹窗改为二级页面

**需求**
设置页的 6 个弹出式二级设置不要弹窗，改为二级页面。

**关键改动**
- `src/PickWeight.App/Pages/SettingsPage.xaml`
  - 页面 Grid 内嵌 `Frame`（`x:Name="SettingsFrame"`），与 6 张分类卡片同层。
  - 删除 6 个 `ContentDialog`。
  - 卡片列表 `ScrollViewer` 命名 `SettingsListScrollViewer`。
- `src/PickWeight.App/Pages/SettingsPage.xaml.cs`
  - 6 个分类卡片点击改为 `OpenSettingsPage(typeof(XxxSettingsPage))`。
  - 导航前清空 `BackStack`/`ForwardStack`，并把当前 `SettingsPageViewModel` 实例作为参数传入，确保子页共享同一 VM。
  - 新增 `ReturnToCardList()` 供子页返回时切回卡片列表。
  - 首次运行引导、查看令牌、打开数据文件夹、全局快捷键录制、恢复默认、预览动画等逻辑分别迁入对应子页。
- 新建 `src/PickWeight.App/Pages/Settings/` 目录及 6 个子页：
  - `SystemSettingsPage`（系统设置）
  - `DrawSettingsPage`（点名设置）
  - `RatingSettingsPage`（评价与权重）
  - `HistoryArchiveSettingsPage`（历史与归档）
  - `NotificationSettingsPage`（通知）
  - `AppearanceSettingsPage`（外观和个性化）
- `src/PickWeight.App/Pages/Settings/SettingsSubPageNavigation.cs`
  - 共享静态帮助类：`ApplyViewModel` 把导航参数设为子页 `DataContext`；`GoBack` 优先 `Frame.GoBack`，返回栈为空时找到宿主 `SettingsPage` 调 `ReturnToCardList`。
- `src/PickWeight.Core/Models/SettingsChangedEventArgs.cs`
  - `HasChanges` 加入 `FairnessReminderDaysChanged`，修复仅修改「长期未点提醒天数」时不触发刷新提醒的 bug。

---

### 2.7 P8：Haiku 代码检查与问题修复

P8 使用 Haiku 模型对 P1–P6 全量改动进行代码错误检查，发现 2 个 warning 与若干 info 级清理项，已修复并重新通过测试。

**已修复的问题**

1. **设置二级页返回栈缺陷**
   - 位置：`src/PickWeight.App/Pages/SettingsPage.xaml.cs`
   - 问题：`OpenSettingsPage` 先清空 `BackStack` 再 `Frame.Navigate`，但 `Navigate` 会把当前子页自动压入返回栈；连续打开两个分类后，子页「返回」按钮会回到上一个子页而非卡片列表。
   - 修复：导航前先将 `SettingsFrame.Content = null`，确保 `BackStack` 始终为空，`SettingsSubPageNavigation.GoBack` 会正确回到卡片列表。

2. **`DrawPageViewModel` 单例事件订阅泄漏**
   - 位置：`src/PickWeight.App/ViewModels/DrawPageViewModel.cs`
   - 问题：`DrawPageViewModel` 注册为 Transient，每次导航到点名页都会新建实例；构造时订阅了 `AppDataService` 与 `ISettingsService` 的单例事件，但 `OnNavigatedFromAsync` 从不退订，导致旧实例无法被回收（P5 的「返回」按钮会放大该问题）。
   - 修复：把 4 个事件处理 lambda 保存为字段，在 `OnNavigatedFromAsync` 的 `finally` 块中统一取消订阅。

3. **`DrawRulesChanged` 字段不完整**
   - 位置：`src/PickWeight.Core/Models/SettingsChangedEventArgs.cs`
   - 修复：`DrawRulesChanged` 中补上 `DrawNoRepeatInRound`、`DrawSeatRegion`、`DrawSeatColumn`、`DrawSeatRow` 的比较，确保这些规则变更能正确触发 `Changed` 事件。

4. **代码清理**
   - 删除 `src/PickWeight.App/MainPage.xaml.cs` 中未使用的 `using PickWeight.Core.Models;`。
   - 删除 `src/PickWeight.App/ViewModels/DrawPageViewModel.cs` 中 `CommitResultAsync` try 块内未使用的 `committedBefore` 局部变量。

**最终检查结论**
- 构建：0 错误（仅存在既有 MVVMTK0045 AOT 兼容性警告）。
- 测试：`PickWeight.Core.Tests` 90/90 通过，`PickWeight.App.Tests` 65/65 通过。

---

## 3. 关键设计决策与注意事项

1. **结果卡片生命周期**
   自动提交后保留 `_currentDraw`，但释放跨条目 lease。这是 P1 修复按钮失效的核心改动，后续调整提交/替换/评价逻辑时需保持该生命周期。

2. **导航返回项的实现方式**
   直接在 XAML 的 `MenuItems` 中加入第 6 个 `NavigationViewItem` 会触发本机 `XamlCompiler.exe` 崩溃（退出码 1）。因此「返回」项改在代码后台 `Insert(0, ...)` 动态添加，既满足菜单位置又绕过编译器缺陷。

3. **设置页 ViewModel 共享**
   子页通过 `SettingsPageViewModel` 实例作为导航参数传递，避免 DI `Transient` 导致每页拿到不同实例。子页 `Unloaded` 时取消动画预览等事件订阅，防止内存泄漏。

4. **小组勾选状态持久化**
   搜索过滤会重填 `FilteredAvailableMembers` 并导致 `ListView` 程序性清空勾选。通过 `SelectedAvailableMembers` 集合与 `AvailableMembersRefreshed` 事件，用户勾选在搜索/清空过程中不丢失。

5. **导航状态持久化**
   `IsNavigationPaneOpen` 保存用户偏好，启动时恢复。新增属性已加入 `Clone()` 与 `CreateDefaultPreservingRuntime()`，避免运行时状态被重置。

6. **AOT 兼容性警告**
   构建时存在大量 `MVVMTK0045` 警告，来自 `[ObservableProperty]` 字段。当前项目未启用 AOT，若未来启用 Native AOT，需要将这些字段改为 partial 属性。

---

## 4. 构建、测试与打包

```powershell
# 关闭构建服务器并单节点构建（避免本机 XamlCompiler 并发/文件锁问题）
dotnet build-server shutdown
dotnet build src/PickWeight.App/PickWeight.App.csproj `
  -c Debug -p:RuntimeIdentifier=win-x64 `
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false

# 单元测试（App 项目必须指定 RID；使用 --no-build 复用上面产物）
dotnet test tests/PickWeight.App.Tests/PickWeight.App.Tests.csproj `
  -p:RuntimeIdentifier=win-x64 --no-build
dotnet test tests/PickWeight.Core.Tests/PickWeight.Core.Tests.csproj --no-build

# Release 打包（未签名）
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Configuration Release

# 启动
Start-Process dist/PickWeight-win-x64/PickWeight.exe -WorkingDirectory dist/PickWeight-win-x64
```

---

## 5. 已知限制与后续建议

- **XamlCompiler 环境敏感性**：本机 WinAppSDK 1.8 的 `XamlCompiler.exe` 在 MSBuild 并发调用下可能崩溃。单节点、禁用共享编译、先 `build-server shutdown` 可稳定复现通过。
- **设置子页状态栏**：卡片列表顶部的状态栏（`StatusSection`）在子页打开时被 `Frame` 覆盖。子页与列表共享同一 VM，状态本身保持一致；如需在子页也显示自动保存状态，可在子页补 `InfoBar`。
- **UI/交互测试覆盖**：导航返回、座位图高亮、设置二级页面等目前主要靠单元测试与手工验证。后续可考虑 WinUI UI 测试或截图回归。
- **Release 包未签名**：`scripts/package-release.ps1` 默认不带证书参数，生成的 `dist/PickWeight-win-x64` 与 `PickWeight-win-x64.zip` 未签名，不可作为正式签名发布包。

---

## 6. 相关文件速查

| 文件 | 作用 |
|------|------|
| `src/PickWeight.App/Pages/DrawPage.xaml` | 点名页 UI、结果卡片 |
| `src/PickWeight.App/ViewModels/DrawPageViewModel.cs` | 点名页状态、抽取生命周期 |
| `src/PickWeight.App/ViewModels/DrawResultItemViewModel.cs` | 结果项 VM、按钮命令 |
| `src/PickWeight.App/Services/DrawExecutionService.cs` | 抽取、替换、提交、评价 |
| `src/PickWeight.Core/Services/MemberRules.cs` | 权重倍率与评价调整 |
| `src/PickWeight.App/Pages/MembersPage.xaml` | 成员列表精简 UI |
| `src/PickWeight.App/Pages/GroupsPage.xaml.cs` | 小组添加成员弹窗 |
| `src/PickWeight.App/ViewModels/GroupsPageViewModel.cs` | 小组页 VM、可勾选成员逻辑 |
| `src/PickWeight.App/MainPage.xaml` / `.xaml.cs` | 主导航壳、返回按钮、折叠持久化 |
| `src/PickWeight.App/MainWindow.xaml.cs` | 窗口外壳、跨页面导航入口 |
| `src/PickWeight.App/Pages/SettingsPage.xaml` / `.xaml.cs` | 设置页卡片列表与二级页导航 |
| `src/PickWeight.App/Pages/Settings/*.xaml` / `.xaml.cs` | 6 个设置二级页面 |
| `src/PickWeight.App/Pages/Settings/SettingsSubPageNavigation.cs` | 子页导航与 VM 共享辅助 |
| `src/PickWeight.Core/Models/AppSettings.cs` | 应用设置模型 |
| `src/PickWeight.Core/Models/SettingsChangedEventArgs.cs` | 设置变更事件 |
| `scripts/package-release.ps1` | Release 打包脚本 |

---

*本文件用于向后续维护者交接上下文；具体代码以当前仓库为准。*
