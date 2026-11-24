# 事件内段上下文验证

2026-10-24。**1490/1490、25套；Runtime159、Hooks153、Audio567完整通过并冻结暂存，未安装。** 新增15组受控回归（Hooks9、Audio6）及独立原生mid fixture的3559个断言。原生桥保存GPR、XMM低128位、RFLAGS、MXCSR和x87控制字；物理凭据限定同一父调用、线程、sink和代次，旧凭据不能跨复用context重放。Adapter只在凭据有效时独立持根并采用执行列表。

生产mid patch未发布：五字节并发指令发布未证明，真实native父调用→mid→managed Adapter联合链路尚未验收。POD列表与受控root不能替代IL2CPP GC或游戏实例；三条实际listener调用、连续mutation、动态转交/取消、全消费者/资源退休及完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。

## 证据与适用范围

| 层次 | 实际证据 | 未证明的内容 |
|---|---|---|
| 固定游戏静态范围 | [mid-site-evidence.json](mid-site-evidence.json)：单函数148条指令、17次.pdata二分探测、一个unwind项，候选五字节NOP在156441B，CALL真实返回1564420 | 全镜像所有间接入边、运行期发布安全 |
| 独立原生mid | [native-mid-evidence.json](native-mid-evidence.json)：3559断言，16进/16出、6次故障containment、22次实际异常context walk、128次受控RtlVirtualUnwind | native parent、真实Binding解绑、IL2CPP列表、游戏执行 |
| 物理父上下文 | [build.log](build.log)中的Hooks153，新增9组：stage/top、foreign、worker、overflow、异常、错序及stale identity | 此批真实mid与Dobby父链联合运行 |
| 列表采用 | 同一日志Audio567，新增6组：同代次、空/重建列表、隔离、拒绝、采用故障和跨代重放 | 生产mid安装、实际listener执行或future-source封口 |

Frame为0x1B0，32个永久配置槽；只读副本不回写寄存器。x87只保存控制字，不宣称数据栈/AVX上半部完整状态。mid计数只覆盖C++ dispatch/callback，ASM恢复和RET还在其后；必须联合父原生完整窗口，不能单凭mid inFlight=0释放根。详见[原生复核](native-review.md)、[运行时复核](runtime-review.md)、[fixture复核](fixture-review.md)。

## 构建与冻结

439源/18暂存，较1475新增8/修改8/删除0；旧1021安装18项、旧326归档和原游戏三文件hash匹配。源码ZIP `a773d9f963b2d3223715765460b91d18be748a561e0178ba5e1363f0205decb1`；暂存ZIP `d850301a4d0355f2f5ce68b5332ccb34cb26fa2e5f7d96463dd2956b0266d9a9`；完整构建exit0，日志SHA `10c5ba2c4f7bada508a46fa3785a77ab64f66116181963962c9a24d632593a82`。完整日志17条不同编译诊断均为测试warning，原始日志保留；首轮MASM XMM尺寸错误已修，定向native及full通过。

在仓库根目录用PowerShell7运行 `scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/event-mid-final-20261024`。完整环境还依赖已核哈希的工具链与缓存；此处未宣称干净环境或全B19发布矩阵复现。[构建汇总](build-summary.json)、[源码归档](source-archive-check.json)、[发布归档](package-archive-check.json)、[与1475差异](changes-from-1475.json)。

沿用23范围/1706指令/6个E8候选并重新核字节和三输入hash，见[静态再核对](terminal-evidence-check.json)；新mid-site证据单列。游戏仍为旧1021包。无安装、游戏/UI操作、游戏进程终止、原游戏文件修改或commit/push。

## 后续

先补[联合父调用fixture](next-parent-fixture-plan.md)，再按[发布边界](next-publication-plan.md)证明首次执行屏障与指令发布。随后验证实际listener与mutation来源。完整目标仍按[B01–B19](../../../.docs/foundation-expansion/SPEC.md)，仅B03整项/B18a子项勾选。用户“实测稍后”保持。授权范围见[scope](scope.md)，正式报告见[事件内段上下文](../../2026-10-24_底层-事件内段上下文-report.md)。
