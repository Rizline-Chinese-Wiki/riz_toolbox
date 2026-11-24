# 原生调用准入与运行时责任

2026-10-24。**1507/1507、25套；Runtime159/Hooks164/Audio573通过并冻结暂存，未安装。** 新增原生调用准入门：Close停止新调用进入CLR，等待已准入的Enter/original/Exit全部完成后，签发绑定槽、全局epoch和OS线程的凭据。同步同槽递归继续；任何void槽内自Close被拒绝；超时自动重开，错误Release不开门。运行时可选私有lease在写尝试/Unknown后Dispose保关闭；Unknown贯通bridge。

独立native fixture实际15次Dobby原调用，656个断言、4次成功关闭、3次timeout、3个回调阶段自关闭拒绝、1次Closing期间递归，最终Active/Parked/InFlight全部0。真实bridge联合用例增加CLR boundary probe：Closed停车时0/0，正确owner释放后1/1，timeout恢复后2/2；错OS线程不放行，首次写尝试后Dispose仍关，RetainUnknown后bridge报告Known=false。11组受控协议用例单列，不代替这两类实际native执行。

首个父detour安装和生产mid安全发布尚未证明，当前patch未发布；真实IL2CPP/listener、连续mutation/provider转交/取消、全部消费者与资源退休、完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。 等待API故障、计数/epoch耗尽未注入，首次安装和游戏实测均未完成。

## 验证与冻结

445源/18暂存，较1496新增5/修改7/删除0。源码ZIP `95d43b33aad52ef66ce7079afe5f2ce6b2d9cf638bc5269d6b6b97d34b38e651`；暂存ZIP `438f50f5e98d9ea1473abdc28c09d9adeb30a6260cde89f1f19d17879ea1edb8`；full exit0，日志SHA `b14808538861beecf1e0685e43b17ddd8ecdc904543564c5cb754780b5d1bf13`。旧1021安装18项、旧326归档及原游戏三hash匹配。最终完整日志0条不同编译诊断；首轮完整通过到Package后runner转义错误退出1，原日志保留，补强CLR probe后重跑最终full exit0。

[构建汇总](build-summary.json)、[完整日志](build.log)、[runner记录](build-run.json)、[首轮日志](build-first-wrapper.log)、[原生准入结果](native-admission-evidence.json)、[源码归档](source-archive-check.json)、[暂存归档](package-archive-check.json)、[与1496差异](changes-from-1496.json)、[有限交叉审查](review.md)。

执行命令（仓库根，PowerShell7）：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/void-admission-final-20261024`。新测试脚本由Build必跑，缺已pin helper/Dobby失败；固定缓存/工具链与干净环境B19复现另行验收。旧的mid3559和父链六条联合用例全部重跑通过，不倒写旧结论。

23范围/1706指令/6E8及三输入hash继续[核对](terminal-evidence-check.json)，[静态证据来源](static-evidence-origin.json)单列；本批不增加游戏调用路径结论。没有安装、游戏/UI操作、游戏进程终止、原游戏文件修改或commit/push，游戏仍1021。

## 后续

[生产接线计划](next-integration.md)：先解决首次执行屏障，再由固定installer签发完整发布结果，才能恢复写后准入并验证actual listener/全run闭合。[完整标准](../../../.docs/foundation-expansion/SPEC.md)仅B03整项/B18a子项勾选，用户“实测稍后”保持。[授权](scope.md)、[正式报告](../../2026-10-24_底层-原生调用准入-report.md)。
