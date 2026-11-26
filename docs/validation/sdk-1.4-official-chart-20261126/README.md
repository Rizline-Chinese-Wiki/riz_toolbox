# SDK 1.4.0 官方选曲读取验证

插件可以在选曲页读取当前难度对应的 `ChartId`，鼠标浏览时也可以调用。若能确认当前卡片上的原版 Play 按钮，结果还包含按钮是否活跃、能否交互、是否获得焦点。调用方法和失败结果见 [选曲读取接口](../../Ui-input-navigation.md#读取选曲页的当前官方谱面)。

## 从源码运行离线回归

在包含 PR #6 的仓库根目录执行以下命令。选曲回归使用 .NET 8 SDK，作者包回归使用 PowerShell 7.2+。两项检查使用仓库提供的合成数据，可在没有 Steam 或游戏文件的环境中运行。 [公开 PR 与源码](https://github.com/Rizline-Chinese-Wiki/riz_toolbox/pull/6)

```powershell
dotnet run --project ./tests/RizToolbox.UiInput.Tests/RizToolbox.UiInput.Tests.csproj -c Release
pwsh -NoProfile -File ./scripts/Test-PreviewDistribution.ps1 -Case 'author-stale-sdk-feed-keeps-previous-publication'
```

- UI/Input 套件包含 7 项以 `Official chart read` 开头的选曲用例，覆盖调用方按需读取、Play 按钮未聚焦或无法核实时的谱面结果，以及场景或卡片变化时的拒绝。 [选曲回归源码](../../../tests/RizToolbox.UiInput.Tests/OfficialChartSelectionRegression.cs)
- 作者包用例在已有发布件的情况下传入内部校验通过的 SDK 1.3.0 feed 与 1.4.0 模板，确认入口拒绝替换，并核对上一版文件集合及所有文件和发布清单的 SHA-256。该用例应显示 `1/1` 通过，改用 `-Case 'author-*'` 可运行全部作者包检查。 [作者包回归源码](../../../scripts/Test-PreviewDistribution.ps1#L534)

## 验证结果

| 验证 | 结果 |
| --- | --- |
| Release 构建及 UI/Input 用例 | 构建 0 错误、0 警告，88/88 通过。覆盖未聚焦、无 EventSystem、按钮缺失或有歧义、场景与卡片切换，以及原生读取失败。 |
| 外部插件 | 两个插件分别从仅含参考程序集的 NuGet 包还原并编译。无 Play 按钮快照时都能读取谱面 ID。分发测试 14/14 通过。 |
| 版本检查 | r22 的 SDK 1.3.0 宿主在加载插件 DLL 前拒绝了两个声明 `minSdk=1.4.0` 的插件。程序集标识版本仍为 `1.0.0.0`。 |
| 作者包 | 作者包的 12/12 项分发检查通过。 |

离线验收使用的 `RizToolbox.Sdk.1.4.0.nupkg` 的 SHA-256 为 `e43d4c19fcc80637cecb5dc1ae3a0bfe0e518f8c694ca6046a2ee7b4671aea24`。本次针对 Steam Windows x64 构建 `25977441` 实现了原生读取。实际选曲页的谱面和按钮读数仍待验证。

后续对普通作者包入口做了版本混用回归。向 1.4.0 模板传入一份内部校验有效的 1.3.0 引用包，旧入口会发布不可还原的组合。现在入口会在替换产物前拒绝发布，并保留上次成功包的文件和清单。含这项回归的作者包测试通过 13/13 项。
