# PickWeight 课堂增强开发计划

> 状态：**开发中**。P0-P3、P5 已完成；P4 进行中。
> 编号沿用讨论时的 1–18，方便对照。

## 一、范围确认

| 编号 | 功能 | 决定 |
|---|---|---|
| 1 | 一轮不重复轮转模式 + 本轮进度 | ✅ 做 |
| 2 | 长期未被点主动提醒 + 一键提权 | ✅ 做 |
| 3 | 「换一个」一键换人（含次数回滚） | ✅ 做 |
| 4 | 今日请假（临时在场状态，次日自动恢复） | ✅ 做 |
| 5 | 课堂表现记录（评价 + 参与度报告） | ✅ 做 |
| — | 多班级（Classroom） | ✅ 做 |
| 6 | 投影模式支持多人 | ✅ 做 |
| 7 | 投影模式不再失焦即关闭 | ✅ 做 |
| 8 | 大屏呈现：全屏自适应字号 / 投影配色 / 减速停止 / 语音报名 / 倒计时 | ✅ 做（**音效取消**） |
| 9 | 主界面前置模式选择器 | ❌ 不做（保留现有对话框入口） |
| 10 | 键盘 / 翻页笔操作 | ✅ 做 |
| 11 | 成员列表行内显示次数权重 + 排序 | ✅ 做 |
| 12 | 成员字段扩展（学号 / 座位 / 备注） | ✅ 做 |
| 13 | 统计页可视化 + 公平性指数 | ✅ 做 |
| 14 | 一键随机分组 | ❌ 不做（保留） |
| 15 | 座位表模式 + **座位表编辑器页面**（列式布局） | ✅ 做 |
| 16 | ClassIsland 联动（课时上下文 / 下课收尾 / 反向触发） | ✅ 做（**自动切班取消**） |
| 17 | 粘贴导入 | ✅ 做 |
| 18 | 自动备份 | ❌ 不做 |
| 债 A | ListView 虚拟化 | ❌ 不动 |
| 债 B | 历史无上限 → 按学期归档 | ✅ 修 |
| 债 C | 全局鼠标钩子 → 移除 | ✅ 修 |
| 债 D | 快捷键限制 → 录制式捕获 | ✅ 修 |

## 二、数据模型变更总览

所有变更都要能读旧 `state.json`。迁移在 `JsonStateStore.LoadAsync` 内做一次性升级，并写 `SchemaVersion`。

### 新增 / 修改的模型

```csharp
// 新增：班级容器
public sealed class Classroom
{
    public string Name { get; set; } = "默认班级";   // 同时是文件夹名，唯一
    public string? Subject { get; set; }             // 科目，仅用于历史记录标注
    public List<Member> Members { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
    public List<DrawHistoryEntry> DrawHistory { get; set; } = [];
    public List<DrawPreset> DrawPresets { get; set; } = [];
    public SeatMap? SeatMap { get; set; }
    public RoundState Round { get; set; } = new();
}

// 新增：轮转状态（编号 1）
public sealed class RoundState
{
    public int RoundNumber { get; set; } = 1;
    public List<string> DrawnNames { get; set; } = [];
    public DateTimeOffset? StartedAt { get; set; }
}

// Member 扩展（编号 4 / 12 / 15）
public sealed class Member
{
    // 既有：Name, Gender, ExplicitWeight, ImplicitWeight,
    //       CalledCount, IsWeightLinked, IsActive
    public string? StudentId { get; set; }        // 学号，重名区分
    public string? Note { get; set; }             // 备注
    public DateOnly? AbsentOn { get; set; }       // 今日请假；日期不等于今天即失效
}

// DrawHistoryEntry 升级（编号 5 / 16）
public sealed class DrawHistoryEntry
{
    public DateTimeOffset DrawnAt { get; set; }
    public List<string> MemberNames { get; set; } = [];   // 保留，兼容旧数据与导出
    public List<DrawRecord> Records { get; set; } = [];   // 新增：逐人明细
    public string? Subject { get; set; }                  // 来自 ClassIsland
    public int? PeriodIndex { get; set; }                 // 第几节课
    public int? RoundNumber { get; set; }
}

public sealed class DrawRecord
{
    public string Name { get; set; } = string.Empty;
    public DrawRating Rating { get; set; } = DrawRating.None;
    public string? Note { get; set; }
    public bool WasReplaced { get; set; }   // 被「换一个」换掉的记录
}

public enum DrawRating { None, Excellent, Fair, NoAnswer, Volunteered }
```

