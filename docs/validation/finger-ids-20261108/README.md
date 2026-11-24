# fingerIds 输入集合重建与回退：2026-11-08

`RunValue.PrepareSeekFingerIds(profile, targets)` 已接入正式 backend。每个实际音符必须提供一次目标；真实 null 与空列表分别处理，保留有符号 Int32 极值、重复值和顺序。

新源码通过 **118/118 判定测试（新增 25 组）及 36/36 底层写入回归**。六组件/判定测试/Runtime 测试项目警告数依次为 0/0/1，错误均 0。见[构建结果](build-test-result.json)、[源码复核](source-review.json)和[证据索引](evidence-index.json)。

## 已实现

- 原字段、List、backing、完整容量槽和 signed size/version 分开保留。同 List 别名要求一致目标、仅修改一次；原 null 字段各自分配独立 scratch List。
- Prepare 先复制全部目标、检查别名与容量预算，再调官方构造函数生成未发布空 List。Commit 调官方 Clear/Add 后发布字段。回调先记录 root 责任，撤销不发布；已进入的未发布构造函数仍可能完成。
- Clear 支持两种完整结果：原槽保持，或 active 区全零且 tail 保持；未知 partial 不被接受。此处未证明真实游戏采用哪种实现。
- 非 null 发布前重新核对目标 List；回退重接必须符合原 baseline。外部改动及半恢复对象不会被接回。未发布 scratch 的未知写入责任与原源恢复分开记录。
- Runtime pin 住 Int32 数组，校验官方布局后完整复制并复核；写槽使用真实 boxed Int32 和官方 Array.SetValue。最终托管 guard 后才进入写入，未知 root free 保留 owner、不重试。
- 故障测试覆盖构造、Clear/Add、字段发布、回退、别名、外改、撤销/切局/重入/线程、错误元数据和未知释放；旧 93 组判定保持。

构建前后 626 项输入哈希一致。直接执行本次 D 目录新 DLL，两个测试目录的 16 份 SDK/Core/Runtime/Adapter DLL/PDB 与本次六组件一致。

r1 的实际 116/118 失败结果保留。最终测试按明确的 Unsupported 容量合同核对超限异常，并精确核对撤销后的未确认 publication owner、原异常、null token 和空临时根记录；没有删除用例或放宽为任意异常。独立复核另补正常 Restore 末尾的完整图及原源复核，并增加最后一次 setter 回调换位 allNotes 的回归。

r2 已通过 118/118 与 36/36；后续再补同组内“末尾未发布 scratch 第三态写入失败”的子例，验证原 source 完全恢复仍保留 SideEffectsUnknown、原 pending 和独立 root 责任，无发布或重试。最终源码由 finger-ids-r3 验收。

## 边界与下一步

单 List 最大容量 65536；原存储及预计增长数组聚合预算 1,048,576 槽。超预算及异 List 共享非空 backing 明确 Unsupported，空 backing 可共享。条件采样非原子 CAS，不能排除 ABA；性能后置。

这不是实体设备输入、HoldIdle、目标时间策略、renderer/pool 同步或完整 Seek 的验收。下一步补目标策略和渲染同步，再组合事务；Hold 引擎退出证明仍缺。真实 Speed/Seek 与 B01–B19 未完成。

系统在 11 月 8 日 00:30 关机、05:18 启动；旧 typed 五例 r2 的三个进程已不存在，case 停在 checkpoint 36、最终 DTO 仍占位，按中断未通过保留，未重启该链。未安装/启动游戏或操作账号/UI。11 月 7 日预览 ZIP 保持原哈希，新源码未入旧包，也未借旧包验收。
