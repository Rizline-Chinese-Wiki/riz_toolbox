# 七个独立对象根、实际 API 读回与 stock 凭据联合

2026-10-31，`flavor=null`。本增量基于[writer生命周期](LIFETIME.md)，在独立D staging完成；生产树未修改，未安装/启动游戏或操作游戏UI/账号。它把已经实现的native stock/leaf/writer凭据接到现有`NativePlaybackActualSource`的七个独立根和实际`NativeRuntime`调用，仍不签run/scene admission、完整provider或Clock。

原件根为 `D:/riz-build-cache-next/playback-stock-rooted-source-stage-r1-20261031/`。`completion-r1.json` SHA `ff57eb02f23d788d21b7988846a103638508b71153807a603067921d59523173`。最终7进程89检查全部actual exit0，managed-build-r3为0 warning/0 error，5组PE/PDB与464个源文档匹配；744源归档、9文件diff和841文件哈希索引在`handoff-r1`。native fixture、SingleReturn、Dobby及18导出source helper均复用各自不变的冻结输入，本项没有重编native或借旧结果验新native代码。

## 已实现

- `NativePlaybackStockRootedSource`实际持有controller、information、progress、pause、player、audio source、clip七个独立GC根。根/class/graph和GetTime MethodInfo由已有accessor验证；共同组再核这些实际接收者class与其已封存metadata一致。
- 真正stock Before前后读取独立DSP，真正Returned内先读actual API的media、pitch、clip、nullable origin与游戏字段。Replay/Resume必须独立读到stock reset=1，再经private native reservation恢复所选rate，并用actual API读回2；实际schedule原double来自已证leaf，不能用DSP+delay或origin推算。
- 私有readback receipt由当前真实consumer发行，绑定实际execution/writer票和七根；复制和被取代的旧票拒绝，own writer后另发新readback，旧S2或旧readback不改写fence。
- API读取前后重验native票与graph，额外DSP读保持原始before/sample/after值及单调性。观察到graph更换或DSP回退后，即便字段恢复旧值，旧逻辑票仍不能复活；普通逻辑失效保留健康物理组。
- callback中Dispose只结束逻辑来源并请求释放。新增的drain接缝核本producer真正发行的observations/writer都已完成并释放，七根才在owner线程释放；没有用整个组的in-flight=0替代自己的actual journal责任。

## 实际证据

| Evidence | 原件 | 观察与限定 |
|---|---|---|
| E-ROOTED | `checks-r1/test-result.json`、7组stdout/stderr/exit | normal24、dispose-inflight7、dsp-regression12、external-ABA11、readback-mismatch11、graph-replacement12、getter-writer-race12，总计89 |
| E-API | normal输出与对应测试源 | `NativeRuntime`实际经过synthetic IL2CPP export/metadata delegate读取同一native stock fixture内存；正常Pause/Replay/Resume，24次DSP和10次pitch API读，七根最终均释放 |
| E-WRITER | external-ABA与getter-writer-race输出 | 直接`NativeRuntime.Call(set_pitch)`实际进入detoured native setter；同值和ABA由真实native fence观测，不再沿用旧owned stub的“裸Runtime写者未观测”边界 |
| E-SYMBOLS | `managed-build-r3/`、`symbols-r1/test-result.json` | 实际build0，5PE/PDB464文档、当前源码checksum，符号子进程actual0 |
| E-SOURCE | `handoff-r1/changed-files.json`、`changes.patch`、`implemented-source-snapshot.zip`、`verification-index.json` | 9文件增量，744源归档，841文件哈希；生产树未改 |

测试明确使用metadataSynthetic=true：fixture的可写MethodInfo class在任何broker捕获前与synthetic IL2CPP API的对象class关联，之后由组持续封存；controller/UI字段通过合成export提供，native player/source/progress存储和原stock body、detour、API调用及根句柄真实执行。本结果不能升级为真实IL2CPP metadata或游戏首次加载/运行证明。

F-ROOTED（`n/a_re/high/validated`，E-ROOTED/E-API/E-SYMBOLS）：actual stock occurrence与private native writer可以和七个独立根及API读回构成不可复制的readback链。F-RACE（`n/a_re/high/validated`，E-WRITER/E-ROOTED）：getter期间同值写入、DSP回退、实际native player/source边更换都使当前逻辑票失效，正常setter返回不能替代API readback。P-ROOTED（`path_type=callflow`）：group已封存metadata/class → 七根/class/graph → actual stock Before/leaf/Returned → native writer successor → API DSP/media/pitch/clip/origin读回 → private readback receipt → actual own journal drain → 释放七根；各步对应上述实际源和输出。

`managed-build-r1`保留测试helper错误命名编译失败；r2只有早期normal24正向，不验后来的sticky逻辑修复。最终统一使用r3源码与7进程89结果，没有覆盖失败原件或累计早期检查数。

## 下一缺口

当前Binding中的run/scene/generation是明确的测试关联描述，readback receipt始终声明`RunAuthorityProven=false`；它不是宿主真实run/lifecycle私有票。完整writer覆盖仍限已知入口，下层engine/field writer未封口。尚需宿主真实run/source关联、唯一final Clock与native publication构造/arm顺序、正式provider/module及同一物理安装下不同run的conditional handoff。默认真实Speed/Seek和production保持关闭；不同run/scene/source的移交、任意Seek和B01–B19总验收都未完成。