### AppState 与存储布局（**按班级名文件夹**）

每个班一个文件夹，文件夹名 = 班级名，班级数据与该班的隐式权重配置放在一起：

```
%LOCALAPPDATA%\PickWeight\
  state.json                        // SchemaVersion + Settings + ActiveClassroomName
  classes\
    高一(3)班\
      classroom.json                // 名单 / 小组 / 历史 / 预设 / 座位表 / 轮次
      implicit_config.json          // 该班隐式权重（原发布包 Data 下的那份）
      archive\2026-上学期.json       // 归档历史（债 B）
    高一(5)班\
      classroom.json
      implicit_config.json
```

`AppState` 变为：`SchemaVersion`、`Settings`、`ActiveClassroomName`、`ClassroomNames`。

**文件夹名规则**：
- 班级名唯一（不区分大小写），重名在新建/重命名时直接拒绝并提示
- 非法文件名字符 `\ / : * ? " < > |` 禁止输入，输入框实时校验
- Windows 保留名（`CON`/`PRN`/`AUX`/`NUL`/`COM1`…）禁止
- 长度上限 50 字，两端空格与结尾句点自动裁掉
- **重命名班级 = 重命名文件夹**：先校验目标名不存在 → `Directory.Move` → 更新 `state.json`；任一步失败则整体回滚，不留半个状态
- 启动时扫描 `classes\` 下的文件夹作为班级列表；`state.json` 里记录的名字若已不存在（用户手工删了文件夹），回退到第一个可用班级并提示

**隐式权重配置的位置变更**：从发布包 `Data/implicit_config.json` 移到每班文件夹下。首次迁移时把原文件复制进「默认班级」文件夹。新建班级时生成一份空配置，由现有的名单同步逻辑自动填充（新成员 `1.0`）。设置页加「打开数据文件夹」按钮，方便继续手工编辑。

**迁移规则**（v1 → v2，只跑一次）：
1. 把 `state.json` 复制为 `state.json.v1.bak`
2. 顶层 `Members/Groups/DrawHistory/DrawPresets` 打包写入 `classes\默认班级\classroom.json`
3. 发布包 `Data/implicit_config.json` 复制为 `classes\默认班级\implicit_config.json`（原文件保留不删，便于回退）
4. 重写 `state.json`：只留 `SchemaVersion=2`、`Settings`、`ActiveClassroomName=默认班级`
5. 任一步失败 → 不写 `SchemaVersion`，应用以只读模式启动并提示用户，下次启动重试

## 三、分阶段开发计划

### P0 · 地基：数据分层与迁移

没有这一步，后面所有带班级上下文的功能都要返工。**建议第一个做完并单独验证。**

| # | 任务 | 涉及文件 |
|---|---|---|
| 0.1 | 新增 `Classroom` / `RoundState` / `DrawRecord` / `SeatMap` 模型 | `src/PickWeight.Core/Models/` |
| 0.2 | `AppState` 拆分为全局设置 + 班级名列表 | `Models/AppState.cs` |
| 0.3 | `JsonStateStore` 改为按班级文件夹读写、`SchemaVersion`、v1→v2 迁移 + 备份 | `Services/JsonStateStore.cs` |
| 0.4 | 新增 `ClassroomNameValidator`（非法字符 / 保留名 / 唯一性 / 长度） | `Core/Services/` |
| 0.5 | `AppDataService` 增加 `ActiveClassroom`、`SwitchClassroomAsync`、`CreateClassroomAsync`、`RenameClassroomAsync`（含文件夹 Move 回滚）、`ClassroomChanged` 事件 | `App/Services/AppDataService.cs` |
| 0.6 | 各 ViewModel 数据源从 `State.Members` 改为 `ActiveClassroom.Members` | 全部 ViewModel |
| 0.7 | `JsonImplicitWeightProvider` 路径改为按当前班级文件夹解析（切班时重新绑定） | `Services/JsonImplicitWeightProvider.cs` |
| 0.8 | 迁移单元测试：旧文件能读、内容不丢、二次启动不重复迁移、重命名回滚 | `tests/PickWeight.Core.Tests/PersistenceTests.cs` |

**验收**：装有旧数据的机器升级后，名单/小组/历史/预设全在「默认班级」下，抽取行为与升级前一致。

---

### P1 · 班级切换 UI + 课堂闭环（编号 1 / 3 / 4）

这一阶段做完，每节课的实际操作流程就完整了。

#### 1.1 班级切换（UI）

- `MainPage.xaml` 的 `NavigationView.PaneCustomContent` 放班级下拉，或标题栏右侧放 `DropDownButton`
- 下拉项：班级名 + 人数；底部固定「管理班级…」
- 管理班级对话框：新建 / 重命名 / 删除 / 设置科目 / 复制名单到新班
- 新建与重命名的输入框实时校验班级名（非法字符、保留名、重名、长度），不合法时禁用确定并显示原因
- 删除班级需输入班级名二次确认（会删掉整个文件夹，含历史和隐式权重配置），删除前自动导出一份 JSON 到桌面
- 切换时：保存当前班 → 加载目标班 → 重绑隐式权重配置路径 → 广播 `ClassroomChanged` → 各页刷新

#### 1.2 一轮不重复（编号 1）

- `AppSettings` 新增 `DrawNoRepeatInRound : bool`（默认 **开**）
- `DrawFilterOptions` 新增 `ExcludeRoundDrawnNames : IReadOnlySet<string>?`
- `DrawFilterService.Apply` 增加一条过滤分支（与现有 `ExcludeRecentDrawCount` 并列）
- 提交成功后把姓名写入 `Round.DrawnNames`（在 `DrawExecutionService.CommitAsync` 内，与历史写入同一处，保证回滚时一起撤销）
- 池空时自动 `RoundNumber++` 并清空 `DrawnNames`，状态栏提示「第 2 轮开始」
- **UI**：`DrawPage.xaml` 标题下方常驻进度条

```
本轮进度  ████████████░░░░░░░░░  12 / 45        [重开一轮]
```

- 「重开一轮」需二次确认
- 与「排除最近 N 次」并存时，两者取交集，规则摘要里写清楚

#### 1.3 换一个（编号 3）

- `DrawResultItemViewModel` 增加 `ReplaceCommand`
- 执行流程（复用现成的 `MemberSnapshot`）：
  1. 回滚该成员 `CalledCount`
  2. 从 `Round.DrawnNames` 移除
  3. 加入本次会话的临时排除集
  4. 用相同规则重抽 1 人，替换列表中该行
  5. 历史条目里原记录标记 `WasReplaced = true`（保留痕迹，便于复盘）
- 主页面结果卡片加第三个按钮；投影窗口也要有（见 P2）
- **边界**：候选池已空时按钮禁用并提示

#### 1.4 今日请假（编号 4）

- `Member.AbsentOn : DateOnly?`；判定 `AbsentOn == DateOnly.FromDateTime(DateTime.Today)` 才算请假
- 过期值不清理也不生效，次日自动恢复（无需定时任务）
- `DrawFilterService` 默认排除今日请假成员
- **UI**：`DrawPage` 加「今日考勤」可折叠面板

```
今日考勤   全班 45 · 在场 42 · 请假 3          [全部到齐]
┌────────────────────────────────────────┐
│ 张三  李四  王五  赵六  钱七  孙八 …      │  ← 点一下变灰=请假
│ ▨陈九  ▨周十                            │  ← 灰色=今日请假
└────────────────────────────────────────┘
```

- 成员管理页行内也显示请假标记，可从那里取消

**验收**：一节课内完成「标记 2 人请假 → 连抽 10 次无重复 → 中途换掉 1 人 → 进度显示 10/43」全流程。

---

### P2 · 投影与大屏（编号 6 / 7 / 8 + 债 C）

#### 2.1 多人投影（编号 6）

- `QuickDrawPresentation.FinalName : string` → `FinalNames : IReadOnlyList<string>`
- `QuickDrawWindowService` 允许传入抽取人数（不再写死 1 人）
- `QuickDrawResultWindow` 布局按人数切换：
  - 1 人：巨大居中
  - 2–4 人：横排
  - 5+ 人：网格 / 自动换行
- 逐个揭晓：每个名字间隔约 400 ms 定格

#### 2.2 关闭行为（编号 7 + 债 C）

- **移除 `WH_MOUSE_LL` 全局鼠标钩子**（`InstallOutsideClickHook` / `MouseHookCallback` / 相关 P/Invoke 全部删除）
- 投影模式：不再监听 `Deactivated`。关闭方式 = `Esc` / 关闭按钮 / 可选自动关闭倒计时
- 紧凑快捷模式：保留失焦关闭，但加 800 ms 宽限期，避免弹出瞬间被抢焦点误关
- `AppSettings` 新增 `PresentationAutoCloseSeconds : int`（0 = 不自动关）

#### 2.3 大屏呈现（编号 8）

| 子项 | 做法 |
|---|---|
| 全屏 + 自适应字号 | `FullScreenPresenter`；字号 = f(屏宽, 姓名长度, 人数)，取消 `MaxLines=1` 截断 |
| 投影配色 | `AppSettings.PresentationColorScheme`：跟随主题 / 高对比亮色 / 高对比暗色 |
| 减速停止 | `AnimationTimer` 间隔由 60 ms 按缓动曲线拉长到 400 ms，最后定格 |
| 语音报名 | `SpeechService`：`Windows.Media.SpeechSynthesis`，模板「请 {名字} 回答」，可关、可调语速 |
| 倒计时 | 揭晓后可选 30/60/90 s 大字倒计时，剩 10 s 变色，结束时数字闪烁提示（无声） |

新增设置分类「投影与大屏」，与现有四个分类卡片并列。

**验收**：接投影仪，PPT 放映中按热键 → 全屏结果不被误关 → 语音报名 → 倒计时 60 秒 → Esc 关闭回到 PPT。

---

### P3 · 座位表（编号 15）

**布局模型：以「列」为单位。** 教室按纵列排布，每列独立设置人数（行数），列与列之间可以并排贴紧、也可以插入过道空格。方位用**靠门 / 靠窗**描述，不用左右，避免视角歧义。

#### 3.1 数据模型

```csharp
public sealed class SeatMap
{
    /// <summary>从靠门侧到靠窗侧依次排列。</summary>
    public List<SeatColumn> Columns { get; set; } = [];

