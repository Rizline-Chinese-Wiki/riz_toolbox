# 766 包实机证据快照

冻结于 **2026-10-21 04:49:42 UTC（12:49:42 UTC+8）**。日志最后事件为04:49:01.880Z。主代理在冻结前通过computer-use正常Alt+F4退出；本文件只按冻结日志陈述运行行为，退出画面/进程检查由主代理另行记录。

诊断session：`20261021T044121502Z-991c2e0274194ba9838a0e3aa1f1b476`；生命周期session：`7e428cec1fa84a92b37fa6bc7eafd825`；进程48988。原始toolbox.log包含旧session，**不能把10月20日旧日志计入本次**。`current-session.log`按本次启动时间截取967行；`summary.json`的toolboxLine指向完整`toolbox.log`。

## 真实观察

| 行为 | 本轮证据 | 结论与边界 |
|---|---|---|
| Pause | toolbox.log:1191，04:45:10.634Z action0成功；rev13 Paused | 原生调用返回，立即条件观察true；不证明全局结果/提交行为 |
| 暂停时钟 | toolbox.log:1193、1218，相隔约21.07秒 | GameTime/RealTime/SyncedTime均未变，DSP+21.0773秒、host+21.0679秒；`SecondsMappingVerified=false` |
| Resume | toolbox.log:1229、1238，04:45:47.411Z action1成功，rev14 Resumed | 即刻postcondition=false，但后续采样恢复Playing，判定继续产生 |
| 实际长按尾 | toolbox.log:1241，04:45:50.669Z `hold.end` Invocation103 | 同一Note81，OriginalReturned=true，Perfect1→2；RawDelta=-2保留原单位，不能解释为秒或通用成功率 |
| Retry | toolbox.log:1302、1314，04:48:39.038Z action2成功，rev16 RunEnded/Aborted | 匹配原生Retry返回及旧info activeSelf true→false；旧run=`5262224e32fa4f8e8e53b1590104c3c3` |
| 新局身份 | rev21 RunStarted，04:48:42.886Z | 同谱新run=`177d58e9f6e0421bbcfc3a62a1f6bb75`，ControllerGeneration1→2；没有本轮正常Completed或实际结算的证据 |
| TMP_Text.color | 30次新binding发现，32次基线getter读回 | 真实对象发现/基线读取已观察；不证明TMP双插件覆盖/释放顺序或像素输出 |
| Translucent Float材质 | toolbox.log:987–990 | `_Brightness=.7`、`_Vibrancy=1.1`、`_Flatten=.1`克隆读回通过并应用，asset有效；旧MainMenu目标已失效时明确TargetMissing，未创建clone |
| 切场景释放 | toolbox.log:1051–1054 | probe请求恢复并释放托管lease，created1/released1/active0；**adapter同时记录一次Visual cleanup pending，roots/budget retained，不能称物理释放已证明** |

首次run在Retry前最后状态样本为343次判定观察、UnassociatedCalls0，其中timeout-normal79、timeout-challenge217、drag.timeout45、tap-hold.head1、hold.end1。其后新局继续运行，最后hook计数390并且native/original/before/after相等；**不同统计窗口不能合并成“390个独立音符”**。日志只按策略输出部分逐条判定，`loggedJudgementSamples`也不能当成总数。

本次截取范围内`[Error]`/Exception为0行，Warning为13行：8行VisualTargetCapacity（有界扫描延后候选）、4行模块Waiting/Suspended（设置页未打开/启动期目录未加载）、1行上述Visual cleanup pending。旧session仍含ReadHoldNote及VisualCleanupPending异常，原样保留在完整toolbox.log，不因为新局无Error而删除历史失败。

## 可复核文件

- `toolbox.log`：完整原始复制；`current-session.log`：本次时间过滤副本。
- `status.json`：运行末期原始状态，进程已退出后文件仍可能显示末次运行快照，不能拿它单独判断进程存活。
- `session.json`、`events-000001.jsonl`：原始诊断文件。**运行中读取为0字节是缓冲现象；退出后本快照为579,493字节、967条同session记录，序号1–967连续、无解析错误。** `structured-summary.json`验证了原始复制哈希、session、序号和等级统计，与时间过滤日志吻合。
- `successful-launcher-observation.json`：04:41:14实际返回PID48988的启动观察；其中`hostInitializationVerified=false`是启动器自身的边界，后续本轮日志独立证明宿主执行。
- `bepinex-log.txt`：复制时该路径保留的**旧BepInEx日志**，mtime为2026-10-19 05:05:53 UTC，早于本轮；包含Interop生成失败/Fatal，不能冒充本轮启动失败或正常启动证据。`structured-summary.json`内sourceMetadataAfterExit保留源mtime及与冻结哈希一致的检查。
- `summary.json`：完整解析后的控制、时钟、生命周期、TMP、材质、警告；`evidence-manifest.json`：每份原始复制的源路径、读时点、SHA256及读取期间是否变化。

上级目录的`launch-attempt.json`、`launcher-result.json`属于之前Steam未登录的失败启动，保持原样；不得覆盖成此次成功记录。临时probe配置已由主代理恢复原Absent状态，具体字节备份、hash不一致时的拒删、语义复核和最终恢复见[恢复记录](../module-probe-restoration.json)；不在本快照脚本中执行。

此目录固定对应已安装766包。后续如果为原生退休成功增加诊断receipt，必须另建包/运行证据，不能用新源码或receipt改写此次仅有pending警告及probe计数的边界。

复现冻结：运行上级`freeze-runtime-evidence.py`；每次创建新的带UTC时间目录，绝不覆盖旧证据。再以快照绝对路径为参数运行`analyze-structured-evidence.py`核验JSONL及源mtime。这两个脚本只复制/解析日志，不操作游戏、不改配置。
