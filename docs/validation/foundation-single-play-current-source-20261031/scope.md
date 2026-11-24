# 范围

本包承接 [2026-10-30非空Play合同](../foundation-single-play-nonempty-20261030/scope.md)，仅因当前Runtime/GetTime修复和root新core-r3完整源码快照重新验证同一个 `automatic-duplicates` 模式。

- 显式输入：`D:/riz-build-cache-next/foundation-broker-source-core-r3-20261031/test-result.json`及其全部704 snapshotFiles、对应704 live inputs；native另取当前仓库JudgeContext全部94源码/header/ASM/include。
- 新工作目录：`D:/riz-build-cache-next/single-play-current-manifest-work-20261031`；新执行目录：`D:/riz-build-cache-next/single-play-current-source-r1-20261031`。所有目录新建，旧runner和旧r4/失败原件保持不变。
- 只跑actual outer/nonempty/source/reference/Add/queue的duplicates正向一个模式，不重跑instant/旧七进程/退休，不增加覆盖模式数。
- repository的src/tests零改动；D-only测试接线保留exact变换与hash；repository只新增本报告目录。
- 元数据、注册、图generation/effects、admission/death与RootHandle/barrier后端仍受控；不验证真实游戏/IL2CPP GC/Unitydeath或native Claim/Seal/Unregister。
- 禁止并未执行安装/游戏启动/GUI/账号操作，不使用B账号，不签生产Ready、retirement、根释放或完整B01–B19完成。