    /// <summary>靠门侧在渲染时位于哪一端，仅影响显示方向。</summary>
    public SeatSideOrientation DoorSide { get; set; } = SeatSideOrientation.Start;
}

public sealed class SeatColumn
{
    public int SeatCount { get; set; } = 6;              // 该列人数，各列可不同
    public bool HasAisleAfter { get; set; }              // 此列之后是否留过道空格
    public List<string?> MemberNames { get; set; } = []; // 下标 = 第几排，null = 空位
}

public enum SeatSideOrientation { Start, End }  // 靠门在左端 / 右端
```

设计要点：
- 没有全局 `Rows`。每列 `SeatCount` 独立，天然支持"第一列 6 人、第二列 5 人"这类真实教室
- 过道是**列的属性**（`HasAisleAfter`），不是独立实体。两列并排 = 前一列 `HasAisleAfter = false`；空一格 = `true`
- `MemberNames` 长度始终与 `SeatCount` 对齐，改人数时自动补 `null` 或截断（截断前若有人会先提示）
- 座位与成员按**姓名**关联，与 `Group` 一致；改名走 `AppDataService` 现有 renames 映射同步
- `DoorSide` 只影响渲染方向，不改变数据顺序——保存的永远是"靠门→靠窗"的逻辑顺序

#### 3.2 座位表编辑器页面（新页面）

导航栏新增「座位表」（`&#xE80A;`）。

