# 事件父调用与中段观察的联合验证

2026-10-24。**1496/1496、25套；Runtime159/Hooks153/Audio573通过并冻结暂存，未安装。** 新增6条隔离的联合原生回归：真实Dobby父入口→原生函数五字节CALL→mid helper→CLR bridge→broker当前私有凭据→ledger同代次列表采用。固定entry+0x140返回地址由原生标签取得，Dobby后中段CALL保持；空列表/外来递归、worker尾窗口/解绑、采用故障、成功采用后原生SEH和C++异常、跨代旧凭据拒绝均通过。

列表图和持根仍是受控backend，未证明IL2CPP GC或真实游戏对象。生产mid并发指令发布仍未解决，patch未发布；实际listener执行、连续mutation与动态转交/取消、全部消费者/资源退休、完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。

## 已执行的六条联合回归

| 用例 | 实际执行和断言 |
|---|---|
| 正常/空列表/外来递归 | 真Dobby和两个原生helper；实际caller/MethodInfo/mid PC；私有token随代次变化；六次原函数/中段/probe、五次实际context配对，外来MethodInfo不捕获 |
| worker尾窗口/解绑 | 真native worker通过mid后阻塞在original，此时mid=0但parent/binding/probe=1且owner不idle；worker不读图、不采用列表；逻辑detach保留原窗口和配对cleanup |
| 采用故障/原异常 | 采用责任先记录后故障，列表不发布；native原函数仍一次执行；SEH/C++在native catch核原code/kind/payload，abnormal不制造正常After |
| 旧凭据重放 | 真实连续调用复用同一个InvokeContext，actual frame相同而token变化；前代凭据在任何新列表采用前被拒绝 |
| 干净owner原生SEH | 冷fixture先成功mid采用，随后original SEH；根责任保留、After0、真实context Dispose和abnormal probe配对，native drain归零 |
| 干净owner原生C++ | 另一个冷fixture先成功采用再抛C++原异常；同样核原code/kind/payload、一次continuation、未伪造正常Returned或释放采用责任 |

六条新用例严格要求三个实际native路径，全部在独立子进程执行；缺依赖会失败，不以SKIP充当通过。新fixture kind4保留旧0–3 ABI，同一个FRAME的首32NOP留给Dobby、中段site=entry+0x13B、返回=entry+0x140。固定测试module由真实entry推导，未放宽Adapter偏移，也未把合成Frame交给ledger。

原生中段自身3559断言继续通过，独立EXE的nativeParentVerified=false仍准确；本批联合证明由[六条Audio新用例](native-parent-mid-evidence.json)单独给出，不倒写旧fixture结论。mid stats只覆盖dispatch，完整窗口依赖native parent。详见[源码复核](review.md)。

## 构建与冻结

440源/18暂存，较1490新增1/修改5/删除0。源码ZIP `9a251c8c6a8d689c7f2af92cd1c58e6b48a31cb2f69fc9ffdeea8463fb2e98c2`；暂存ZIP `004b0f9ae2c3d6d9fca2860354fd7db0c169f672a8929d609ec2bb7c2da7fb00`；full exit0，日志SHA `d84319c2f14832c43aec98118eb0135f8d9fe2f22305c4c7616aebe64c520207`。旧1021安装18项、旧326归档及原游戏三hash匹配。完整日志含0条不同编译诊断，实际信息见汇总，不宣称干净构建无warning。

[构建汇总](build-summary.json)、[完整日志](build.log)、[源码归档](source-archive-check.json)、[发布归档](package-archive-check.json)、[与1490差异](changes-from-1490.json)。执行命令（仓库根、PowerShell7）：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/event-mid-parent-final-20261024`。需要已核哈希的缓存和工具链；完整B19发布矩阵与干净环境复现尚未完成。

23范围/1706指令/6E8及三输入hash继续[重新核对](terminal-evidence-check.json)，沿用1490的[mid静态证据](mid-site-evidence.json)；本批不新增游戏反汇编结论。无安装、游戏/UI操作、游戏进程终止、原游戏文件修改或commit/push；游戏仍1021包。

## 下一步

按[生产安全发布计划](../foundation-event-mid-20261024/next-publication-plan.md)取得可证明的首次执行屏障，再接生产installer并验证actual listener与mutation；当前Loader首次parent安装前不具全局排他证明。继续完整消费者退休和Retry/Return/Loader，而非把本批原生联合通过当作全run关闭凭据。[完整标准](../../../.docs/foundation-expansion/SPEC.md)仅B03整项/B18a子项勾选，用户“实测稍后”保持。[授权范围](scope.md)、[正式报告](../../2026-10-24_底层-事件父链联合验证-report.md)。
