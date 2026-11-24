# 参考项目后的宿主生命周期改进

本批参考 RD-InFalsus-Mod 的薄加载入口、明确的停机入口和文件变更处理，补齐本项目两个实际缺口：普通运行可安全停用本次 SDK 会话，以及报告已加载插件包的磁盘变化。新 Loader 已构建并通过相关离线检查，新预览包已产出；未安装或启动游戏。

参考版本固定为 [617153d0183128c5e1d7d60acfd872bbb64cd6e8](https://github.com/REDDRAGON-HL/RD-InFalsus-Mod/tree/617153d0183128c5e1d7d60acfd872bbb64cd6e8)，本地只读副本在 `work/reference-rd-infalsus`，MIT。仅借鉴组织方式，未复制代码、执行其脚本或移用异游戏偏移。普通工程报告，flavor=null；范围为用户授权的本地 SDK 改进。

## 改动与证据

| ID | 原件 | 结论与边界 |
|---|---|---|
| E01 | 参考项目 [Entry.BepInEx.cs](https://github.com/REDDRAGON-HL/RD-InFalsus-Mod/blob/617153d0183128c5e1d7d60acfd872bbb64cd6e8/CustomSong/Source/Mod/Entry.BepInEx.cs)及[CustomSongCore.cs](https://github.com/REDDRAGON-HL/RD-InFalsus-Mod/blob/617153d0183128c5e1d7d60acfd872bbb64cd6e8/CustomSong/Source/Mod/CustomSongCore.cs) | 加载入口委托业务 Core，卸载时停机，文件变化在受控生命周期中处理。已有的模块隔离、依赖撤销、ALC 不重复实现。 |
| E02 | [最终冻结](../work/game-seek-20261117/loader-reference/source-freeze-final.json) | 1506项输入；相对已验证 Runtime 候选仅一个产品文件 ToolboxHost.cs 改动，另绑定两个命令/验证脚本。主 README 保护哈希保持。 |
| E03 | [构建与现有检查](../work/game-seek-20261117/loader-reference/build-result-r1.json) | 新 Loader及其依赖 Release构建，包加载20/20、生命周期14/14。Runtime、Adapter等其他 DLL 与先前验证字节相同。 |
| E04 | [实际宿主方法检查](../work/game-seek-20261117/loader-reference/host-checks-r2.json) | 4/4受控宿主场景，执行当前 Loader 的真实 ToolboxHost 方法；真实 managed Host/Dispatcher/Package、受控 Adapter，未运行游戏或构造原生 hook/票。 |
| E05 | [命令脚本检查](../work/game-seek-20261117/loader-reference/script-checks-r1/result.json) | 9/9；请求 ID、大小写归一、参数拒绝、原子写入和 PowerShell 语法，未联系游戏。 |
| E06 | [播放链338](../work/game-seek-20261117/dobby-traversal/checks-runner/checks-r1/result.json)及[实际组入口/出口48](../work/game-seek-20261117/dobby-traversal/group-checks/run-r1/result.json) | 与新包 Runtime 字节相同；另有 Hooks210，共596。自有23-member机制，无stock foundation、Unity、正式adoption、Clock或游戏正向结论。 |
| E07 | [新包回执](../artifacts/delivery-20261117/loader-reference-preview-receipt.json) | 38成员/25运行文件，完整逐字节读回；绑定E02–E06及未改原生字节证据。 |

F01（E01–E04）：原有 Shutdown 只由验证计划触达。现在 `command.json` 的 `shutdown` 在主线程停用会话，Pump 返回后本帧直接停止，后续 Tick 不再访问 Adapter 或发布 FrameUpdate；取消令牌及 Dispatcher 清掉后续排队请求。普通停机不调用 Application.Quit，永久原生桥保留原有生命周期。

F02（E02–E05）：命令 ID 在回调实际完成后才确认，队列满不会永久吞请求。坏 JSON 的错误处理与包扫描独立。最终状态只读取停机后仍合法的 managed 记录，保留实际插件状态、能力详情与顶层清理错误；不重新访问已释放 Adapter。验证脚本必须看到 stopped、实际插件记录和无顶层错误才接受 allStopped，避免空集通过。

F03（E01–E04）：现有 CheckForUpdates 原先未接到运行宿主。现在命令 worker 每5秒读取已加载包的目录和指纹，主线程接收不可变结果，status.json 输出 pluginId/restartRequired/changedFiles。扫描不替换包文件或 DLL，不共享阻塞主线程停机的磁盘锁；取消和发布检查丢弃晚到结果。新增的整个包目录仍需重启后发现。

F04（E04）：4个场景覆盖命令经队列停机、清理一步抛错后继续释放、同帧停机后不 Tick/Frame、剩余工作取消和重复停机；真实包文件新增/修改/删除/恢复；真实 WatchCommands 队列满后同 ID 重试；非法 JSON 存在时仍按5秒周期扫描并发布，最后由有效命令停机。夹具使用真实 ToolboxHost 的受控构造，不覆盖实际游戏对象、native pending drain 或原生资源全释放。首次两项业务断言已走完，但测试目录删除被 Windows 映射 DLL 阻止，原失败及夹具源码保留；仅调整夹具保留自己的映像证据后，4/4通过，产品不变。

P01（E02–E05支持F01/F02）：`原子保存command → 后台读取 → 主线程队列 → shutdown(false) → 逐项停机/取消 → 终态status → 停止本帧剩余工作`。

P02（E02–E04支持F03）：`后台周期包指纹 → immutable view → 主线程发布 → status.packageUpdates`。文件变更不等于插件实例更新；提示的是相对实际加载快照的磁盘差异。

## 产物与使用

新包：[RizToolbox-0.3.0-loader-reference-preview-20261117.zip](../artifacts/delivery-20261117/RizToolbox-0.3.0-loader-reference-preview-20261117.zip)，37326691字节，SHA-256 `63f491e46a5a74b5fc16c2c7d69558e2198a88aa6c7f636ea5bad9fa06b30207`。

新 Loader：55296字节，SHA-256 `fd5222aea38208b8d968bfc2a7f7f99f3258220e3e4b8630d45cc00d2ecf6b57`。Runtime：1716224字节，SHA-256 `df8c42768edaa8cbc6e776c69bd977a33cefd3f580a0618a36b7e2bf294422f2`。

命令及状态字段见[宿主控制说明](Host-control.md)。每步命令、输入、日志和自然退出码见E03–E05；宿主场景必须运行生成的 apphost EXE，使测试状态文件落在测试输出目录。命令脚本最终归一化改动发生在 DLL 构建后，旧脚本已随初始冻结归档，最终脚本由E05独立执行验证，最终冻结明确区分；编译源未在构建后变化。

`shutdownFailures=[]` 仅表示顶层清理步骤未抛异常，内部提供方捕获的失败及原生待退出责任仍须单独检查。本轮没有做新包实机验收。此前游戏分钟级阻塞、真正 Clock Ready、Speed/Seek 与 B01–B19 整体仍未闭合；不使用此前游戏或旧 Loader 结果验收这批宿主行为。