```
┌─ 座位表 ──────────────────────────────────────────────────┐
│ [＋添加列]  靠门侧: (◉ 左  ○ 右)   [自动排入▾] [清空]      │
│                                            未分配 3 人 ⚠   │
├───────────────────────────────────────────────────────────┤
│                   ▔▔▔▔▔ 讲  台 ▔▔▔▔▔                      │
│                                                           │
│  靠门                                              靠窗    │
│  ┌─列1─┐┌─列2─┐      ┌─列3─┐┌─列4─┐      ┌─列5─┐          │
│  │╭───╮││╭───╮│      │╭───╮││╭───╮│      │╭───╮│          │
│  ││张三│││李四││      ││王五│││赵六││      ││钱七││          │
│  │╰───╯││╰───╯│      │╰───╯││╰───╯│      │╰───╯│          │
│  │╭───╮││╭───╮│ 过道 │╭───╮││╭───╮│ 过道 │╭───╮│          │
│  ││孙八│││周九││      ││吴十│││郑一││      ││ ＋ ││          │
│  │╰───╯││╰───╯│      │╰───╯││╰───╯│      │╰───╯│          │
│  │╭───╮││     │      │╭───╮││╭───╮│      │╭───╮│          │
│  ││王二│││     │      ││冯三│││陈四││      ││褚五││          │
│  │╰───╯││     │      │╰───╯││╰───╯│      │╰───╯│          │
│  │ 6人 ││ 5人 │      │ 6人 ││ 6人 │      │ 4人 │          │
│  │[⚙][✕]│[⚙][✕]│      │[⚙][✕]│[⚙][✕]│      │[⚙][✕]│         │
│  └─────┘└─────┘      └─────┘└─────┘      └─────┘          │
├───────────────────────────────────────────────────────────┤
│ 未分配：  周十一   吴十二   郑十三          [自动排入]      │
└───────────────────────────────────────────────────────────┘
```

