# SDK 结构化错误：冻结与发行验证

本批把 SDK 的失败结果统一为 `OperationError`，保留 `Code`、`Message`、`CapabilityId`、`TargetId` 和 `Retryable`。调用者可以判断失败原因，宿主不再从错误文本推断代码。完整 B01–B19 尚未完成。

## 实现范围

- 四种 Result / OperationResult 只通过工厂构造，失败必须包含有效错误；未知数字代码可以透传。
- 服务查找、跨插件代理、目标匹配和修改事务透传同一份结构化错误。已失效作用域、工作线程和私有合同拒绝均在进入 provider 前处理。
- JSON 转换支持嵌套 DTO、camelCase 和未来错误代码，并拒绝矛盾的成功／失败输入。
- Adapter、独立插件示例、模板和现有测试使用当前合同；没有保留字符串失败的旧入口。
- 两个真正由引用包独立构建的插件验证共享代理，不以同程序集的测试类替代插件隔离。

## 实际通过的冻结检查

所有下列目录均位于 `D:/riz-build-cache-next/`，结果文件和 SHA256 见 [evidence-index.json](evidence-index.json)。

| 证据 | 实际结果 | 目录 |
|---|---|---|
| 当前功能快照 | Audio 990、协调器 28、receiver 10 | `foundation-structured-errors-core-r3-20261029` |
| 完整托管矩阵 | 24 套、2139/2139 | `foundation-structured-errors-full-r2-20261029` |
| 发布构建 | 六 DLL、0 错误；1 条既有 generated nullable 警告 | `foundation-structured-errors-release-r2-20261029` |
| 同 helper 的 CLR 采用 | 两个独立映像进程、525 检查 | `foundation-structured-errors-adoption-r2-20261029` |
| SDK 引用包和独立模板插件 | 14/14 | `foundation-structured-errors-distribution-r1-20261029` |

发行新增四组检查覆盖：已声明及未来错误的五字段跨代理保留；工作线程和失效作用域拒绝且 provider 调用数不变；两个插件各自私有合同被明确拒绝；真正抛异常的 provider 只停用自己的插件，另一插件与回调继续工作。普通失败结果不会被误判为插件崩溃。

首轮完整矩阵在 Settings 的两条旧断言上失败，实际为 41/43：旧断言仍期待 `TargetExpired`，新的首次写入失败应为 `MutationWriteFailed`。修复断言后从冻结源码重建，377 份生产源码保持逐字节一致；失效、回滚和不覆盖外部值的断言仍保留。失败目录 `foundation-structured-errors-full-r1-20261029` 没有删除，也没有将旧运行改写为通过。

## 当前候选和范围

候选为 `artifacts/foundation-structured-errors-candidate-r2-20261029`，含 24 份载荷、31 份证据。manifest SHA256：`72e1bfeb8a0d25567bbd0bf1e6710b30ca97b30ad165a7f9f1840c8065ec0647`。六组件来自上述 core-r3 的发布构建，CLR 检查也采用相同冻结输入。

本批没有更改正式 native 代码，所以候选沿用已冻结且源码匹配的 retirement helper：SHA256 `cd34da8cf215ed2e6a22d2330291c85449c2a34456697ee05c0f129dc28abb8e`，77 EAT，当前 V6 / 26 operation / 57 writes / 22 source。来源、引用、退出、准入、映像与 ABI 证明逐项见包内清单和 [上一批报告](../foundation-tween-retirement-20261029/README.md)。没有把历史原生运行宣称为本批重跑。

合成安装与恢复的 **21/21** 项检查已实际终态 exit0，原件 `foundation-structured-errors-install-r1-20261029/test-result.json`。缺失证明、旧 helper、源码篡改及改写外层 hash 的包均拒绝；中断恢复和用户文件保留也实际通过。检查只在临时目录操作，真实游戏仅提供只读 PE 文件。

本批未安装或执行真实游戏，生产准入保持关闭。N2 有值控制／时间转换、N4 持续绑定和 Single V7 正式接线是后续施工，2139 与本候选不能验收它们。
