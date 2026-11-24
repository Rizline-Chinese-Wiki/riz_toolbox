# CLR启动采用与初始化调用合同验证

2026-10-24，flavor=null，兼容框架工程验证。**1515/1515、25套、482源/22包冻结，未安装。** 本轮实际执行生产RequireReady、RuntimeAdopt与双idle sink broker采用；修复atomic Unknown详情滞后并验证100次确定交错；同生产C初始化函数在16个受控进程保留调用/返回/原异常。真实Doorstop冷启动顺序、生产native→CLR观察回调、实际IL2CPP元数据/listener/GC尚未验收；连续mutation/provider转交与取消、未来来源、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。

按[scope](validation/foundation-clr-preinit-20261024/scope.md)，三代理协作，root唯一builder。GameAssembly只映射、不运行游戏；真实冷启动和完整B01–B19尚未验收。

## Evidence → Finding → Path

source_type为file/log，observed_at为各JSON记录时间及root确认终态时刻。下表content_hash对应链接文件本身；源码/包ZIP另见后面的archiveSha256，避免把归档hash误标成JSON文件hash。

| E-id | source_ref | repro_command或离线限制（仓库根） | content_hash SHA-256 |
|---|---|---|---|
| E-build | [build.log](validation/foundation-clr-preinit-20261024/build.log) | scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/clr-preinit-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll | `e4876e10fbb5cc2a5e9045f721ae626947f04f7ea284c57c8d4a971a1ed8d797` |
| E-managed | [managed-adoption-evidence.json](validation/foundation-clr-preinit-20261024/managed-adoption-evidence.json) | 同完整命令，Test-FirstInstallManaged的happy/reentry独立进程 | `2c9ba06114e581b3a2c87f987273acb0f3a1494aa589b4b90b30302bf4e7810c` |
| E-unknown | [atomic-unknown-evidence.json](validation/foundation-clr-preinit-20261024/atomic-unknown-evidence.json) | 同完整命令，Test-FirstInstallUnknownNative编译真实producer源 | `84f04e74a6e31af9ff8ec64878d312d1408158c098f0e6b071757fabeda9bbd4` |
| E-init | [init-call-evidence.json](validation/foundation-clr-preinit-20261024/init-call-evidence.json) | 同完整命令，Test-DoorstopInitNative的16独立进程 | `ded5b5c34d4b142c47c8a5f07fdf44a0c7544660cbb6c1029cebdb89eafd05b5` |
| E-failed | [build-first-seh-assertion-failure.log](validation/foundation-clr-preinit-20261024/build-first-seh-assertion-failure.log) | 只读取保留红日志；最终构建不覆盖它 | `ce63bc65758804c707526011f45fc25c01e2fc699e6c6851d4a706ff3a110fca` |
| E-diagnostic | [init-seh-diagnostic.log](validation/foundation-clr-preinit-20261024/init-seh-diagnostic.log) | 只读取保留SEH诊断日志 | `c9cdadefc08e0675c2313e06e09e7be8e6f9a2cbbceb9da961149e9fadc9d4d5` |
| E-source | [source-archive-check.json](validation/foundation-clr-preinit-20261024/source-archive-check.json) | 读取冻结记录并逐项核source-inputs.zip | `e0abe845b4b97c4eb2b5b5f7990f6c2cfefda33b25327d26ac759112a402fabd` |
| E-package | [package-archive-check.json](validation/foundation-clr-preinit-20261024/package-archive-check.json) | 读取冻结记录并逐项核staged-package.zip | `8db913af1a9129d273a5a33197c4ba1976bff16c4f5c37e9cd5587012ab59f17` |

| F-id | category / severity | evidence_ids | status / confidence / location |
|---|---|---|---|
| F-managed | other / n/a_re | E-managed,E-build,E-source | validated/high，生产adopter在两个真实CLR子进程接实际publisher；双idle sinks共享entry、解绑/拒绝；零native callback与受控metadata |
| F-unknown | design / n/a_re | E-unknown,E-build,E-source | validated/high，writer已atomic失效且详情锁尚未更新，100读全Unknown；identity/三写不变，独立EXE compile-time seam |
| F-init | other / n/a_re | E-init,E-build,E-source | validated/high，生产C原init调用函数16进程；original一次、真实int/异常、0返回禁bootstrap、实际failfast；ready受控 |
| F-seh-test | design / n/a_re | E-failed,E-diagnostic,E-init | validated/high，仅修测试Flags0假设；first-chance=128、outer=128，实际code/flags/参数/payload保持；生产异常路径不变 |
| F-limit | design / n/a_re | E-managed,E-unknown,E-init | accepted boundary/high，均未运行实际game init/CoreCLR冷启动/IL2CPP metadata/listener/GC；不能扩大为底层完整验收 |

P-adopt，path_type=callflow：隔离CLR进程DONT_RESOLVE映射固定GameAssembly→真实producer发布三处并Ready→生产RequireReady核实际身份→runtime一次Attach→broker采用现slot31并共享双sink→逻辑解绑/再绑定与错误身份拒绝；E-managed/F-managed。该路径native原函数、观察回调和ledger捕获计数都是0，不是游戏事件执行证明。

P-unknown，path_type=callflow：同生产FirstInstall.cpp编入独立EXE→实际固定映像三写Ready→worker真实重入→atomic unknown=1后在详情锁前暂停→主线程等待后进行100次读→最终snapshot同时核atomic返回4/1→释放writer→重入2/三写不变；E-unknown/F-unknown。Unknown确定交错使用真实FirstInstall.cpp编入独立EXE，编译期宏只在该fixture暂停atomic写后/详情锁前；生产helper编译不定义宏，无运行时测试mode。没有测试export只是辅助检查，仍须结合生产编译参数与源码清单。

P-init，path_type=callflow：受控ready/disabled→共享生产C一次性CAS→真实native original(domain指针)一次→normal int0跳过bootstrap、normal nonzero且ready才bootstrap→int原样返回；原SEH/C++不被包装函数捕获。递归/重调/invalid-ready在第二次original前真实failfast。E-init/F-init。此处不执行实际producer、CoreCLR或游戏。

## 失败记录、冻结与余项

首full在ready-original-seh失败：测试假定ExceptionFlags恒为0，但被动first-chance及外层filter实际均128；source RaiseException参数0没有被当作OS返回flags。诊断后改为逐字段比较真实异常记录，仅测试修正，最终session 57905 exit0。首失败和诊断均保留，不追认失败为通过，也不声称任意SEH状态都为128。

482项源码/22项暂存包已逐项冻结，较producer1515批新增8/修改7/删除0。source ZIP `2caf27f9443d01102c3374334cfb4f5c61e1ba120ae46b20370e801d3dee8e24`；package ZIP `6d43e363c96d96d032dcc46e6a356eefd122a06f318a06d395a006b30269fbfb`；完整日志SHA `e4876e10fbb5cc2a5e9045f721ae626947f04f7ea284c57c8d4a971a1ed8d797`。 本次完整构建保留92条去重编译诊断，原文见汇总；不宣称零warning或干净环境B19复现。 旧1021安装18项、326项归档及原游戏三hash不变；本包未安装，实测稍后。对应源码与LGPL交付沿修改版Doorstop保留，干净环境复现另验。

仍须实际native→CLR回调/ledger组合、真实冷启动顺序、IL2CPP/listener/GC、未来来源/取消、所有消费者资源退休与完整Retry/Return/Loader。仅B03整项/B18a子项已验收，[下一阶段](validation/foundation-clr-preinit-20261024/next-integration.md)、[批次索引](validation/foundation-clr-preinit-20261024/README.md)。