**交互设计**：

| 操作 | 行为 |
|---|---|
| 点击座位（圆角矩形） | 弹出成员选择面板（带搜索框 + 未分配优先排在前面），选中即落座 |
| 点击已有人的座位 | 同上，可换人；面板底部有「清空此座」 |
| 点击 `＋` 空位 | 同上 |
| 列头 `⚙` | 设置该列人数、是否在其后留过道 |
| 列头 `✕` | 删除该列（有人时二次确认，被移除的人回到未分配区） |
| `＋添加列` | 在末尾追加一列，默认人数取当前最多的那一列 |
| 靠门侧切换 | 整体左右镜像渲染，数据不变 |
| 自动排入 | 按 姓名拼音 / 学号 / 名单顺序 / 随机 四种方式填满空位 |

- **不做拖拽**。点击→选择面板的方式在触摸屏（希沃白板）上比拖拽更可靠，也少一大块交互代码
- 座位矩形显示姓名，可选叠加学号角标；空位显示浅色 `＋`
- 未分配成员醒目提示但**不阻止保存**，允许部分排座
- 复用 `IEditablePageViewModel` 的草稿 + 保存模式，与成员/小组页一致
- 窄窗口横向滚动，不挤压列宽

#### 3.3 座位抽取模式

- 抽取规则对话框新增「按座位范围」：全部 / 指定列 / 指定排 / 靠门半区 / 靠窗半区 / 过道分块
- 结果展示新增**座位图高亮**：抽中的圆角矩形放大高亮 + 其余淡出，投影模式下尤其直观
- 未排座的成员在座位模式下自动排除，规则摘要里说明

**验收**：建 5 列（人数分别 6/5/6/6/4）+ 2 处过道 → 点击排入 27 人 → 保存 → 用「靠窗半区」抽取 → 投影显示座位高亮。

---

### P4 · 编辑与统计体验（编号 2 / 5 / 11 / 12 / 13 / 17 + 债 B / D）

#### 4.1 成员列表信息密度（编号 11 / 12）

`MembersPage` 行模板改为：

```
┌──────────────────────────────────────────────────────┐
│ ✎  12 张三   ♂                     被点 3 · 权重 100 │
│              3排5座 · 英语好        ███░░░░░  [启用]  │
└──────────────────────────────────────────────────────┘
```

- 行内显示：学号、姓名、性别、座位、备注（截断）、被点次数、权重、热力条
- 顶部排序下拉：姓名 / 学号 / 座位 / 被点次数 / 权重 / 最近被点
- 编辑对话框增加：学号、备注、座位（只读，跳转座位表）、今日请假开关
- **注意**：不改动列表虚拟化相关设置

#### 4.2 粘贴导入（编号 17）

