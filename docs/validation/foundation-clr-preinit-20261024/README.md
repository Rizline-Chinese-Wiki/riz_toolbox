# CLR实际采用、Unknown快照与初始化调用合同

2026-10-24。**1515/1515、25套完整通过，482源/22包冻结，未安装。** 本批补上真实producer到生产CLR adopter的隔离进程接入，确定修复Unknown发布滞后，并执行生产Doorstop C初始化函数。测试总数与上一producer批同为1515，标题和证据目录区分这两个批次；不以总数代表覆盖率。

## 实际通过的范围

| 验证 | 已运行的具体路径 | 证明边界 |
|---|---|---|
| 实际CLR采用 | 59检查、happy/reentry两个独立dotnet进程；真实producer→RequireReady→RuntimeAdopt→broker双sink共享entry；单方/全部解绑后采用、重复/错误target/冲突metadata拒绝 | 两sink均idle，transport调用与managed callbacks全为0；metadata身份受控，不解析真实IL2CPP |
| atomic Unknown | 328检查；writer实际设unknown=1后暂停，reader在其更新详情前连续100次得到Unknown4/1；三写与原身份不变 | 确定验证发布滞后因果，不承诺返回之后的未来状态；不执行游戏函数 |
| 生产init C函数 | 259检查、16独立进程：12正常/原异常/bootstrap异常，4真实failfast；原指针/int/一次调用及0返回不启CLR | ready与original/bootstrap受控；执行同生产C源，未实际运行producer/CoreCLR或游戏冷启动 |

实际CLR用DONT_RESOLVE_DLL_REFERENCES映射固定GameAssembly，只执行helper生产publisher和生产managed adopter。RequireReady与RuntimeAdopt、双sink broker采用是真的，**没有游戏native→CLR回调**；MethodInfo/Class是受控、不解引用的身份值。没有二次Dobby安装，采用前后代码字节不变。另一独立模式真实重入producer后要求Unknown且拒绝生产adopter。

Unknown确定交错使用真实FirstInstall.cpp编入独立EXE，编译期宏只在该fixture暂停atomic写后/详情锁前；生产helper编译不定义宏，无运行时测试mode。没有测试export只是辅助检查，仍须结合生产编译参数与源码清单。

首次完整构建因SEH测试把ExceptionFlags硬编码为0而失败。被动first-chance诊断和外层捕获实际均为128，source RaiseException flags仍为0；现在比较实际first-chance与捕获的code/flags/参数/payload。**只修正测试假设，未修改生产异常路径。** [首轮失败](build-first-seh-assertion-failure.log)与[诊断](init-seh-diagnostic.log)保留，最终full session 57905 exit0。

真实Doorstop冷启动顺序、生产native→CLR观察回调、实际IL2CPP元数据/listener/GC尚未验收；连续mutation/provider转交与取消、未来来源、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。 实测稍后；游戏仍旧1021包。

## 构建、冻结与复现

482项源码/22项暂存包已逐项冻结，较producer1515批新增8/修改7/删除0。source ZIP `2caf27f9443d01102c3374334cfb4f5c61e1ba120ae46b20370e801d3dee8e24`；package ZIP `6d43e363c96d96d032dcc46e6a356eefd122a06f318a06d395a006b30269fbfb`；完整日志SHA `e4876e10fbb5cc2a5e9045f721ae626947f04f7ea284c57c8d4a971a1ed8d797`。 本次完整构建保留92条去重编译诊断，原文见汇总；不宣称零warning或干净环境B19复现。 旧1021安装18项、其326项归档与原游戏三文件hash核对不变；本轮没有安装、游戏/UI操作、游戏进程终止、原文件修改或commit/push。

仓库根PowerShell7：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/clr-preinit-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`。实际映像联合验证需要显式ValidationGameAssemblyPath，不能将省略该参数的构建称为本批验证。缓存工具链依赖与干净环境复现另验。

[完整日志](build.log)、[汇总](build-summary.json)、[runner终态](build-run.json)、[实际CLR采用](managed-adoption-evidence.json)、[atomic Unknown](atomic-unknown-evidence.json)、[init C函数](init-call-evidence.json)、[源码归档](source-archive-check.json)、[包归档](package-archive-check.json)、[与producer1515差异](changes-from-1515.json)、[有限复核](review.md)。

仅B03整项/B18a子项勾选，[完整B01–B19](../../../.docs/foundation-expansion/SPEC.md)继续active。[授权](scope.md)、[下一步](next-integration.md)、[正式报告](../../2026-10-24_底层-CLR启动采用-report.md)。
