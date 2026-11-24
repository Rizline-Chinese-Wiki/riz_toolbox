# 同源 Canvas basis 独立源码复核

本复核在声明范围内完成，当前没有剩余必要阻断。审查代理不是实现、测试或 Program 的作者，只写本目录两个审查文件；没有构建或运行测试，也没有启动游戏/UI、安装部署、操作账号/代理、Git 或旧 ZIP。sourceReviewCompleteForScope=true 只代表源码审查结论，不代表执行通过或完整 Seek。

[机器可读审查记录](C:/Users/71957/Desktop/riz_toolbox/work/canvas-basis-review-20261112/independent-source-review.json) 列出 58 个审查文件的实际 SHA-256、职责和语义范围。部分大文件只审了注明的 section，其余 799 个输入仅列入 dependencyHashManifest，不冒称逐行独立审查。58 项包含单独的合同文档；仅 57 项属于编译输入。

冻结图来自 [final-r1 inputs-before.json](D:/riz-build-cache-next/canvas-basis-20261112/root-r2/final-r1/inputs-before.json)，共 856 项；本代理逐项重新计算后全部一致。该 manifest 的 SHA-256 为 2128730dbf9f09a5ccf534ffb2b9a4a00dfd9452b25b780579ccc6f94cdddda6。实现合同另外标为 contract/document，并按当前文件 SHA-256 核对，未假装它属于构建输入。旧缓存批次的 300/51/6/1001 结果不能认证这份新源码图。

复核覆盖单次 gate/serial、先 reserve 公共 owner、同一 capture/Plan、原 ordinal、原 cache/position owner、managed shared scope、child-local 取消隔离、bootstrap/child lease 的延迟释放、独立 child callback 内 aggregate Dispose、terminal observer 返还注册槽、temporary/terminal unknown cleanup 的原 owner 链与不重试、完整 expected/别名、缓存先于位置、位置先于缓存恢复、两轮联合读回、revision 撤旧证明、不可变 journal 和位置 effects 保责。计数耗尽只保留原 managed owner，没有把它写成 native free unknown。

| 证据 | 静态结论 | 调用路径及修复 |
|---|---|---|
| E-contract：implementation-contract.md；E-source：JSON 中对应文件哈希 | 原两个 child 必须共享实际 capture/Plan，并按各自原责任释放 | RunValue → 预留三个 owner → SharedBasisSource → 两个 child → 缓存 Commit/Confirm → 位置 Execute → 联合样本 |
| E-source：PositionConflict、CanvasPositions.Read、PositionCommands Lifecycle/Execute、Faults | 原位置 reader 抛出真实值冲突时，不能丢 sticky 第三态 | typed raw bits 保留实际样本；完整双 snapshot 检查所有 ordinal/Transform；只有原 pending Transform 的 before/target 例外 |
| E-source：NativeCanvasCacheConflict、Single array reader、CanvasCaches.ReadWrite、cache Lifecycle、CacheConflicts | 首差异恰为 pending 槽时，不能裁掉同次已读的其他第三态 | 保留原完整 first、已读 second prefix、已完成外层 prefix/first，以及 ReadSlot 两份整数组或第二 inner throw 前的第一份；manager 逐 physical slot 核 expected |
| E-source：cache Execute、Faults | native 进入前已创建的尝试记录应保留 | array-before 断言为一条 Attempted=true、NativeEntered/Returned=false、Readback=null，AttemptedWrites 仍为 0 |
| E-inputs：冻结 manifest 与重新计算的 856 个输入哈希 | 源码结论绑定到这一图 | 任一输入变更后应重新判断对应审查范围，不能沿用旧通过数 |

冲突补充严格限定为 typed 双样本及既有最终条件拒绝边界的实际样本证据。它转录已经取得的完整或部分目标样本，不补一次额外 native 扫描，也不承诺任意普通 IO/metadata 异常的通用部分采样日志。普通 getter 失败自身不是第三态；已证明的另槽第三态不会因回到 expected 而清除。新增组内包含 pending before/target 的正例与另槽第三态的反例，审查仅核其源码，未代签运行结果。

新 fixture 对借用的 class/field/method metadata 名称按当前字符串文本复用稳定指针：getter 仍先读取当前 metadata 值，因此 fault 改名会返回新文本。拥有型 type_get_name 继续走 CoTaskMem/Free；native roots、boxed results 与 unknown 责任没有新增释放路径。该变更属于测试 fixture 内存控制，不是生产功能或性能认证。

Program 仍把 16 个新增 basis Cases 加入默认列表，并新增 --canvas-basis-case N 的 1..16 精确选择。主控计划用同一冻结 DLL/输入图把 16 个 case 各跑一个新进程，再在另一个进程跑原 300；本复核不把它写成“16+300 只用两个进程”，也不声称默认 316 单进程已执行。自然退出、逐 case 结果、构建 W/E、Runtime 51/6 和 AudioContent 1001 必须由主控的新运行证据单独记录。

显式成功只覆盖所选 cache/localPosition 的有序完整可观察值。position setter 一旦进入，原 EffectsOwner 和完整来源根仍保留；selected-value 恢复不关闭 Unity 引擎 effects。所有 CompleteSeekPermitted 仍为 false。Unity internal-call、consumer/callback closure、line/ring、缺失 runtime、Hold 引擎退出及完整时钟事务均未由本复核补齐；有序样本也不是原子 CAS，不能排除未观察 writer/ABA。