- 成员页 CommandBar 增加「粘贴导入」
- 对话框：多行文本框 + 实时预览表格
- 分隔符自动识别：换行、空格、制表、逗号、顿号、分号
- 前缀自动剥离：`1.` `1、` `01` `(1)`
- 可识别 `学号 姓名` / `姓名 权重` 两种两列格式，预览里让用户确认列含义
- 复用现有 `MemberImportPreview` 管线，只是换了输入源

#### 4.3 公平性提醒（编号 2）

- `DrawPage` 顶部 `InfoBar`（可关闭）：
  `张三、李四 已 21 天未被点到　[本次优先] [忽略]`
- 「本次优先」= 仅本次抽取给这些成员临时倍率（默认 3×，可在设置调），不改存储权重
- 判定数据源：历史里该成员最后一次出现的时间；阈值用现有 `FairnessReminderDays`

#### 4.4 课堂表现记录（编号 5）

- 结果卡片增加四个快捷评价按钮：👍 优秀 / 👌 一般 / 👎 未答上 / 🙋 主动
- 写入 `DrawRecord.Rating`；可补充文字备注
- `AppSettings.RatingAffectsWeight`（默认 **关**）：开启后**答得好的降权**——让已经充分表现过的学生把机会让给别人。规则在设置页明写：

  | 评价 | 权重变化 |
  |---|---|
  | 👍 优秀 | × 0.7 |
  | 👌 一般 | 不变 |
  | 👎 未答上 | 不变（不惩罚答不上来的学生） |
  | 🙋 主动 | × 0.8 |

  倍率可在设置里调整；权重有下限保护（不低于默认权重的 10%，避免被彻底边缘化）。关闭该开关后已产生的权重变化保留，可用成员页「重置成员数据」还原。
- 统计页新增「课堂参与度」区块：每人 被点次数 / 各评价占比 / 主动率
- 导出 CSV 增加评价列

#### 4.5 统计可视化（编号 13）

- 30 天趋势：`ProgressBar` 列表 → 柱状图（自绘 `Canvas` 或 `Microsoft.UI.Xaml.Shapes`，不引第三方库）
- 新增**公平性指数**卡片：基于被点次数的基尼系数，0–100 分 + 一句话解读（「分布均匀」/「偏向少数人」）
- 成员排行支持「最多 / 最少」切换
- 按科目、按轮次筛选（依赖 P5 的 ClassIsland 上下文）

#### 4.6 历史归档（债 B）

- `AppSettings.HistoryRetentionDays`（默认 180 天）
- 启动时：超期条目移入该班文件夹下 `archive/{yyyyMM}.json`，`classroom.json` 只留近期
- 统计页加「包含归档数据」开关，勾选时才加载归档文件
- 好处：`state.json` 不再无限增长，每次抽取的全量保存开销可控

#### 4.7 快捷键录制（债 D）

- 新建 `HotkeyRecorderControl`：聚焦后按下组合键即录制，显示为 `Ctrl + Alt + P`
- 支持修饰键任意组合 + A–Z / 0–9 / F1–F12
- `GlobalHotkeyService.TryParse` 改为解析结构化的 `HotkeyDefinition`（modifiers + vk），不再只认 `Ctrl+Alt+字母`
- 注册失败（被占用）时在 UI 上明确红字提示，而不是只写日志
- 新增第二个热键槽：投影抽取（与快捷抽取分开）

---

### P5 · 键盘操作与 ClassIsland 联动（编号 10 / 16）

#### 5.1 键盘 / 翻页笔（编号 10）

| 按键 | 行为 | 生效范围 |
|---|---|---|
| `空格` / `Enter` | 开始抽取 | 点名页、投影窗口 |
| `Esc` | 关闭结果 / 取消 | 投影窗口 |
| `R` | 换一个 | 结果显示中 |
| `1`–`4` | 切换点名模式 | 点名页 |
| `+` / `-` | 增减抽取人数 | 点名页 |
| `PageDown` / `PageUp` | 抽取 / 关闭（翻页笔常见映射） | 投影窗口 |

实现放在 `DrawPage` 与 `QuickDrawResultWindow` 的 `KeyboardAccelerator` / `KeyDown`；焦点在输入框时不拦截。

#### 5.2 ClassIsland 深度联动（编号 16）

现在协议是单向发通知。扩展为双向：

