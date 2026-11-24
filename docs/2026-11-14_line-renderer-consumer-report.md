# 原动态线条 renderer consumer 验收

本批已合入并验收，主树为 922 输入。原 endpoint 持有者可以按原 occurrence 顺序，向同一块工作数组提交动态线条，并在状态仍可识别时反序恢复选定存储。完整 SDK、B01–B19 和真实 Speed/Seek 仍未完成。

## 实现与效果

[渲染命令和恢复流程](../src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.LineRenderer.cs)保留两条不同 Line、重复 occurrence、不同数量和完整数组尾部。实际 synthetic 调用得到 `101 → 6 → 101`；跳过渲染的 occurrence 不提交 count 0。所有相关 Line 的保存数组引用、数量及 plan/consumer 关联都由[同一原 reader](../src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingRendererAccess.cs)观测，不新抓来源、不为每条线替换数组。

原生调用会先保存数组和数量，再可能进入 Unity 渲染。新 journal 区分 admission、实际原生入口和返回；报错后读取实际字段，只有明确进入过的操作才进入恢复序列。外部先写入相同目标、第三种状态、持有者释放和未知临时引用清理均有实际后端用例。

恢复统一逆转保存字段与前缀写入顺序，保存字段按数量、引用依次还原，不重放 SetPositions。较长旧保存数组的数量可先恢复，再恢复数组引用；完整原位数据与 NaN/负零尾部仍保留。实现依据来自[固定声明和反汇编记录](validation/line-renderer-consumer-20261114/evidence/observed-metadata.json)，其中 nested plan 与实际 UnityEngine.LineRenderer 分开校验。

## 本批验证

新主图实际完成 60 个自然子步骤：六组件 Release 零警告、零错误；solution 零错误、55 条既有测试警告。renderer 12/12、原 reader 7/7、endpoint 36/36、source 选择 13/14/25–30 共 8 项，以及 Runtime 默认 375/375 均通过。不是完整 source 30 或全部 SDK 矩阵。

原 controller 42932、工具会话 36814 实际 exit 0；没有另称额外 kernel handle witness。完整 922 源码、控制、原日志、生产/测试直接产物、fixture、固定 metadata/ASM 及前驱失败历史已归档，归档内脚本的 archive-only 复核实际 exit 0。归档共 1232 文件、51,673,838 bytes。

[归档清单](validation/line-renderer-consumer-20261114/validation-summary.json) SHA-256：`24cb6299c64ddc3f07b4838e1c6c7e14771a2cf21c3580153984dc3f3994dbdf`。[主图结果](validation/line-renderer-consumer-20261114/run/result.json) SHA-256：`9bce65b9d00ba82d79002db47372324087178adbf51d825500b88dc37ebd590f`。[独立验收 receipt](../work/line-renderer-consumer-next-20261114/acceptance-receipt.json)另立，保留 application-record 当时的 freshMainPassed=false 历史。

候选 r5 十项通过后，在第 11 项遇到测试合同错误：原 Canvas 子对象本来只允许获取一次。修正测试后，r6 只补测 11/12 两项；这些候选结果没有拼成新主图验收，正式 main-r1 完整重跑了 12 项。源审查为 root 自审，不冒称独立代理 PASS。

## 范围边界与下一步

测试使用实际 backend/reader、synthetic IL2CPP metadata、物理数组及 SEC_IMAGE 原生调用通道；没有执行映射的机器码，没有实际 GameAssembly/body admission 或真实 Unity 效果验证。未安装、启动游戏或操作游戏 UI；README 原字节保留，旧预览 ZIP 未覆盖，也不认证本批新源码。

选定存储恢复不代表 Unity 内部和外部别名消费者已经闭合。AtomicSnapshot、ExternalConsumersClosed、CompleteSeekPermitted 继续为 false。

[下一离线缺口](../work/line-producer-control-next-20261114/HANDOFF.md)是 LineControl 的 startPosition、endPosition、newLinePosition 与 easeType 写入。world-Y cull 前仍保存起止位置，本批的“跳过不写”仅限数组和 renderer 保存字段。Ring 初始化、Hold engine、实际 DSP/pitch 及完整时钟事务仍待补齐。
