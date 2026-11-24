# 直接 AudioSource 六入口：离线施工与证据

2026-11-02 21:56：正常Replay／Resume里的真实Stop步骤、以及foreign原函数透传的TLS隔离修正，已在前批57文件基础上再合主16文件；当前894输入／482产品。新n3定向200、独审328、同产品整树2598等已实际通过；主目录fresh验证、完整SDK、n3映像和独立补测继续，整体未完成。详见[Stop修正报告](2026-11-02_stock-stop-occurrence-report.md)和[通俗进度](../../../.docs/FOUNDATION-STATUS.md)。下方为前一889阶段记录，不用于外推带Stop的新流程。

2026-11-02 20:46 更新：六个直接音频入口、旧五leaf、MUG三个控制入口、Progress／Start身份隔离及owner-pump故障传播已组合合入主目录57文件，当前889输入／481产品输入。新产品整树28套2598、solution零警告零错误、独立SDK14、六Release及固定映像108均实际通过；完整SDK worker37＋normal28通过。当前新formal-r3首次／同局／跨局／foreign及两个native入口已通过，剩余十项长测继续，尚不签本批整体完成。主目录fresh编译／定向81／5PE-PDB512正由main-verification-r1核对。

组合状态与可复现入口见[本次主目录整合报告](2026-11-02_combined-main-integration-report.md)；Progress实现及owner-pump修复见[专项报告](2026-11-02_progress-control-identity-report.md)。未安装或启动游戏，未操作游戏UI或账号，生产Speed/Seek关闭。下方为14:08时的六入口原批次记录，旧n5结果只签它自己的源码／helper，不能替代新Q/n1组合验收。

## 实现

追加 `AudioPlayHelper`、`AudioPlayDelayed`、`AudioPlay`、`AudioStop`、`AudioStopBoolean`、`AudioSetLoop` 六角色，数值13–18。每个不同物理入口分别保留 metadata、本体、原跳板、Windows RF/UW、实际 Dobby 安装及后续 seal。固定游戏 profile 现在为18个 source 加 GetTime，共19个物理成员；另保留5个只读区间。

`UInt64_1` 为独立 kind7，原值经 RDX 传递；Double 继续使用 XMM1，Boolean 使用实际字节。原 Source V4 的168B frame／136B info不变，source helper进程全局物理槽位扩至32。GetTime使用自己的独立槽位表。

正交 PlayerWatch V2 为120B，在原104B布局末尾追加 AudioSource 与 AudioClass；原字段 Source 继续表示 fence。托管发行借用同一组七个实际 NativeRuntime 根中的 player 和 AudioSource 两个独立 RootHandle。native在同一锁内先匹配 active／pending 精确 receiver，再检查 class、计入原调用责任和推进 fence；相同 class 的其他 source 不发行 playback 观察。

六入口只撤回旧 source、Clock、writer、prepared rate 和 SDK control lease，不发行 stock Pause、schedule、初始化完成或新源代次。私有 direct-attempt fence 在 source-open 后也阻止 action3／11误恢复；正常 Pause／Replay／Resume不被该专用 fence误伤。

## 已实际完成的验证

