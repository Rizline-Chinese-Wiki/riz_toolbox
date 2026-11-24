# 设置覆盖的范围读取与停用恢复

滑块范围在完整探测后发布。探测中重入的写入返回 `Conflict`。插件在范围读回途中停用时，随后发出的命令会返回 `ScopeExpired`。设置读取可继续报告已支持的实际字段。 [设置服务](../../../src/RizToolbox.Adapter/Settings/GameSettingsService.cs)

设置写入已经进入原生 setter 时，停用会立即结束公开租约，并将物理释放延至读回结束。原有共享修改协调器决定下一层及恢复值。游戏在覆盖期间改值，随后又在定期重施覆盖时停用插件，最终恢复的是游戏刚写入的值。未知写入继续保留协调器的原有拒绝和读回规则。 [变更通知](../../../src/RizToolbox.Adapter/Settings/GameSettingsService.Changes.cs)

改前在实际服务与 PluginHost 上复现四个问题，包括范围探测重入污染字典、已停止作用域继续写值、setter 执行期间后端释放，以及范围探测期间释放租约引起的空引用。当前检查还覆盖定期重施期间停用及外部基线恢复。 [改前记录](../../../work/b16-setting-lifetime-20261121/before-tests.log) · [用例](../../../tests/RizToolbox.Settings.Tests/Program.cs)

四个定向 Release 构建成功。Settings 48/48、Mutation 21/21、Data 30/30、Module 48/48，合计 147 项。构建前后复核 1814 项输入。修复已进入源码，真实游戏设置 UI、双插件竞争和可见恢复仍待实机验证。 [封存结果](../../../work/b16-setting-lifetime-20261121/current-r1/validation.json)
