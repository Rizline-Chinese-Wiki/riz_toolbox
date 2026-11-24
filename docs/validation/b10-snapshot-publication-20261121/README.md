# 数据快照的发布与结算更新

一次刷新生成一个完整快照。重复进入刷新会得到 `Conflict`，现有快照仍可读取。采集期间若来源失效或模块释放，本次采集无法发布。若仅本次调用取消，上一次已经验证的数据继续可用。 [快照服务](../../../src/RizToolbox.Adapter/DataSnapshotService.cs)

目录、最佳成绩和设置采集前后核对同一份结算观察。已观察到的结算变化会把旧修订标为陈旧，默认读取返回 `NotReady`。需要继续导出旧修订时，使用同一个 token 和 `AllowStale`。旧页里的成绩和结算都取自当时的快照。分页只读取宿主已复制的结算观察和快照数据。 [模块接线](../../../src/RizToolbox.Adapter/Modules/BuiltinModules.cs) · [公开接口](../../../src/RizToolbox.Sdk/DataContracts.cs)

改前已复现重入刷新、采集失效后仍发布、取消使可用缓存过期，以及释放后仍返回成功。另有定向用例检查同谱旧结算、明确读取旧页、采集期间更换结算和结算来源撤回。 [改前日志](../../../work/b10-snapshot-publication-20261121/before-tests.log) · [定向用例](../../../tests/RizToolbox.Data.Tests/Program.cs)

三个定向 Release 构建成功。Data 30/30、Game 81/81、Module 48/48，共 159 项。Data 套件包括 444 条测试谱面分页和保留修订。构建及检查前后核对 1814 项输入。 [最终结果](../../../work/b10-snapshot-publication-20261121/current-r1/validation.json) · [复现脚本](../../../work/b10-snapshot-publication-20261121/Test-Current.ps1)

此项修复已进入新 V5 候选，包内 DLL 复跑 Data 30/30，独立插件用同源 SDK 编译了结算读取。真实结算和设置的联合表现仍待实机验收。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-result-data-candidate-20261121-r1.zip) · [包内验证](../../../work/v5-result-data-delivery-20261121/packaged-contract-validation-r1.json)
