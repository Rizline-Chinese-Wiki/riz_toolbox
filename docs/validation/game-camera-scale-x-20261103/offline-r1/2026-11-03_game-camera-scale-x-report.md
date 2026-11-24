# rig localScale.x 离线增量

当前局相机rig新增独立的横向缩放标量，位置和缩放可以由两个插件同时控制并各自恢复。新候选专项55/55、整树28套2620/2620、SDK独立双插件14/14及六组件Release均实际通过，solution为0错误／56既有警告，11对PE/PDB与990份源码文档逐项一致。主目录产品保持T894／482，新增能力仍为隔离候选；真实Update/render次序、持续可见效果和玩法影响留待实测。

## 范围与来源

范围以 [scope.md](scope.md) 为准。基线是U最终913输入，本包冻结915输入，含485托管产品输入、48原生输入和767个C#文件。差分仅9路径：SDK角色常量，service完整descriptor白名单及属性自身channel/policy，backend双属性独立图和固定方法，新的scale accessor，Module注释，以及四个相机测试路径。

## Evidence → Finding → Path

| 证据 | 原始记录 | 结论 |
|---|---|---|
| E-001 | [固定方法](retained/scale-static-proof-r1/test-result.json)，4方法848字节及RF／完整反汇编 | SetScale逐轴乘B分量并写localScale，Update调用SetScale；只能称单轴缩放，不能声称uniform zoom |
| E-002 | [专项](retained/functional-r1/test-result.json)及camera完整日志 | 两属性可同时覆盖、分别退役；新鲜Y/Z保留，外部基线／优先权／异常回滚／旧票拒绝／全根清理均通过 |
| E-003 | [新整树](retained/foundation-r1/managed-matrix-result.json) | 当前915输入实际跑28套2620项，不用U旧2599或历史相机33代签 |
| E-004 | [构建链](retained/foundation-r1/test-result.json) | fresh solution、独立SDK双作者14项、六组件Release均actual0；功能没有依赖真实游戏执行 |
| E-005 | [PE/PDB](retained/symbols-r1/test-result.json) | 11对CodeView身份与990份实际源码SHA256一致 |

F-001（validated，confidence=high，category=other）：`RigLocalScaleX`／`local-scale-x`为无量纲单轴能力，无游戏数值范围证据，沿用experimental和玩法影响说明。依据E-001、E-002。

F-002（validated，confidence=high，category=design）：位置与缩放各自保留完整当前局图根、identity、physical identity、别名及仲裁通道，单属性退役不会释放另一属性根；backend完整descriptor快照限制候选授予范围。依据E-002、E-003、E-005。

P-001（callflow，E-001→E-002→E-003）：SDK role/query → 当前局唯一target → 共享MutationCoordinator优先权 → 完整图当前性校验 → 新鲜Vector3 getter → 再次图校验 → 只替换X的exact setter → readback；错误或新一代对象拒绝旧票，作用域结束恢复该属性当前基线。

## 复核与待办

[retained-artifacts.json](retained-artifacts.json)记录归档原件与桌面副本的逐项hash；[candidate-source-915.zip](candidate-source-915.zip)保留全部冻结输入。[scope-index.json](scope-index.json)给出准确范围，不能把其passed解释成整体底层或真实游戏通过。

在本报告所在目录，可用 `dotnet retained/functional-r1/source/tests/RizToolbox.GameCamera.Tests/bin/Release/net8.0/RizToolbox.GameCamera.Tests.dll` 重跑55项自有exports检查；完整构建参数和环境在两个归档Run脚本及各launch.json中，复跑须另建目录保护原件。固定游戏文件按853d完整SHA校验，只读取文件，不加载游戏。

下一步等U末SDK及独立最终审计、T剩余六Runtime场景实际闭合，U精确合主后再合V；不可拿V旧基线ZIP覆盖U已验证的Lifecycle修补。完整generation、未知lower、production、Seek、effect及B01–B19仍未完成。