| 范围 | 实际结果 | 独立原件 |
|---|---|---|
| helper native，含每入口 raw 参数、foreign、worker、观察异常、原函数异常、class、在途、重入、普通及首次 open前后 | 94进程／15556检查，全0 | `playback-direct-audio-native-r1-20261102/checks-n5-r1/test-result.json` |
| 当前helper上的原8role回归 | 14进程／36675检查，全0 | 同目录 `original-eight-n5-r2/test-result.json` |
| 新owned fixture完整19本体、metadata、RF/UW、8确切CALL、未挂接原函数raw参数与SEH | build0；352检查通过，独立记录实际PID和exit0 | `playback-direct-audio-fixture-r1-20261102/profile-run-r5/test-result.json`、`profile-r5/profile.json` |
| Watch V2托管根保留、未知free、首次hold | build零警告／零错误；3进程51检查 | `playback-direct-audio-managed-r1-20261102/checks-r1/test-result.json` |
| 正式共同组native路径 | actual Dobby Prepare/Commit各19；37检查，退出0根 | 同目录 `checks-r3/direct-install.result.json` |
| 正式共同组NativeRuntime.Call六路径 | actual Dobby各19；31检查，6次runtime调用，退出0根 | 同目录 `checks-r3/direct-runtime.result.json` |
| 该871冻结编译源对应 | 5对PE/PDB／502文档 | 同目录 `symbols-r3/test-result.json` |
| 新固定游戏映像metadata／代码pin | actual0／69检查；游戏方法、DllMain、原生对象分配均0 | 同目录 `mapped-r4/metadata.result.json` |
| 新固定映像19成员实际joint安装／publication／撤回 | actual0／39检查，Prepare/Commit各19；与metadata合计108 | 同目录 `mapped-r4/test-result.json` |
| 同 class foreign六入口／worker flood、current worker Unknown及保根；重编后install／runtime | fresh build0／0；4进程92检查全0；foreign退出0根、worker实际退出仍保留7根 | 同目录 `isolation-r6/test-result.json` |
| 当前产品新整树 | 28套2598、solution0 error／0 warning、独立双插件SDK14、六Release全部实际exit0；Release一条既有generated warning | 同目录 `foundation-r1/` |
| 上述已完成范围的hash及当前源码对应 | 实际回核2827个引用文件；foundation872、mapped871、isolation873的全部src/native与当前候选一致，后续测试／runner差异另列 | 同目录 `completed-scopes-r1/test-result.json` |

上表 D 路径均位于 `D:/riz-build-cache-next/`。各测试保留实际命令、PID、exit、stdout/stderr、输入hash和冻结源码。fixture静态检查脚本的早期失败只说明旧检查器漏读 chained RF，已保留r1–r3与诊断；不得把它们描述为新入口产品失败或成功。native n1编译警告、n4测试模式prefix错误，以及托管测试误用0xA0而非实际0x90计数偏移的失败也均保留，后续使用fresh目录验证。

## 固定身份

当前Source helper SHA256：`12318c9f7b42c61ed194c6bd6f02fbcb71dbbe9b75706498bd52444b65205a84`。Single helper：`41fb7294e8b7ca79d100f5f121de8710a28d33e26ea14fb035a985dcd6e6a0d3`。二者来自native `n5`的实际编译输入。

新owned stock fixture为 `625837bd318e00f4976369f82f51813aed74521709f05a2d44b565ad3fdc330a`，来自独立 `f1`。其8个CALL由完整本体和实际chained RF内的C++ body定位；主group只接受这个固定身份及相应return RVA。重新运行recipe不会自动取得此profile。`f1`额外产出的旧Source helper不参与本批组合，组合明确使用上述native n5 helper。

## 当前仍缺

`sdk-r1`用checks-r3现成程序集与冻结helper执行12项“双插件SDK已实际变速后，直接入口在原函数前撤权”的正向场景。首两项native-U64与Runtime-loop已实际通过，其余10项继续原队列；running/PID须续接时重新验证，不能因观察超时重跑。`mapped-r4`及整树均已终态，不再等待旧PID。

还需六入口的正式SDK foreign／worker及first／same／cross source-open组合，随后按文件hash精确合主。isolation-r6的foreign／worker没有运行SDK，仅签其明确native/根责任范围。构造器上一批两个completion读取时机修复仅改变独立测试，已经fresh两进程64实际通过；原失败保留，不改产品回收逻辑。

本批没有证明 Unity 已解析 icall 指针、AudioPlayable 或其他未知lower通道的完整覆盖；watch建立前的host-only阶段也不是七根watch的覆盖范围。生产首次准入、任意Seek的玩法状态重建、effect完成和完整B01–B19仍未完成。
