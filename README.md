# PickWeight
课堂点名器应用，拥有神秘的好用功能。

PickWeight 是一个使用 C#、WinUI 3 和 MVVM 构建的加权点名器。它支持不放回多人抽取、被点次数修正、可选小组联动、名单导入、本地 JSON 持久化和快捷抽取窗口。

关闭主窗口后程序会常驻系统托盘；双击托盘图标或选择“显示主窗口”即可恢复。

## 构建

要求 Windows 10 1809 或更高版本、.NET 8 SDK，以及 WinUI 3 / Windows App SDK 开发工具。

```powershell
dotnet test tests/PickWeight.Core.Tests/PickWeight.Core.Tests.csproj
dotnet build src/PickWeight.App/PickWeight.App.csproj -p:Platform=x64
dotnet publish src/PickWeight.App/PickWeight.App.csproj -p:Platform=x64 -p:PublishProfile=win-x64
```

发布命令会生成 `dist/PickWeight-win-x64/`。请运行根目录的 `PickWeight.exe`；实际 WinUI 程序、运行库、资源和配置文件均位于 `Data/`。

同时生成主程序压缩包和 ClassIsland 插件发布包：

```powershell
.\scripts\package-release.ps1
```

发布产物结构见 `dist/README.md`。

## 数据文件

- 正式状态保存在 `%LOCALAPPDATA%\PickWeight\state.json`。
- 配置文件固定为发布包中的 `Data/implicit_config.json`。
- 配置由成员名单自动维护：新增成员初始值为 `1.0`，改名保留原值，删除成员会移除对应项。

主状态保存使用临时文件覆盖方式，降低写入中断造成数据损坏的风险。

## 抽取规则

每一步都会重新计算候选人的临时权重，然后按权重随机选择一人并移出当前候选池：

```text
最终概率 = 配置权重归一化概率 × 70% + 程序综合权重归一化概率 × 30%

加权/选择模式的 30% 部分 = 显式权重 × 1 / (1 + 被点次数 × 0.1)

智能随机模式的 30% 部分 = 1 / (1 + 被点次数 × 0.1)

完全随机模式的 30% 部分 = 候选成员均等随机
```

同一轮中已抽到某个启用联动的小组成员后，该组其他成员的倍率增加；四种模式都会应用该规则，多个已触发小组采用乘法叠加，最大为 `100`。

## 许可证

本项目使用 GNU GPL-3.0 许可证。产品思路参考了 [SECTL/SecRandom](https://github.com/SECTL/SecRandom)，当前实现为独立的 C# / WinUI 3 代码。