- 协议加 `context-query` / `context-push` 消息类型，`Version` 升到 2
- 插件侧读取 ClassIsland 当前课程信息（科目、第几节、上下课状态），推送给主程序
- 主程序据此：
  - **历史带上下文**：写入 `Subject` / `PeriodIndex`，统计页可按科目、按课时筛选
  - **下课自动收尾**：可选「下课时结束本轮 + 清空今日请假」
- 投影抽取也可从 ClassIsland 侧触发（现在只能主程序发起）
- **不做自动切班**——班级切换保持手动，避免误切到错误班级后污染数据

**版本不匹配处理**：协议版本不一致时**明确提示用户升级插件**——主程序状态栏显示可关闭的提示条（「ClassIsland 插件版本过旧，课时信息不可用，请更新插件」），插件侧也在其设置页显示同类提示。基础的抽取结果通知仍按 v1 方式正常工作，不因版本差异中断。

**验收**：ClassIsland 处于语文课第 3 节 → PickWeight 抽取 → 历史记录带「周三第 3 节 · 语文」→ 统计页可按语文筛选；插件降级到旧版后出现升级提示且通知仍能送达。

---

## 四、测试计划

| 层 | 新增测试 |
|---|---|
| `PickWeight.Core.Tests` | v1→v2 迁移；班级名校验（非法字符/保留名/重名/长度）；重命名文件夹失败回滚；轮转不重复 + 池空翻轮；请假过滤与次日恢复；座位列模型（增删列、改人数截断、过道、靠门镜像）与改名同步；归档切分；粘贴导入解析；评价降权倍率与下限保护 |
| `PickWeight.App.Tests` | 换一个的次数回滚与并发保护；班级切换时草稿不串、隐式权重路径重绑；投影多人展示流程 |
| `PickWeight.ClassIsland.Protocol.Tests` | v2 协议编解码；版本不匹配时通知仍可送达且产生升级提示；上下文消息校验 |

现有 `DrawServiceTests` / `PersistenceTests` 需同步更新。

## 五、风险与注意点

1. **迁移是唯一不可逆环节**——必须先备份 `state.json.v1.bak`，迁移失败时回退到只读模式并提示用户，不能静默丢数据。
2. **班级名即文件夹名，是最脆弱的一环**。重命名要做 `Directory.Move` + `state.json` 更新两步，中途失败必须整体回滚。文件夹被用户手工删除或改名时，启动扫描要能容错。中文班级名在 NTFS 上没问题，但要注意大小写不敏感导致的"重名"（`1班` vs `1班` 全角半角差异需归一化后比较）。
3. **隐式权重配置搬家**——从发布包 `Data/` 移到用户数据目录后，**升级安装不会再覆盖用户的配置**（这是好事），但要确认打包脚本 `scripts/package-release.ps1` 不再把它当必需文件。原文件保留不删，便于回退。
4. **班级切换与进行中的抽取冲突**——切换前要走 `DrawExecutionCoordinator` 的 lease 检查，有未确认的选择模式结果时先提示处理。
5. **座位表与成员改名/删除**——必须挂到 `AppDataService.ReplaceMembersAsync` 的 renames 流程，否则会出现指向已删成员的座位。删除成员时对应座位应变回空位而不是留下悬空姓名。
6. **移除鼠标钩子后**——紧凑模式的失焦关闭要重测，确认没有窗口关不掉的情况。
7. **语音报名**——需要在语音包缺失 / 静音的机器上静默降级，不能抛异常打断抽取。中文语音包在部分精简系统上不存在，要先探测再决定是否显示该开关。
8. **评价降权的可解释性**——权重被自动改动过的成员，应在成员页有可见标记（比如"已按课堂表现调整"），否则老师会困惑"我明明设的 100 怎么变了"。

## 六、建议实施顺序

```
P0 数据地基  ─┐
              ├─→ P1 班级切换 + 课堂闭环  ─→ P2 投影大屏  ─→ P3 座位表
              │                                              │
              └────────────────→ P4 编辑与统计 ──────────────┴─→ P5 键盘 + ClassIsland
```

P4 与 P2/P3 无强依赖，可并行或插空做。P5 的统计筛选依赖 P4.5，放最后。

